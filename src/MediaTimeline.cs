using System;
using System.Diagnostics;

namespace DynamicIsland
{
    /// <summary>
    /// 播放进度时钟。SMTC 语义以 Microsoft Learn 文档为准：
    /// TimelineProperties.Position 是截至 LastUpdatedTime 的进度；
    /// 正在播放时的估计值 = (Position - StartTime) + max(0, now - LastUpdatedTime) * rate。
    /// 暂停时不加物理流逝。LastUpdatedTime 若是默认值、过旧或明显超前，则不加流逝，
    /// 以免把年份差加成进度。本类不引用 WinRT，便于单独做逻辑测试。
    /// </summary>
    internal sealed class MediaTimeline
    {
        public const double SeekThresholdSeconds = 0.75;
        public const double SnapEpsilonSeconds = 0.05;
        public const double RecentEvidenceSeconds = 1.5;
        public const double SoftCorrectSeconds = 0.25;
        public const double ExternalResyncSeconds = 0.51;
        public const double StrongExternalResyncSeconds = 2.0;

        public enum ApplyKind
        {
            Unusable = 0,
            Ignored = 1,
            Seeded = 2,
            Seeked = 3,
            Corrected = 4,
            Unchanged = 5
        }

        public struct SmtcSnapshot
        {
            public double SnapSec;
            public double EndSec;
            public DateTimeOffset LastUpdated;
            public DateTimeOffset Now;
            public bool Playing;
            public double Rate;
        }

        private readonly object _gate = new object();
        private readonly Stopwatch _clock = new Stopwatch();
        private double _basePos;
        private double _rate = 1.0;
        private bool _hasUsable;
        private bool _hasAnchor;
        private bool _hasSnap;
        private double _lastSnap = -1;
        private DateTimeOffset _lastLut = DateTimeOffset.MinValue;
        private DateTimeOffset _lastEvidenceAt = DateTimeOffset.MinValue;
        private DateTimeOffset _lastAppliedLut = DateTimeOffset.MinValue;
        private DateTimeOffset _lastSeekAt = DateTimeOffset.MinValue;
        private double _lastAppliedPos;
        private double _snapBeforeLastSeek = -1;
        private double _posBeforeLastSeek;

        public void Reset()
        {
            lock (_gate)
            {
                _clock.Reset();
                _basePos = 0;
                _rate = 1.0;
                _hasUsable = false;
                _hasAnchor = false;
                _hasSnap = false;
                _lastSnap = -1;
                _lastLut = DateTimeOffset.MinValue;
                _lastEvidenceAt = DateTimeOffset.MinValue;
                _lastAppliedLut = DateTimeOffset.MinValue;
                _lastSeekAt = DateTimeOffset.MinValue;
                _lastAppliedPos = 0;
                _snapBeforeLastSeek = -1;
                _posBeforeLastSeek = 0;
            }
        }

        public bool HasUsableReport
        {
            get { lock (_gate) return _hasUsable; }
        }

        public double Position
        {
            get { lock (_gate) return LocalUnlocked(); }
        }

        public void SetPlaying(bool playing)
        {
            lock (_gate) SetPlayingUnlocked(playing);
        }

        public bool HasRecentSmtcEvidence(DateTimeOffset now)
        {
            lock (_gate) return HasRecentUnlocked(now);
        }

        public bool AllowsExternalResync(DateTimeOffset now)
        {
            lock (_gate) return !HasRecentUnlocked(now);
        }

        public ApplyKind ApplySmtc(SmtcSnapshot s)
        {
            lock (_gate)
            {
                if (!IsUsableSnapshot(s.SnapSec, s.EndSec))
                {
                    _hasUsable = false;
                    SetPlayingUnlocked(s.Playing);
                    return ApplyKind.Unusable;
                }

                if (s.Rate > 0) _rate = s.Rate;
                double estimate = Estimate(s);
                double local = LocalUnlocked();

                bool snapChanged = !_hasSnap || Math.Abs(s.SnapSec - _lastSnap) > SnapEpsilonSeconds;
                bool lutChanged = _hasSnap
                    && IsLastUpdatedSane(s.LastUpdated, s.Now)
                    && Math.Abs((s.LastUpdated - _lastLut).TotalSeconds) > 0.001;

                // 相同快照反复上报是文档允许的正常状态，只继续本地外推，绝不因它回拉或放弃可用时间轴
                if (_hasSnap && !snapChanged && !lutChanged)
                {
                    _hasUsable = true;
                    SetPlayingUnlocked(s.Playing);
                    return ApplyKind.Unchanged;
                }

                if (IsStaleSnapshotUnlocked(s, estimate))
                {
                    _hasUsable = true;
                    SetPlayingUnlocked(s.Playing);
                    return ApplyKind.Ignored;
                }

                double prevSnap = _lastSnap;
                _lastSnap = s.SnapSec;
                _lastLut = s.LastUpdated;
                _hasSnap = true;
                _hasUsable = true;
                if (snapChanged || lutChanged)
                    _lastEvidenceAt = s.Now;

                if (!_hasAnchor)
                {
                    ParkUnlocked(estimate, s.Playing);
                    NoteAppliedUnlocked(s, estimate);
                    return ApplyKind.Seeded;
                }

                double diff = Math.Abs(estimate - local);
                if (diff > SeekThresholdSeconds)
                {
                    _snapBeforeLastSeek = prevSnap;
                    _posBeforeLastSeek = local;
                    ParkUnlocked(estimate, s.Playing);
                    NoteAppliedUnlocked(s, estimate);
                    _lastSeekAt = s.Now;
                    return ApplyKind.Seeked;
                }

                if ((snapChanged || lutChanged) && diff > SoftCorrectSeconds)
                {
                    ParkUnlocked(estimate, s.Playing);
                    NoteAppliedUnlocked(s, estimate);
                    return ApplyKind.Corrected;
                }

                SetPlayingUnlocked(s.Playing);
                NoteAppliedUnlocked(s, estimate);
                return ApplyKind.Unchanged;
            }
        }

        public bool TryExternalResync(double seconds, DateTimeOffset now, bool playing, double minDiff)
        {
            if (seconds <= 0 || double.IsNaN(seconds) || double.IsInfinity(seconds)) return false;
            if (minDiff < 0) minDiff = 0;
            lock (_gate)
            {
                if (HasRecentUnlocked(now)) return false;
                if (Math.Abs(seconds - LocalUnlocked()) < minDiff) return false;
                ParkUnlocked(seconds, playing);
                return true;
            }
        }

        public static bool IsLastUpdatedSane(DateTimeOffset lut, DateTimeOffset now)
        {
            if (lut.Year < 2000) return false;
            if ((lut - now).TotalSeconds > 5) return false;
            if ((now - lut).TotalHours > 6) return false;
            return true;
        }

        public static bool IsUsableSnapshot(double snapSec, double endSec)
        {
            if (double.IsNaN(snapSec) || double.IsInfinity(snapSec)) return false;
            if (snapSec < -0.01) return false;
            bool hasRange = !double.IsNaN(endSec) && endSec > 0.5;
            bool hasPos = snapSec > SnapEpsilonSeconds;
            if (!hasRange && !hasPos) return false;
            if (hasRange && snapSec > endSec + 5) return false;
            return true;
        }

        public static double Estimate(SmtcSnapshot s)
        {
            double snap = s.SnapSec < 0 ? 0 : s.SnapSec;
            double rate = s.Rate > 0 ? s.Rate : 1.0;
            double pos = snap;
            if (s.Playing && IsLastUpdatedSane(s.LastUpdated, s.Now))
            {
                double elapsed = (s.Now - s.LastUpdated).TotalSeconds;
                if (elapsed > 0) pos = snap + elapsed * rate;
            }
            if (s.EndSec > 0 && pos > s.EndSec) pos = s.EndSec;
            if (pos < 0) pos = 0;
            return pos;
        }

        /// <summary>当前句索引：正常播放走 hint 快路径，快进/快退时回退二分，支持重复时间戳（取最后一个 Time &lt;= p）。</summary>
        public static int LineIndexAt(double[] times, double p, ref int hint)
        {
            if (times == null) return -1;
            int n = times.Length;
            if (n == 0) return -1;
            if (hint >= 0 && hint < n)
            {
                if (times[hint] <= p && (hint + 1 >= n || times[hint + 1] > p))
                    return hint;
                if (hint + 1 < n && times[hint + 1] <= p)
                {
                    int step = 1;
                    while (hint + step < n && times[hint + step] <= p) step <<= 1;
                    int lo2 = hint + (step >> 1), hi2 = Math.Min(n - 1, hint + step - 1), ans2 = lo2;
                    while (lo2 <= hi2)
                    {
                        int mid2 = (lo2 + hi2) >> 1;
                        if (times[mid2] <= p) { ans2 = mid2; lo2 = mid2 + 1; }
                        else hi2 = mid2 - 1;
                    }
                    hint = ans2;
                    return ans2;
                }
            }

            int lo = 0, hi = n - 1, ans = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                if (times[mid] <= p) { ans = mid; lo = mid + 1; }
                else hi = mid - 1;
            }
            hint = ans;
            return ans;
        }

        private double LocalUnlocked()
        {
            double r = _rate > 0 ? _rate : 1.0;
            return _basePos + (_clock.IsRunning ? _clock.Elapsed.TotalSeconds * r : 0);
        }

        private void ParkUnlocked(double pos, bool playing)
        {
            if (pos < 0) pos = 0;
            _basePos = pos;
            _hasAnchor = true;
            _clock.Reset();
            if (playing) _clock.Start();
        }

        private void SetPlayingUnlocked(bool playing)
        {
            if (playing)
            {
                if (!_clock.IsRunning) _clock.Start();
            }
            else if (_clock.IsRunning)
            {
                _basePos = LocalUnlocked();
                _clock.Reset();
            }
        }

        private void NoteAppliedUnlocked(SmtcSnapshot s, double estimate)
        {
            _lastAppliedPos = estimate;
            if (IsLastUpdatedSane(s.LastUpdated, s.Now))
                _lastAppliedLut = s.LastUpdated;
        }

        private bool IsStaleSnapshotUnlocked(SmtcSnapshot s, double estimate)
        {
            if (!_hasAnchor) return false;
            if (IsLastUpdatedSane(s.LastUpdated, s.Now) && _lastAppliedLut != DateTimeOffset.MinValue
                && s.LastUpdated + TimeSpan.FromMilliseconds(20) < _lastAppliedLut)
                return true;
            // 刚接受过一次 seek，只忽略“像是 seek 前那一帧的回声”的旧快照，
            // 不能把用户紧接着的第二次快进/快退也挡住。
            if (_lastSeekAt != DateTimeOffset.MinValue && (s.Now - _lastSeekAt).TotalSeconds < 0.8
                && Math.Abs(estimate - _lastAppliedPos) > SeekThresholdSeconds)
            {
                if (IsLastUpdatedSane(s.LastUpdated, s.Now) && _lastAppliedLut != DateTimeOffset.MinValue
                    && s.LastUpdated > _lastAppliedLut + TimeSpan.FromMilliseconds(20))
                    return false;   // 更新的 LastUpdatedTime，视为新的一次 seek
                bool echoSnap = _snapBeforeLastSeek >= 0
                    && Math.Abs(s.SnapSec - _snapBeforeLastSeek) <= SnapEpsilonSeconds;
                bool echoPos = Math.Abs(estimate - _posBeforeLastSeek) <= SeekThresholdSeconds + 0.5;
                if (echoSnap || echoPos) return true;
            }
            return false;
        }

        private bool HasRecentUnlocked(DateTimeOffset now)
        {
            if (!_hasUsable) return false;
            if (_lastEvidenceAt == DateTimeOffset.MinValue) return false;
            return (now - _lastEvidenceAt).TotalSeconds <= RecentEvidenceSeconds;
        }
    }
}

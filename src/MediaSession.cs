using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Media.Control;

namespace DynamicIsland
{
    /// <summary>
    /// 读 Windows 系统媒体会话（GSMTC）：歌名、歌手、播放状态、封面。
    /// 再拿封面算主色调（给氛围光用），并按「歌名 + 歌手」去网上拉歌词（给歌词行用）。
    /// 所有数据都是真的：抓不到就留空，界面自己退回默认表现，不编造。
    /// </summary>
    public static class MediaSession
    {
        public static volatile bool Playing;
        public static volatile string Title = "";
        public static volatile string Artist = "";
        public static volatile string App = "";
        /// <summary>当前应该显示的那句歌词；没有就空串</summary>
        public static volatile string Lyric = "";

        /// <summary>封面主色 / 副色，0 表示还没有；格式 0xRRGGBB</summary>
        public static int ArtA, ArtB;
        public static volatile bool HasArt;

        private sealed class LrcLine
        {
            public double Time;
            public string Text;
        }

        private static bool _started;
        private static Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager _mgr;
        private static bool _mgrHooked;
        private static bool _mgrTokenSet;
        private static object _mgrToken;
        private static readonly object _hookLock = new object();
        private static GlobalSystemMediaTransportControlsSession _hookedSession;
        private static object _tokTimeline, _tokPlayback, _tokMedia;
        private static bool _hasTokTimeline, _hasTokPlayback, _hasTokMedia;
        private static readonly TypedEventHandler<GlobalSystemMediaTransportControlsSessionManager, CurrentSessionChangedEventArgs> SessionChangedHandler =
            new TypedEventHandler<GlobalSystemMediaTransportControlsSessionManager, CurrentSessionChangedEventArgs>(OnCurrentSessionChanged);
        private static readonly TypedEventHandler<GlobalSystemMediaTransportControlsSession, TimelinePropertiesChangedEventArgs> TimelineHandler =
            new TypedEventHandler<GlobalSystemMediaTransportControlsSession, TimelinePropertiesChangedEventArgs>(OnTimelineChanged);
        private static readonly TypedEventHandler<GlobalSystemMediaTransportControlsSession, PlaybackInfoChangedEventArgs> PlaybackHandler =
            new TypedEventHandler<GlobalSystemMediaTransportControlsSession, PlaybackInfoChangedEventArgs>(OnPlaybackInfoChanged);
        private static readonly TypedEventHandler<GlobalSystemMediaTransportControlsSession, MediaPropertiesChangedEventArgs> MediaPropsHandler =
            new TypedEventHandler<GlobalSystemMediaTransportControlsSession, MediaPropertiesChangedEventArgs>(OnMediaPropertiesChanged);
        private static readonly MediaTimeline _timeline = new MediaTimeline();
        private static int _refreshGen;
        private static readonly List<LrcLine> _lines = new List<LrcLine>();
        private static string _loadedFor = "";
        /// <summary>歌词比音频提前多少秒亮起（ISLAND_LEAD 可调，默认 0.35 秒，符合人耳人声视觉节奏）。</summary>
        private static readonly double Lead = LeadFromEnv();
        private static double LeadFromEnv()
        {
            try
            {
                string s = Environment.GetEnvironmentVariable("ISLAND_LEAD");
                if (s != null) return Math.Max(-0.5, Math.Min(1.0, double.Parse(s)));
            }
            catch { }
            return 0.35;
        }

        public static void Start()
        {
            if (_started) return;
            _started = true;
            try
            {
                // 拉歌词要走 TLS 1.2，老框架默认还是 TLS 1.0，不改会直接握手失败
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                ServicePointManager.Expect100Continue = false;
            }
            catch { }
            Task.Run(new Func<Task>(Loop));
            // 事件订阅是 seek 的主路径；400ms 轮询只作事件失效时的兜底，避免 60ms 密集打系统 API
            Task.Run(new Func<Task>(FollowTimeline));
            // 兜底：播放器完全不上报进度时，靠读它自己画在屏幕上的歌词/时间码来纠正句子
            LyricWatch.Start();
            ScreenClock.Start();
        }

        /// <summary>
        /// 后台读取当前会话时间轴并交给 MediaTimeline。暂停时也读，以便「暂停时跳转」能落锚。
        /// </summary>
        private static async Task FollowTimeline()
        {
            while (true)
            {
                try
                {
                    await Task.Delay(400);
                    var s = _hookedSession;
                    if (s == null)
                    {
                        var mgr = _mgr;
                        if (mgr != null) s = mgr.GetCurrentSession();
                    }
                    if (s == null) continue;
                    ApplyTimeline(s);
                }
                catch { }
            }
        }

        private static void EnsureManager(GlobalSystemMediaTransportControlsSessionManager mgr)
        {
            if (mgr == null) return;
            GlobalSystemMediaTransportControlsSession current = null;
            lock (_hookLock)
            {
                _mgr = mgr;
                if (!_mgrHooked)
                {
                    _mgrHooked = true;
                    // Framework csc 禁止 +=（CS1545），Roslyn 又禁止直接写 add_/remove_（CS0571）。
                    // 通过 WinRtEvent 反射调用同一对 add_/remove_ 并保存 token。
                    _mgrTokenSet = WinRtEvent.TryAdd(mgr, "CurrentSessionChanged", SessionChangedHandler, out _mgrToken);
                }
                try { current = mgr.GetCurrentSession(); }
                catch { }
            }
            AttachSession(current);
        }

        private static void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
        {
            try
            {
                GlobalSystemMediaTransportControlsSession next = null;
                if (sender != null) next = sender.GetCurrentSession();
                AttachSession(next);
            }
            catch { }
        }

        private static void UnhookSessionUnlocked()
        {
            var s = _hookedSession;
            if (s == null)
            {
                _hasTokTimeline = false;
                _hasTokPlayback = false;
                _hasTokMedia = false;
                return;
            }
            if (_hasTokTimeline)
                WinRtEvent.TryRemove(s, "TimelinePropertiesChanged", ref _tokTimeline, ref _hasTokTimeline);
            if (_hasTokPlayback)
                WinRtEvent.TryRemove(s, "PlaybackInfoChanged", ref _tokPlayback, ref _hasTokPlayback);
            if (_hasTokMedia)
                WinRtEvent.TryRemove(s, "MediaPropertiesChanged", ref _tokMedia, ref _hasTokMedia);
        }

        private static void HookSessionUnlocked(GlobalSystemMediaTransportControlsSession s)
        {
            if (s == null) return;
            // 部分订阅失败时保留已成功的，其余靠 400ms 轮询兜底，不能整段放弃事件。
            _hasTokTimeline = WinRtEvent.TryAdd(s, "TimelinePropertiesChanged", TimelineHandler, out _tokTimeline);
            _hasTokPlayback = WinRtEvent.TryAdd(s, "PlaybackInfoChanged", PlaybackHandler, out _tokPlayback);
            _hasTokMedia = WinRtEvent.TryAdd(s, "MediaPropertiesChanged", MediaPropsHandler, out _tokMedia);
        }

        private static void AttachSession(GlobalSystemMediaTransportControlsSession s)
        {
            bool becameIdle = false;
            bool apply = false;
            lock (_hookLock)
            {
                var old = _hookedSession;
                if (object.ReferenceEquals(old, s))
                {
                    apply = (s != null);
                }
                else
                {
                    UnhookSessionUnlocked();
                    _hookedSession = s;
                    if (s == null) becameIdle = true;
                    else
                    {
                        HookSessionUnlocked(s);
                        apply = true;
                    }
                }
            }
            if (becameIdle)
            {
                Idle();
                return;
            }
            if (apply)
            {
                ScheduleSessionRefresh(s);
                ApplyTimeline(s);
            }
        }

        private static bool IsCurrentSession(GlobalSystemMediaTransportControlsSession sender)
        {
            return sender != null && object.ReferenceEquals(sender, _hookedSession);
        }

        private static void OnTimelineChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args)
        {
            try { if (IsCurrentSession(sender)) ApplyTimeline(sender); }
            catch { }
        }

        private static void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
        {
            try { if (IsCurrentSession(sender)) ApplyTimeline(sender); }
            catch { }
        }

        private static void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
        {
            try { if (IsCurrentSession(sender)) ScheduleSessionRefresh(sender); }
            catch { }
        }

        private static void ScheduleSessionRefresh(GlobalSystemMediaTransportControlsSession s)
        {
            if (s == null) return;
            int gen = Interlocked.Increment(ref _refreshGen);
            Task.Run(new Func<Task>(async () => { await RefreshSessionAsync(s, gen); }));
        }

        private static void ApplyTimeline(GlobalSystemMediaTransportControlsSession s)
        {
            if (s == null) return;
            bool playing = Playing;
            double rate = 1;
            try
            {
                var info = s.GetPlaybackInfo();
                if (info != null)
                {
                    playing = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                    rate = ReadPlaybackRate(info);
                }
            }
            catch { }

            if (playing != Playing)
            {
                _timeline.SetPlaying(playing);
                Playing = playing;
            }

            try
            {
                var tl = s.GetTimelineProperties();
                if (tl == null) return;
                double snap = (tl.Position - tl.StartTime).TotalSeconds;
                double end = (tl.EndTime - tl.StartTime).TotalSeconds;
                if (end <= 0)
                {
                    try { end = (tl.MaxSeekTime - tl.StartTime).TotalSeconds; }
                    catch { }
                }
                var shot = new MediaTimeline.SmtcSnapshot();
                shot.SnapSec = snap;
                shot.EndSec = end;
                shot.LastUpdated = tl.LastUpdatedTime;
                shot.Now = DateTimeOffset.UtcNow;
                shot.Playing = playing;
                shot.Rate = rate;
                _timeline.ApplySmtc(shot);
            }
            catch { }

            string dbg = Environment.GetEnvironmentVariable("ISLAND_TL");
            if (dbg != null)
            {
                try
                {
                    int nc;
                    lock (_lines) nc = _lines.Count;
                    string cl = CurrentLine();
                    if (!System.IO.File.Exists(dbg) || (DateTime.Now - System.IO.File.GetLastWriteTime(dbg)).TotalMilliseconds > 800)
                        System.IO.File.AppendAllText(dbg, DateTime.Now.ToString("HH:mm:ss") + " playing=" + Playing + " usable=" + _timeline.HasUsableReport + " pos=" + Position.ToString("F1") + " lines=" + nc + " lyric=" + Lyric + " cur=" + cl + "\n");
                }
                catch { }
            }
        }

        private static double ReadPlaybackRate(GlobalSystemMediaTransportControlsSessionPlaybackInfo info)
        {
            if (info == null) return 1;
            try
            {
                object raw = info.PlaybackRate;
                if (raw == null) return 1;
                if (raw is double)
                {
                    double d = (double)raw;
                    return d > 0 ? d : 1;
                }
                var vp = raw.GetType().GetProperty("Value");
                if (vp != null)
                {
                    object v = vp.GetValue(raw, null);
                    if (v is double)
                    {
                        double d = (double)v;
                        return d > 0 ? d : 1;
                    }
                }
            }
            catch { }
            return 1;
        }

        private static void ReleaseManager()
        {
            lock (_hookLock)
            {
                UnhookSessionUnlocked();
                _hookedSession = null;
                var mgr = _mgr;
                if (mgr != null && _mgrTokenSet)
                    WinRtEvent.TryRemove(mgr, "CurrentSessionChanged", ref _mgrToken, ref _mgrTokenSet);
                _mgrHooked = false;
                _mgr = null;
            }
            Idle();
        }

        private static async Task Loop()
        {
            GlobalSystemMediaTransportControlsSessionManager mgr = null;

            while (true)
            {
                try
                {
                    if (mgr == null)
                        mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                    if (mgr == null) { Idle(); await Task.Delay(1500); continue; }
                    EnsureManager(mgr);

                    var s = mgr.GetCurrentSession();
                    if (s == null)
                    {
                        AttachSession(null);
                    }
                    else
                    {
                        if (!object.ReferenceEquals(_hookedSession, s)) AttachSession(s);
                        await RefreshSessionAsync(s, _refreshGen);
                        ApplyTimeline(s);
                        UpdateLyricLine();
                    }
                }
                catch
                {
                    ReleaseManager();
                    mgr = null;
                }
                await Task.Delay(450);
            }
        }

        private static async Task RefreshSessionAsync(GlobalSystemMediaTransportControlsSession s, int gen)
        {
            if (s == null) return;
            try
            {
                var info = s.GetPlaybackInfo();
                bool playingNow = info != null &&
                    info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                if (playingNow != Playing)
                {
                    _timeline.SetPlaying(playingNow);
                    Playing = playingNow;
                }

                var props = await s.TryGetMediaPropertiesAsync();
                if (gen != _refreshGen && gen != 0) return;
                string t = (props == null || props.Title == null) ? "" : props.Title.Trim();
                string a = (props == null || props.Artist == null) ? "" : props.Artist.Trim();

                bool titleChanged = t != Title;
                bool artistChanged = a != Artist;
                if (titleChanged || artistChanged)
                {
                    bool songChanged = titleChanged && t.Length > 0;
                    Title = t;
                    Artist = a;
                    if (songChanged)
                    {
                        _timeline.Reset();
                        _timeline.SetPlaying(playingNow);
                        _lineHint = -1;
                        lock (_lines) _lines.Clear();
                        Lyric = "";
                    }
                    RefreshArt(props);
                    RefreshLyric(t, a);
                }
            }
            catch { }
        }

        private static void Idle()
        {
            Playing = false;
            Title = "";
            Artist = "";
            App = "";
            Lyric = "";
            HasArt = false;
            ArtA = ArtB = 0;
            lock (_lines) _lines.Clear();
            _loadedFor = "";
            _lineHint = -1;
            _timeline.Reset();
        }

        /// <summary>当前播放到第几秒（播放器给了就用给的，没给就用本地计时）</summary>
        /// <summary>播放器是否真的通过系统上报了播放进度（false 说明只能靠本地计时/截图读时间码）</summary>
        public static bool HasReportedPosition { get { return _timeline.HasUsableReport; } }

        /// <summary>外部（读播放器界面时间码）发现真实进度后，直接把歌词基准对齐过去</summary>
        public static void TryResync(double real)
        {
            double minDiff = _timeline.HasUsableReport
                ? MediaTimeline.StrongExternalResyncSeconds
                : MediaTimeline.ExternalResyncSeconds;
            _timeline.TryExternalResync(real, DateTimeOffset.UtcNow, Playing, minDiff);
        }

        public static bool AllowsExternalResync
        {
            get { return _timeline.AllowsExternalResync(DateTimeOffset.UtcNow); }
        }

        public static double Position
        {
            get { return _timeline.Position; }
        }

        /// <summary>播放器到底给不给真实进度。false 时说明我们只能靠自己的秒表。</summary>
        public static bool HasRealProgress
        {
            get { return _timeline.HasUsableReport; }
        }

        /// <summary>
        /// 屏幕上的歌词文字 → 回锚时间线。
        /// 用于「播放器完全不上报进度」的情况（网易云 / 汽水音乐）：它们自己的速度在系统接口里看不到，
        /// 唯一能看到的就是它画在屏幕上的那句歌词。这里把那句字和 LRC 里已有的句子比对，
        /// 认出来了、并且和我们推算的位置差得够远（说明用户拖了进度条），就直接把基准时间挪到那句上。
        /// 只做「整句级」纠正，精度约 ±1 句，正好满足「跳了要能跟上」而不是要求逐毫秒。
        /// </summary>
        public static bool ResyncFromText(string raw)
        {
            if (!_timeline.AllowsExternalResync(DateTimeOffset.UtcNow)) return false;
            string probe = NormalizeForMatch(raw);
            if (probe.Length < 3) return false;

            lock (_lines)
            {
                if (_lines.Count < 2) return false;
                int best = -1;
                double bestScore = 0, secondScore = 0;
                for (int i = 0; i < _lines.Count; i++)
                {
                    string t = NormalizeForMatch(_lines[i].Text);
                    if (t.Length < 3) continue;
                    double s = Similarity(probe, t);
                    if (s > bestScore) { secondScore = bestScore; bestScore = s; best = i; }
                    else if (s > secondScore) secondScore = s;
                }
                // 两条都要过：绝对相似度够高 + 明显高于第二候选。
                // 只用一个阈值的话，短句里共用几个常用字就可能误判，一误判就是把时间线拽飞。
                if (best < 0 || bestScore < 0.6 || (bestScore - secondScore) < 0.1) return false;

                double target = _lines[best].Time;
                double minDiff = _timeline.HasUsableReport ? MediaTimeline.StrongExternalResyncSeconds : 0.9;
                if (Math.Abs(target - Position) < minDiff) return false;

                if (!_timeline.TryExternalResync(target, DateTimeOffset.UtcNow, Playing, minDiff)) return false;
                _lineHint = best;
                string lg = Environment.GetEnvironmentVariable("ISLAND_LW_LOG");
                if (lg != null)
                {
                    try
                    {
                        System.IO.File.AppendAllText(lg,
                            DateTime.Now.ToString("HH:mm:ss") + " probe=" + probe + " best=" + best +
                            " score=" + bestScore.ToString("F2") + " second=" + secondScore.ToString("F2") +
                            " target=" + target.ToString("F1") + " 回锚\n");
                    }
                    catch { }
                }
                return true;
            }
        }

        /// <summary>去掉空格、标点、符号，只留中日韩文字和字母数字，便于 OCR 结果和 LRC 比对。</summary>
        private static string NormalizeForMatch(string s)
        {
            if (s == null) return "";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (char.IsLetterOrDigit(c)) sb.Append(c);
            }
            return sb.ToString().ToLowerInvariant();
        }

        /// <summary>字符集合的 Jaccard 相似度：歌词句子短、用字重复少，靠这一条就够区分了。</summary>
        private static double Similarity(string a, string b)
        {
            if (a.Length == 0 || b.Length == 0) return 0;
            if (a.Length > b.Length * 3 || b.Length > a.Length * 3) return 0;
            var set = new HashSet<char>();
            foreach (char c in a) set.Add(c);
            int inter = 0;
            var bs = new HashSet<char>();
            foreach (char c in b)
            {
                if (bs.Add(c) && set.Contains(c)) inter++;
            }
            int union = set.Count + bs.Count - inter;
            if (union <= 0) return 0;
            return (double)inter / union;
        }


        /// <summary>随时算出当前该显示的那句歌词（UIS 每 33ms 取一次，避免只在轮询时更新造成「歌词慢半拍」）。</summary>
        /// <summary>这行是不是「制作人员表 / 署名」而不是歌词。</summary>
        private static bool LooksLikeCredits(string t)
        {
            if (string.IsNullOrEmpty(t)) return true;
            if (t.EndsWith("&") || t.EndsWith(",") || t.EndsWith("：") || t.EndsWith(":")) return true;
            if (Regex.IsMatch(t, "^(recording|engineer|producer|mixed|mix|master|mastered|lyrics?|composer|arranger|guitar|bass|drum|drums|strings|piano|keyboard|arrangement|vocal|backing|production|publisher|studio|assistant)\\b", RegexOptions.IgnoreCase)) return true;
            if (Regex.IsMatch(t, "(工程师|制作人|编曲|混音|母带|录音|和声|监制|出品|校对|统筹|企划|作词|作曲)")) return true;
            // 「乐队队长/键盘：某某」「吉他：某某」这类署名：带冒号 + 乐器/职务词
            if (Regex.IsMatch(t, "[:：]") && t.Length <= 40 &&
                Regex.IsMatch(t, "(队长|乐队|键盘|吉他|贝斯|鼓手|鼓|弦乐|打击|和声|伴唱|人声|合唱|演奏|演唱|词曲|编曲|配器|指挥|监制|混音|录音|母带|制作|出品|统筹|企划|美术|封面|摄影|设计|文案|鸣谢|感谢|工作室|录音棚|团队|program|sample|guitar|keyboard|bass|drum|string|vocal|choir|arrange|produc|mix|master|engineer|record|studi)", RegexOptions.IgnoreCase)) return true;            if (Regex.IsMatch(t, "\\bby\\b", RegexOptions.IgnoreCase) &&
                Regex.IsMatch(t, "(arrange|conduct|produc|mix|compos|writ|perform|record|publish|manage|assist|program|string|vocal|guitar|drum|keyboard|company|studio)", RegexOptions.IgnoreCase)) return true;
            if (!Regex.IsMatch(t, "[\\u4e00-\\u9fa5]") &&
                Regex.IsMatch(t, "(arrange|keyboard|guitar|bass|drum|string|vocal|produc|compos|written|writer|mix|master|engineer|record|piano|synth|program|backing|choir|violin|cello|harmony|publisher|studio|assistant)", RegexOptions.IgnoreCase)) return true;
            return false;
        }
        public static string CurrentLine()
        {
            if (Title.Length == 0 && !Playing) return "";
            double p = Position + Lead;
            string cur = "";
            lock (_lines)
            {
                int idx = LineIndexAt(p);
                // 从命中那句往前退几步找最近的非署名行，跳过制作人员表
                for (int i = idx; i >= 0 && i > idx - 4; i--)
                {
                    if (!LooksLikeCredits(_lines[i].Text)) { cur = _lines[i].Text; break; }
                }
            }
            return cur;
        }

        private static int _lineHint;

        /// <summary>
        /// 二分查找“当前时间落在第几句”，并带上次命中位置做提示：
        /// 正常播放时基本一次比较就命中，长时间播放也是 O(1)，
        /// 拖到副歌/切歌时才退回真正的 log2(n) 查找。实现见 MediaTimeline.LineIndexAt。
        /// </summary>
        private static int LineIndexAt(double p)
        {
            int n = _lines.Count;
            if (n == 0) return -1;
            var times = new double[n];
            for (int i = 0; i < n; i++) times[i] = _lines[i].Time;
            int hint = _lineHint;
            int ans = MediaTimeline.LineIndexAt(times, p, ref hint);
            _lineHint = hint;
            return ans;
        }

        private static void UpdateLyricLine()
        {
            if (Title.Length == 0 && !Playing) { Lyric = ""; return; }
            string cur = "";
            lock (_lines)
            {
                if (_lines.Count == 0) { Lyric = ""; return; }
                double p = Position + Lead;   // 轻微提前，抵消 UI 刷新延迟
                int idx = LineIndexAt(p);
                if (idx >= 0) cur = _lines[idx].Text;
            }
            Lyric = cur;
        }

        // ------------------------------------------------------------------
        //  封面取色
        // ------------------------------------------------------------------
        private static void RefreshArt(GlobalSystemMediaTransportControlsSessionMediaProperties props)
        {
            try
            {
                if (props == null || props.Thumbnail == null) { HasArt = false; return; }

                var ras = props.Thumbnail.OpenReadAsync().AsTask().GetAwaiter().GetResult();
                if (ras == null) { HasArt = false; return; }
                int a, b;
                using (var net = ras.AsStreamForRead())
                {
                    var ms = new MemoryStream();
                    net.CopyTo(ms);
                    ms.Position = 0;
                    if (Palette(ms, out a, out b)) { ArtA = a; ArtB = b; HasArt = true; }
                    else HasArt = false;
                    ms.Dispose();
                }
            }
            catch { HasArt = false; }
        }

        /// <summary>从封面里挑两个有区分度的颜色（按色相分桶，取饱和度×亮度最高的两桶）</summary>
        private static bool Palette(Stream s, out int rgbA, out int rgbB)
        {
            rgbA = rgbB = 0;
            try
            {
                s.Position = 0;
                using (var raw = new Bitmap(s))
                using (var small = new Bitmap(40, 40, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(small))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(raw, 0, 0, 40, 40);
                    }

                    double[] weight = new double[12];
                    double[] rSum = new double[12], gSum = new double[12], bSum = new double[12];
                    var data = small.LockBits(new Rectangle(0, 0, 40, 40), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                    try
                    {
                        int stride = data.Stride;
                        byte[] bytes = new byte[stride * 40];
                        System.Runtime.InteropServices.Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
                        for (int y = 0; y < 40; y++)
                        {
                            for (int x = 0; x < 40; x++)
                            {
                                int o = y * stride + x * 4;
                                byte bb = bytes[o], gg = bytes[o + 1], rr = bytes[o + 2], aa = bytes[o + 3];
                                if (aa < 40) continue;
                                double hh, ss, vv;
                                RgbToHsv(rr, gg, bb, out hh, out ss, out vv);
                                if (ss < 0.18 || vv < 0.12) continue;      // 灰/黑/过曝的不要
                                int bucket = (int)(hh / 30.0);
                                if (bucket > 11) bucket = 11;
                                double w = ss * vv;
                                weight[bucket] += w;
                                rSum[bucket] += rr * w;
                                gSum[bucket] += gg * w;
                                bSum[bucket] += bb * w;
                            }
                        }
                    }
                    finally { small.UnlockBits(data); }

                    // 主色
                    int best = -1;
                    double bestW = 0;
                    for (int i = 0; i < 12; i++)
                        if (weight[i] > bestW) { bestW = weight[i]; best = i; }
                    if (best < 0 || bestW <= 0) return false;

                    byte br = (byte)(rSum[best] / bestW), bg = (byte)(gSum[best] / bestW), bb2 = (byte)(bSum[best] / bestW);
                    Color c1 = Color.FromArgb(br, bg, bb2);
                    double h1, s1, v1;
                    RgbToHsv(c1.R, c1.G, c1.B, out h1, out s1, out v1);
                    c1 = HsvToColor(h1, Math.Min(0.82, Math.Max(0.55, s1 + 0.25)), 0.99);
                    rgbA = (c1.R << 16) | (c1.G << 8) | c1.B;

                    // 副色：色相离主色至少 40° 的另一个强桶；没有就把主色转 40° 当副色
                    int second = -1;
                    double secondW = 0;
                    for (int i = 0; i < 12; i++)
                    {
                        double dh = Math.Abs(i * 30 - h1);
                        if (dh > 180) dh = 360 - dh;
                        if (dh < 40) continue;
                        if (weight[i] > secondW) { secondW = weight[i]; second = i; }
                    }
                    Color c2;
                    if (second >= 0 && secondW > bestW * 0.12)
                    {
                        c2 = Color.FromArgb((byte)(rSum[second] / secondW), (byte)(gSum[second] / secondW), (byte)(bSum[second] / secondW));
                        double h2, s2, v2;
                        RgbToHsv(c2.R, c2.G, c2.B, out h2, out s2, out v2);
                        c2 = HsvToColor(h2, Math.Min(0.82, Math.Max(0.5, s2 + 0.2)), 0.95);
                    }
                    else
                    {
                        double h, sat, val;
                        RgbToHsv(c1.R, c1.G, c1.B, out h, out sat, out val);
                        c2 = HsvToColor((h + 40) % 360, sat, 0.92);
                    }
                    rgbB = (c2.R << 16) | (c2.G << 8) | c2.B;
                    return true;
                }
            }
            catch { return false; }
        }

        private static void RgbToHsv(byte r, byte g, byte b, out double h, out double s, out double v)
        {
            double rr = r / 255.0, gg = g / 255.0, bb = b / 255.0;
            double max = Math.Max(rr, Math.Max(gg, bb));
            double min = Math.Min(rr, Math.Min(gg, bb));
            double d = max - min;
            v = max;
            s = max <= 0 ? 0 : d / max;
            if (d <= 0) { h = 0; return; }
            if (max == rr) h = 60 * (((gg - bb) / d) % 6);
            else if (max == gg) h = 60 * ((bb - rr) / d + 2);
            else h = 60 * ((rr - gg) / d + 4);
            if (h < 0) h += 360;
        }

        private static Color HsvToColor(double h, double s, double v)
        {
            h = (h % 360 + 360) % 360;
            double c = v * s;
            double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
            double m = v - c;
            double r, g, b;
            if (h < 60) { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }
            return Color.FromArgb(255,
                (byte)Math.Round((r + m) * 255),
                (byte)Math.Round((g + m) * 255),
                (byte)Math.Round((b + m) * 255));
        }

        // ------------------------------------------------------------------
        //  歌词（按「歌名 + 歌手」去网易云搜，QQ音乐 / 酷狗 的歌名一样能搜到）
        // ------------------------------------------------------------------
        private static void RefreshLyric(string title, string artist)
        {
            if (title.Length == 0) return;
            string key = title + "\u0001" + artist;
            if (key == _loadedFor) return;
            _loadedFor = key;

            Task.Run(async () =>
            {
                try
                {
                    var list = await FetchLrc(title, artist);
                    if (list != null && key == _loadedFor)
                    {
                        _lineHint = -1;
            lock (_lines) { _lines.Clear(); _lines.AddRange(list); }
                    }
                }
                catch { }
            });
        }

        private static async Task<List<LrcLine>> FetchLrc(string title, string artist)
        {
            string q = Uri.EscapeDataString((title + " " + artist).Trim());
            string searchUrl = "https://music.163.com/api/search/get/web?s=" + q + "&type=1&limit=5";
            string searchBody = await Http(searchUrl);
            if (searchBody == null) return null;

            // 搜索结果是一坨 JSON：songs[] 里每个对象才是歌曲，专辑/歌手都有自己的 id
            Dictionary<string, object> root;
            try
            {
                var ser = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                root = ser.Deserialize<Dictionary<string, object>>(searchBody);
            }
            catch { return null; }

            object resObj;
            if (!root.TryGetValue("result", out resObj)) return null;
            var res = resObj as Dictionary<string, object>;
            if (res == null) return null;
            object songsObj;
            if (!res.TryGetValue("songs", out songsObj)) return null;
            // 反序列化出来的数组有时是 object[]、有时是 ArrayList，两种都吃
            System.Collections.IEnumerable songs = songsObj as System.Collections.IEnumerable;
            if (songs == null) return null;

            string id = null;
            int idx = 0;
            foreach (object so in songs)
            {
                var song = so as Dictionary<string, object>;
                if (song == null) { idx++; continue; }
                string nm = song.ContainsKey("name") ? Convert.ToString(song["name"]) : "";
                if (idx == 0 && song.ContainsKey("id")) id = Convert.ToString(song["id"]);
                if (nm != null && nm.Trim() == title.Trim() && song.ContainsKey("id"))
                {
                    id = Convert.ToString(song["id"]);
                    break;
                }
                idx++;
            }
            if (id == null) return null;

            // 搜到的第一首不一定有歌词（翻唱 / BGM / 纯音乐常常没有），多试几首再放弃
            var candidates = new List<string>();
            candidates.Add(id);
            idx = 0;
            foreach (object so in songs)
            {
                var song2 = so as Dictionary<string, object>;
                if (song2 != null && idx > 0 && song2.ContainsKey("id"))
                {
                    string cid = Convert.ToString(song2["id"]);
                    if (!candidates.Contains(cid)) candidates.Add(cid);
                }
                idx++;
                if (candidates.Count >= 4) break;
            }

            foreach (string cid in candidates)
            {
                var got = await FetchLrcById(cid, title);
                if (got != null && got.Count > 0) return got;
            }
            return null;
        }

        private static async Task<List<LrcLine>> FetchLrcById(string id, string title)
        {
            string lrcBody = await Http("https://music.163.com/api/song/lyric?id=" + id + "&lv=-1&kv=-1&tv=-1");
            if (lrcBody == null) return null;
            var lyricMatch = Regex.Match(lrcBody, "\"lyric\":\"((?:[^\"\\\\]|\\\\.)*)\"");
            if (!lyricMatch.Success) return null;

            string raw = Unescape(lyricMatch.Groups[1].Value);
            var list = new List<LrcLine>();
            // 整首歌词的全局偏移（毫秒）：有就用，正负都认
            double offsetSec = 0;
            var offM = Regex.Match(raw, "\\[offset\\s*:\\s*(-?\\d+)\\]", RegexOptions.IgnoreCase);
            if (offM.Success) offsetSec = int.Parse(offM.Groups[1].Value) / 1000.0;

            foreach (string line in raw.Split('\n'))
            {
                var m = Regex.Match(line.Trim(), "^\\[(\\d{1,2}):(\\d{1,2})(?:\\.(\\d{1,3}))?\\](.*)$");
                if (!m.Success) continue;
                double tt = int.Parse(m.Groups[1].Value) * 60.0 + int.Parse(m.Groups[2].Value)
                          + (m.Groups[3].Success ? int.Parse(m.Groups[3].Value.PadRight(3, '0')) / 1000.0 : 0);
                tt += offsetSec;
                // 增强型歌词里的逐字时间戳 <mm:ss.xx> / <0:12.34> 是给卡拉OK用的，去掉只留文字
                string text = Regex.Replace(m.Groups[4].Value, "<\\d{1,2}:\\d{1,2}(?:\\.\\d{1,3})?>", "").Trim();
                if (text.Length == 0) continue;
                if (Regex.IsMatch(text, "^(作词|作曲|编曲|制作人|混音|录音|和声|监制|出品|发行|统筹|企划|吉他|贝斯|鼓|弦乐)\\s*[:：]"))
                    continue;
                // 有些歌词包会在开头塞一行「歌名 - 歌手」，那不是歌词，去掉
                if (text == title) continue;
                if (text.StartsWith(title + " - ") || text.StartsWith(title + "－")) continue;
                if (Regex.IsMatch(text, "^(纯音乐|此歌曲为没有填词的纯音乐).*")) continue;
                // 英文歌词包里的制作人员表（Recording Engineers / Producer / Mix ...）也不是歌词
                // 制作人员表（中英文都常见）：整行没有中文、且含乐器/制作类单词 → 不是歌词
                bool hasCjk = Regex.IsMatch(text, "[\u4e00-\u9fa5]");
                if (!hasCjk && Regex.IsMatch(text, "(arrange|keyboard|guitar|bass|drum|string|vocal|produc|compos|written|writer|mix|master|engineer|record|piano|synth|program|backing|choir|violin|cello|harmony|publisher|studio|assistant)", RegexOptions.IgnoreCase)) continue;
                // 续行特征：以 & 或 , 结尾
                if (text.EndsWith("&") || text.EndsWith(",")) continue;
                // 「XXX by 人名」这种署名行（Strings Arranged & Conducted by 陈珀 …）
                if (Regex.IsMatch(text, "\bby\b", RegexOptions.IgnoreCase) &&
                    Regex.IsMatch(text, "(arrange|conduct|produc|mix|compos|writ|perform|record|publish|manage|assist|program|string|vocal|guitar|drum|keyboard)", RegexOptions.IgnoreCase)) continue;
                list.Add(new LrcLine { Time = tt, Text = text });
            }
            if (list.Count == 0) return null;
            list.Sort((p, q2) => p.Time.CompareTo(q2.Time));
            return list;
        }

        private static async Task<string> Http(string url)
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Referer = "https://music.163.com/";
                req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) DynamicIsland/1.0";
                req.Timeout = 6000;
                req.ReadWriteTimeout = 6000;
                req.Proxy = null;
                using (var resp = (HttpWebResponse)await req.GetResponseAsync())
                using (var st = resp.GetResponseStream())
                using (var sr = new StreamReader(st, Encoding.UTF8))
                    return await sr.ReadToEndAsync();
            }
            catch { return null; }
        }

        private static string Unescape(string s)
        {
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] != '\\' || i + 1 >= s.Length) { sb.Append(s[i]); continue; }
                char n = s[++i];
                switch (n)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'u':
                        if (i + 4 < s.Length)
                        {
                            try { sb.Append((char)Convert.ToInt32(s.Substring(i + 1, 4), 16)); i += 4; }
                            catch { sb.Append("u"); }
                        }
                        break;
                    default: sb.Append(n); break;
                }
            }
            return sb.ToString();
        }
    }
}

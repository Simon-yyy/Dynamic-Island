# -*- coding: utf-8 -*-
"""
对照 src/MediaTimeline.cs 的纯逻辑模型测试。

重要限制：本文件是 Python 模型，不是对 C# 源码的编译或运行。
本环境若无 csc/dotnet，则无法验证 MediaTimeline.cs 已编译通过。
模型按该 C# 文件的算法手写对照，用于锁定语义；若 C# 以后改动未同步这里，测试不能代替编译。
"""
from __future__ import print_function, division
import sys
from datetime import datetime, timedelta, timezone

SEEK_THRESHOLD = 0.75
SNAP_EPS = 0.05
RECENT_EVIDENCE = 1.5
SOFT_CORRECT = 0.25
EXTERNAL_RESYNC = 0.51
STRONG_EXTERNAL = 2.0

UTC = timezone.utc


def dto(year, month, day, hour=0, minute=0, second=0, micro=0):
    return datetime(year, month, day, hour, minute, second, micro, tzinfo=UTC)


def is_lut_sane(lut, now):
    if lut.year < 2000:
        return False
    if (lut - now).total_seconds() > 5:
        return False
    if (now - lut).total_seconds() > 6 * 3600:
        return False
    return True


def is_usable(snap, end):
    if snap != snap or snap == float("inf") or snap == float("-inf"):
        return False
    if snap < -0.01:
        return False
    has_range = (end == end) and end > 0.5
    has_pos = snap > SNAP_EPS
    if not has_range and not has_pos:
        return False
    if has_range and snap > end + 5:
        return False
    return True


def estimate(snap, end, lut, now, playing, rate):
    if snap < 0:
        snap = 0
    if rate <= 0:
        rate = 1.0
    pos = snap
    if playing and is_lut_sane(lut, now):
        elapsed = (now - lut).total_seconds()
        if elapsed > 0:
            pos = snap + elapsed * rate
    if end > 0 and pos > end:
        pos = end
    if pos < 0:
        pos = 0
    return pos


class MediaTimeline(object):
    """与 src/MediaTimeline.cs 对应的可推进时钟模型（虚拟秒表，非真实 Stopwatch）。"""

    def __init__(self):
        self.reset()

    def reset(self):
        self._base = 0.0
        self._run_at = None  # virtual time when clock started, or None
        self._virtual_now = 0.0  # seconds, only for stopwatch
        self._rate = 1.0
        self._has_usable = False
        self._has_anchor = False
        self._has_snap = False
        self._last_snap = -1
        self._last_lut = dto(1, 1, 1)
        self._last_evidence = dto(1, 1, 1)
        self._last_applied_lut = dto(1, 1, 1)
        self._last_seek_at = dto(1, 1, 1)
        self._last_applied_pos = 0.0
        self._snap_before_seek = -1
        self._pos_before_seek = 0.0

    def advance(self, seconds):
        self._virtual_now += seconds

    def _local(self):
        r = self._rate if self._rate > 0 else 1.0
        if self._run_at is None:
            return self._base
        return self._base + (self._virtual_now - self._run_at) * r

    @property
    def position(self):
        return self._local()

    @property
    def has_usable(self):
        return self._has_usable

    def set_playing(self, playing):
        if playing:
            if self._run_at is None:
                self._run_at = self._virtual_now
        elif self._run_at is not None:
            self._base = self._local()
            self._run_at = None

    def _park(self, pos, playing):
        if pos < 0:
            pos = 0
        self._base = pos
        self._has_anchor = True
        self._run_at = self._virtual_now if playing else None

    def _recent(self, now):
        if not self._has_usable:
            return False
        if self._last_evidence.year < 2:
            return False
        return (now - self._last_evidence).total_seconds() <= RECENT_EVIDENCE

    def allows_external(self, now):
        return not self._recent(now)

    def _note(self, lut, now, est):
        self._last_applied_pos = est
        if is_lut_sane(lut, now):
            self._last_applied_lut = lut

    def _stale(self, lut, now, est, snap=None):
        if not self._has_anchor:
            return False
        if (is_lut_sane(lut, now) and self._last_applied_lut.year >= 2000
                and (lut + timedelta(milliseconds=20)) < self._last_applied_lut):
            return True
        if (self._last_seek_at.year >= 2000
                and (now - self._last_seek_at).total_seconds() < 0.8
                and abs(est - self._last_applied_pos) > SEEK_THRESHOLD):
            if (is_lut_sane(lut, now) and self._last_applied_lut.year >= 2000
                    and lut > self._last_applied_lut + timedelta(milliseconds=20)):
                return False
            echo_snap = self._snap_before_seek >= 0 and snap is not None and abs(snap - self._snap_before_seek) <= SNAP_EPS
            echo_pos = abs(est - self._pos_before_seek) <= SEEK_THRESHOLD + 0.5
            if echo_snap or echo_pos:
                return True
        return False

    def apply(self, snap, end, lut, now, playing, rate=1.0):
        if not is_usable(snap, end):
            self._has_usable = False
            self.set_playing(playing)
            return "Unusable"
        if rate > 0:
            self._rate = rate
        est = estimate(snap, end, lut, now, playing, rate)
        snap_changed = (not self._has_snap) or abs(snap - self._last_snap) > SNAP_EPS
        lut_changed = (self._has_snap and is_lut_sane(lut, now)
                       and abs((lut - self._last_lut).total_seconds()) > 0.001)
        if self._has_snap and not snap_changed and not lut_changed:
            self._has_usable = True
            self.set_playing(playing)
            return "Unchanged"
        if self._stale(lut, now, est, snap):
            self._has_usable = True
            self.set_playing(playing)
            return "Ignored"
        prev_snap = self._last_snap
        self._last_snap = snap
        self._last_lut = lut
        self._has_snap = True
        self._has_usable = True
        if snap_changed or lut_changed:
            self._last_evidence = now
        if not self._has_anchor:
            self._park(est, playing)
            self._note(lut, now, est)
            return "Seeded"
        diff = abs(est - self._local())
        if diff > SEEK_THRESHOLD:
            self._snap_before_seek = prev_snap
            self._pos_before_seek = self._local()
            self._park(est, playing)
            self._note(lut, now, est)
            self._last_seek_at = now
            return "Seeked"
        if (snap_changed or lut_changed) and diff > SOFT_CORRECT:
            self._park(est, playing)
            self._note(lut, now, est)
            return "Corrected"
        self.set_playing(playing)
        self._note(lut, now, est)
        return "Unchanged"

    def try_external(self, seconds, now, playing, min_diff):
        if seconds <= 0 or seconds != seconds:
            return False
        if min_diff < 0:
            min_diff = 0
        if self._recent(now):
            return False
        if abs(seconds - self._local()) < min_diff:
            return False
        self._park(seconds, playing)
        return True


def line_index_at(times, p, hint):
    n = len(times)
    if n == 0:
        return -1, hint
    if 0 <= hint < n:
        if times[hint] <= p and (hint + 1 >= n or times[hint + 1] > p):
            return hint, hint
        if hint + 1 < n and times[hint + 1] <= p:
            step = 1
            while hint + step < n and times[hint + step] <= p:
                step <<= 1
            lo2 = hint + (step >> 1)
            hi2 = min(n - 1, hint + step - 1)
            ans2 = lo2
            while lo2 <= hi2:
                mid2 = (lo2 + hi2) >> 1
                if times[mid2] <= p:
                    ans2 = mid2
                    lo2 = mid2 + 1
                else:
                    hi2 = mid2 - 1
            return ans2, ans2
    lo, hi, ans = 0, n - 1, -1
    while lo <= hi:
        mid = (lo + hi) >> 1
        if times[mid] <= p:
            ans = mid
            lo = mid + 1
        else:
            hi = mid - 1
    return ans, ans


def parse_timecodes(text):
    """对照 ScreenClock：全角规范化后取 mm:ss；两段时取较小者作为当前进度。"""
    import re
    if not text:
        return -1
    table = {chr(ord('0') + i): str(i) for i in range(10)}
    table.update({chr(ord('\uff10') + i): str(i) for i in range(10)})
    norm = text.replace('\uff1a', ':').replace('\uff0f', '/')
    out = []
    for ch in norm:
        out.append(table.get(ch, ch))
    norm = ''.join(out)
    ms = re.findall(r'(\d{1,3})\s*:\s*([0-5]\d)', norm)
    if not ms:
        return -1
    first = int(ms[0][0]) * 60.0 + int(ms[0][1])
    if len(ms) == 1:
        return first
    second = int(ms[1][0]) * 60.0 + int(ms[1][1])
    return first if first <= second else second


def almost(a, b, eps=0.02):
    return abs(a - b) <= eps


def check(name, cond, detail=""):
    if not cond:
        raise AssertionError("%s failed %s" % (name, detail))
    print("  ok:", name)


def main():
    t0 = dto(2026, 11, 17, 10, 0, 0)
    failed = 0

    def run(title, fn):
        nonlocal failed
        try:
            fn()
            print("PASS", title)
        except Exception as e:
            failed += 1
            print("FAIL", title, ":", e)

    def test_lut_and_usable():
        now = t0
        check("year1 lut", not is_lut_sane(dto(1, 1, 1, 0, 0, 0), now))
        check("1601 lut", not is_lut_sane(dto(1601, 1, 1, 0, 0, 0), now))
        check("now lut", is_lut_sane(now, now))
        check("future+10", not is_lut_sane(now + timedelta(seconds=10), now))
        check("7h ago", not is_lut_sane(now - timedelta(hours=7), now))
        check("1h ago", is_lut_sane(now - timedelta(hours=1), now))
        check("all zero", not is_usable(0, 0))
        check("tiny snap no end", not is_usable(0.02, 0))
        check("mid no end", is_usable(12.0, 0))
        check("start with duration", is_usable(0.0, 200))
        check("pos way past end", not is_usable(400, 100))
        check("neg", not is_usable(-1, 100))

    def test_estimate():
        now = t0
        lut = now - timedelta(seconds=3)
        check("playing adds elapsed", almost(estimate(10, 200, lut, now, True, 1), 13))
        check("paused no elapsed", almost(estimate(10, 200, lut, now, False, 1), 10))
        check("insane lut no add", almost(estimate(10, 200, dto(1, 1, 1), now, True, 1), 10))
        check("clamp end", almost(estimate(198, 200, lut, now, True, 1), 200))
        check("rate 2", almost(estimate(10, 200, lut, now, True, 2), 16))

    def test_normal_and_frozen():
        tl = MediaTimeline()
        now = t0
        lut = now
        k = tl.apply(10, 200, lut, now, True, 1)
        check("seed", k == "Seeded", k)
        check("seed pos", almost(tl.position, 10))
        tl.advance(2.0)
        check("extrapolate 2s", almost(tl.position, 12))
        # 同一快照反复上报 20 次（旧逻辑会在 2.5s 后放弃上报）
        for i in range(20):
            later = now + timedelta(milliseconds=200 * (i + 1))
            k = tl.apply(10, 200, lut, later, True, 1)
            check("frozen unchanged %d" % i, k == "Unchanged", k)
            tl.advance(0.2)
        check("still usable after frozen", tl.has_usable)
        check("position kept growing", almost(tl.position, 12 + 4.0, 0.05), str(tl.position))

    def test_forward_back_seek():
        tl = MediaTimeline()
        now = t0
        tl.apply(10, 200, now, now, True, 1)
        tl.advance(1)
        now2 = now + timedelta(seconds=1)
        k = tl.apply(80, 200, now2, now2, True, 1)
        check("forward seek", k == "Seeked", k)
        check("forward pos", almost(tl.position, 80), str(tl.position))
        tl.advance(0.5)
        now3 = now2 + timedelta(seconds=0.5)
        # 0.5s still in stale window? new seek with newer lut should work
        k = tl.apply(15, 200, now3, now3, True, 1)
        check("back seek", k == "Seeked", k)
        check("back pos", almost(tl.position, 15), str(tl.position))

    def test_seek_while_paused_and_resume():
        tl = MediaTimeline()
        now = t0
        tl.apply(20, 200, now, now, True, 1)
        tl.advance(2)
        tl.set_playing(False)
        parked = tl.position
        check("paused holds", almost(parked, 22), str(parked))
        tl.advance(5)  # wall time passes while paused
        check("paused no drift", almost(tl.position, 22))
        now2 = now + timedelta(seconds=7)
        k = tl.apply(90, 200, now2, now2, False, 1)
        check("seek paused", k == "Seeked", k)
        check("paused seek parks", almost(tl.position, 90), str(tl.position))
        tl.advance(3)
        check("still parked", almost(tl.position, 90))
        tl.set_playing(True)
        tl.advance(2)
        check("resume from seek", almost(tl.position, 92), str(tl.position))

    def test_same_snapshot_no_jitter():
        tl = MediaTimeline()
        now = t0
        tl.apply(30, 240, now, now, True, 1)
        positions = []
        for i in range(10):
            tl.advance(0.3)
            later = now + timedelta(milliseconds=300 * (i + 1))
            k = tl.apply(30, 240, now, later, True, 1)
            check("no jump kind", k == "Unchanged", k)
            positions.append(tl.position)
        for a, b in zip(positions, positions[1:]):
            check("monotonic", b >= a - 1e-9, "%s %s" % (a, b))
        check("grew about 3s", almost(positions[-1], 33.0, 0.05), str(positions[-1]))

    def test_track_and_session_reset():
        tl = MediaTimeline()
        now = t0
        tl.apply(50, 200, now, now, True, 1)
        tl.advance(2)
        tl.reset()
        check("reset pos 0", almost(tl.position, 0))
        check("reset not usable", not tl.has_usable)
        now2 = now + timedelta(seconds=2)
        k = tl.apply(3, 180, now2, now2, True, 1)
        check("new song seed", k == "Seeded", k)
        check("new song pos", almost(tl.position, 3))

    def test_unusable_and_invalid_lut():
        tl = MediaTimeline()
        now = t0
        k = tl.apply(0, 0, dto(1, 1, 1, 0, 0, 0), now, True, 1)
        check("zero unusable", k == "Unusable", k)
        check("not usable flag", not tl.has_usable)
        # 即使 LUT 是 year1 的 08:00，只要有进度也只能用快照本身，不能加几百年
        k = tl.apply(40, 200, dto(1, 1, 1, 0, 8, 0), now, True, 1)
        check("seed without insane elapsed", k == "Seeded", k)
        check("no year-long jump", almost(tl.position, 40), str(tl.position))
        check("pos not huge", tl.position < 100)

    def test_seek_to_start():
        tl = MediaTimeline()
        now = t0
        tl.apply(40, 200, now, now, True, 1)
        now2 = now + timedelta(seconds=1)
        k = tl.apply(0, 200, now2, now2, True, 1)
        check("seek start usable", k == "Seeked", k)
        check("at zero", almost(tl.position, 0), str(tl.position))

    def test_stale_snapshot_ignored():
        tl = MediaTimeline()
        now = t0
        tl.apply(10, 200, now, now, True, 1)
        now2 = now + timedelta(seconds=1)
        k = tl.apply(80, 200, now2, now2, True, 1)
        check("seeked 80", k == "Seeked" and almost(tl.position, 80), k)
        # 立刻又来一份更旧 LUT 的 10 秒快照
        k = tl.apply(10, 200, now, now2 + timedelta(milliseconds=50), True, 1)
        check("stale ignored", k == "Ignored", k)
        check("not pulled back", almost(tl.position, 80), str(tl.position))

    def test_external_resync_and_recent():
        tl = MediaTimeline()
        now = t0
        tl.apply(10, 200, now, now, True, 1)
        # 刚有 SMTC 证据，OCR 不得拽走
        ok = tl.try_external(80, now + timedelta(milliseconds=200), True, EXTERNAL_RESYNC)
        check("blocked recent", not ok)
        check("still 10", almost(tl.position, 10))
        later = now + timedelta(seconds=2.0)
        tl.advance(2.0)
        ok = tl.try_external(80, later, True, STRONG_EXTERNAL)
        check("allowed after evidence aged", ok)
        check("external applied", almost(tl.position, 80))
        # 暂停外部回锚不应启动秒表
        tl2 = MediaTimeline()
        tl2.try_external(55, later, False, 0.5)
        # no usable, not recent
        check("paused external parks", almost(tl2.position, 55))
        tl2.advance(3)
        check("paused external no drift", almost(tl2.position, 55), str(tl2.position))

    def test_event_miss_then_poll():
        # 模拟 TimelinePropertiesChanged 丢失，400ms 轮询才看到新进度
        tl = MediaTimeline()
        now = t0
        tl.apply(12, 200, now, now, True, 1)
        tl.advance(0.4)
        now2 = now + timedelta(milliseconds=400)
        k = tl.apply(70, 200, now2, now2, True, 1)
        check("poll catch seek", k == "Seeked", k)
        check("poll pos", almost(tl.position, 70))

    def test_line_index():
        times = [0.0, 2.0, 4.0, 4.0, 8.0, 12.0]  # 重复时间戳 4.0
        hint = -1
        # 正常递增
        seq = []
        for p in (0.0, 1.9, 2.0, 3.9, 4.0, 7.9, 8.0, 20.0):
            idx, hint = line_index_at(times, p, hint)
            seq.append(idx)
        check("inc start", seq[0] == 0)
        check("inc before 2", seq[1] == 0)
        check("inc at 2", seq[2] == 1)
        check("dup at 4 takes last", seq[4] == 3, str(seq))
        check("after last", seq[-1] == 5)
        # 快退
        idx, hint = line_index_at(times, 1.0, hint)
        check("seek back from end", idx == 0, str(idx))
        # 快进
        idx, hint = line_index_at(times, 12.0, hint)
        check("seek forward", idx == 5, str(idx))
        # 边界：早于第一句
        idx, hint = line_index_at(times, -1.0, -1)
        check("before first", idx == -1, str(idx))
        idx, hint = line_index_at([], 1.0, -1)
        check("empty", idx == -1)
        # 重复 hint 快路径
        idx, hint = line_index_at(times, 4.0, 3)
        check("hint exact dup", idx == 3)

    def test_timecode_parse():
        check("single", parse_timecodes("01:23") == 83)
        check("pair current/dur", parse_timecodes("1:23 / 4:56") == 83)
        check("fullwidth", parse_timecodes("１：２３／４：５６") == 83)
        check("if reversed take smaller", parse_timecodes("4:56 / 1:23") == 83)
        check("empty", parse_timecodes("") == -1)
        check("no match", parse_timecodes("hello") == -1)

    def test_interleaved_reads():
        # 交错 apply / 读位置 / 外部回锚，检查锁保护下的状态机（模型层）
        tl = MediaTimeline()
        now = t0
        results = []
        results.append(tl.apply(5, 100, now, now, True, 1))
        p1 = tl.position
        tl.advance(0.1)
        results.append(tl.apply(5, 100, now, now + timedelta(milliseconds=100), True, 1))
        p2 = tl.position
        tl.advance(0.2)
        ok = tl.try_external(40, now + timedelta(seconds=3), True, 0.5)
        p3 = tl.position
        check("kinds", results == ["Seeded", "Unchanged"], str(results))
        check("p1", almost(p1, 5))
        check("p2 grew", p2 > p1)
        check("ext after 3s", ok and almost(p3, 40), str((ok, p3)))

    run("lut and usable", test_lut_and_usable)
    run("estimate official formula", test_estimate)
    run("normal increment + frozen snapshot", test_normal_and_frozen)
    run("forward/back seek", test_forward_back_seek)
    run("seek while paused + resume", test_seek_while_paused_and_resume)
    run("same snapshot no jitter", test_same_snapshot_no_jitter)
    run("track/session reset", test_track_and_session_reset)
    run("unusable + invalid LUT", test_unusable_and_invalid_lut)
    run("seek to start", test_seek_to_start)
    run("stale snapshot ignored", test_stale_snapshot_ignored)
    run("external resync / recent evidence", test_external_resync_and_recent)
    run("missed event then poll", test_event_miss_then_poll)
    run("LRC index edges and duplicates", test_line_index)
    run("timecode parse", test_timecode_parse)
    run("interleaved apply/read/resync", test_interleaved_reads)

    if failed:
        print("\n%d TEST(S) FAILED" % failed)
        return 1
    print("\nALL MODEL TESTS PASSED")
    return 0


if __name__ == "__main__":
    sys.exit(main())

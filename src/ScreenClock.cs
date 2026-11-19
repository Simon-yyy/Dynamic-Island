using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DynamicIsland
{
    /// <summary>
    /// 直接从播放器窗口底部读出它自己显示的「mm:ss」时间码。
    /// 汽水音乐、部分版本网易云这类播放器完全不通过系统上报播放进度，
    /// 只能这样“看”它自己的进度显示，读到多少就立刻对齐到多少。
    /// </summary>
    internal static class ScreenClock
    {
        [DllImport("user32.dll")]
        private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hwnd);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        private static readonly string[] KnownProcs =
        {
            "SodaMusic", "cloudmusic", "CloudMusic", "QQMusic", "KuGou", "kugou",
            "Spotify", "NeteaseCloud", "netease", "KwMusic", "kwmusic", "NexPlayer", "QQMusicMini"
        };

        private static Windows.Media.Ocr.OcrEngine _ocr;
        private static bool _ocrTried;
        private static IntPtr _hwnd;
        private static Bitmap _buf;
        private static readonly object _lock = new object();
        private static volatile bool _running;
        private static double _lastOcrSec = -1;
        private static DateTimeOffset _lastOcrAt = DateTimeOffset.MinValue;

        private static void Log(string s)
        {
            string p = Environment.GetEnvironmentVariable("ISLAND_SCREEN_LOG");
            if (p == null) return;
            try { System.IO.File.AppendAllText(p, DateTime.Now.ToString("HH:mm:ss.fff") + " " + s + Environment.NewLine); } catch { }
        }

        public static void Start()
        {
            if (_running) return;
            _running = true;
            Task.Run(new Func<Task>(Loop));
        }

        private static async Task Loop()
        {
            bool skipping = false;
            while (true)
            {
                try
                {
                    await Task.Delay(180);

                    // 暂停、展开时都要读：用户经常在暂停时拖进度，展开面板也要立刻跟上。
                    if (MediaSession.Title.Length == 0 && !MediaSession.Playing)
                    {
                        if (!skipping) { Log("skip: no session"); skipping = true; }
                        continue;
                    }
                    if (!MediaSession.AllowsExternalResync)
                    {
                        if (!skipping) { Log("skip: recent SMTC evidence"); skipping = true; }
                        continue;
                    }
                    skipping = false;

                    double t = await ReadSeconds();
                    if (t < 0) continue;
                    Log("read t=" + t.ToString("F0") + " local=" + MediaSession.Position.ToString("F1"));
                    DateTimeOffset now = DateTimeOffset.UtcNow;
                    bool confirmed = _lastOcrSec >= 0
                        && Math.Abs(t - _lastOcrSec) <= 1.0
                        && (now - _lastOcrAt).TotalSeconds <= 1.6;
                    _lastOcrSec = t;
                    _lastOcrAt = now;
                    if (!confirmed)
                    {
                        Log("ocr pending confirm t=" + t.ToString("F0"));
                        continue;
                    }
                    MediaSession.TryResync(t);
                }
                catch (Exception ex) { Log("loop err: " + ex.Message); }
            }
        }

        /// <summary>
        /// 整张图几乎是纯色（对比度极低）说明 PrintWindow 没抓到内容，需要改用 CopyFromScreen。
        /// 语义与 LyricWatch.IsBlank 一致：抽点采样 RGB 和的最大最小差。
        /// </summary>
        private static bool IsMostlyBlank(Bitmap bmp)
        {
            if (bmp == null) return true;
            int min = 255 * 3, max = 0, samples = 0;
            int h = bmp.Height, w = bmp.Width;
            if (h <= 0 || w <= 0) return true;
            for (int y = 0; y < h; y += 8)
            {
                for (int x = 0; x < w; x += 8)
                {
                    var c = bmp.GetPixel(x, y);
                    int v = c.R + c.G + c.B;
                    if (v < min) min = v;
                    if (v > max) max = v;
                    samples++;
                }
            }
            return samples == 0 || (max - min) < 40;
        }

        private static async Task<double> ReadSeconds()
        {
            var hwnd = FindPlayerWindow();
            if (hwnd == IntPtr.Zero) { Log("no player window"); return -1; }

            Bitmap shot;
            int w, h;
            lock (_lock)
            {
                RECT r;
                if (!GetWindowRect(hwnd, out r)) return -1;
                w = r.Right - r.Left;
                h = r.Bottom - r.Top;
                if (w < 200 || h < 120) { Log("window too small " + w + "x" + h); return -1; }

                if (_buf == null || _buf.Width != w || _buf.Height != h)
                {
                    if (_buf != null) _buf.Dispose();
                    _buf = new Bitmap(w, h, PixelFormat.Format24bppRgb);
                }
                using (var g = Graphics.FromImage(_buf))
                {
                    IntPtr hdc = g.GetHdc();
                    try { PrintWindow(hwnd, hdc, 2); }
                    finally { g.ReleaseHdc(hdc); }
                }
                if (IsMostlyBlank(_buf))
                {
                    using (var g = Graphics.FromImage(_buf))
                    {
                        try { g.CopyFromScreen(r.Left, r.Top, 0, 0, new Size(w, h)); }
                        catch (Exception ex) { Log("CopyFromScreen err: " + ex.Message); }
                    }
                }
                shot = _buf;
            }

            int cw = Math.Min(w - 4, (int)(w * 0.55));
            int ch = Math.Min(h - 4, 110);
            int cy = h - ch - 4;
            if (cw < 80 || ch < 20) return -1;

            var crop = new Bitmap(cw, ch, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(crop))
                g.DrawImage(shot, new Rectangle(0, 0, cw, ch), new Rectangle(2, cy, cw, ch), GraphicsUnit.Pixel);

            string text;
            try { text = await Ocr(crop); }
            finally { crop.Dispose(); }

            if (string.IsNullOrEmpty(text)) { Log("ocr empty"); return -1; }
            Log("ocr=[" + text.Replace("\r", " ").Replace("\n", "|") + "]");

            // OCR 常把冒号认成全角「：」、数字有时也全角，先统一成半角再抓时间码。
            // 播放器底部常见「1:23 / 4:56」，取较短的那个作为当前进度，避免误把总时长当进度。
            string norm = text.Replace('：', ':').Replace('／', '/').Replace(' ', ' ');
            norm = Regex.Replace(norm, "[０-９]", mm => ((char)('0' + (mm.Value[0] - '０'))).ToString());
            var matches = Regex.Matches(norm, @"(\d{1,3})\s*:\s*([0-5]\d)");
            if (matches.Count == 0) return -1;
            double first = int.Parse(matches[0].Groups[1].Value) * 60.0 + int.Parse(matches[0].Groups[2].Value);
            if (matches.Count == 1) return first;
            double second = int.Parse(matches[1].Groups[1].Value) * 60.0 + int.Parse(matches[1].Groups[2].Value);
            if (first <= second) return first;
            return second;
        }

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumProc cb, IntPtr param);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

        private delegate bool EnumProc(IntPtr hwnd, IntPtr param);

        private static IntPtr _found;

        private static bool EnumCb(IntPtr hwnd, IntPtr param)
        {
            try
            {
                if (!IsWindowVisible(hwnd)) return true;
                RECT r;
                if (!GetWindowRect(hwnd, out r)) return true;
                int w = r.Right - r.Left, h = r.Bottom - r.Top;
                if (w < 300 || h < 200) return true;

                uint pid;
                GetWindowThreadProcessId(hwnd, out pid);
                if (pid == 0) return true;
                string name;
                try { name = Process.GetProcessById((int)pid).ProcessName; }
                catch { return true; }

                foreach (string known in KnownProcs)
                {
                    if (name.Equals(known, StringComparison.OrdinalIgnoreCase))
                    {
                        _found = hwnd;
                        return false;
                    }
                }
                return true;
            }
            catch { return true; }
        }
        private static IntPtr FindPlayerWindow()
        {
            if (_hwnd != IntPtr.Zero)
            {
                uint pid;
                GetWindowThreadProcessId(_hwnd, out pid);
                if (pid != 0 && IsWindowVisible(_hwnd)) return _hwnd;
                _hwnd = IntPtr.Zero;
            }

            _found = IntPtr.Zero;
            try { EnumWindows(EnumCb, IntPtr.Zero); }
            catch { }

            if (_found != IntPtr.Zero)
            {
                _hwnd = _found;
                Log("player window found");
            }
            return _hwnd;
        }

        private static async Task<string> Ocr(Bitmap bmp)
        {
            try
            {
                if (!_ocrTried)
                {
                    _ocrTried = true;
                    _ocr = Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages();
                    if (_ocr == null)
                        _ocr = Windows.Media.Ocr.OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("zh-Hans-CN"));
                    Log(_ocr == null ? "ocr engine unavailable" : "ocr engine ok");
                }
                if (_ocr == null) return null;

                int w = bmp.Width, h = bmp.Height;
                byte[] pixels = new byte[w * h * 4];
                var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    byte[] row = new byte[w * 4];
                    for (int y = 0; y < h; y++)
                    {
                        Marshal.Copy(new IntPtr(data.Scan0.ToInt64() + (long)y * data.Stride), row, 0, w * 4);
                        Buffer.BlockCopy(row, 0, pixels, y * w * 4, w * 4);
                    }
                }
                finally { bmp.UnlockBits(data); }

                var buffer = System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBuffer.Create(pixels, 0, pixels.Length, pixels.Length);
                var sb = Windows.Graphics.Imaging.SoftwareBitmap.CreateCopyFromBuffer(
                    buffer, Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, w, h,
                    Windows.Graphics.Imaging.BitmapAlphaMode.Ignore);

                var result = await _ocr.RecognizeAsync(sb);
                return result == null ? null : result.Text;
            }
            catch (Exception ex) { Log("ocr err: " + ex.GetType().Name + " " + ex.Message); return null; }
        }
    }
}

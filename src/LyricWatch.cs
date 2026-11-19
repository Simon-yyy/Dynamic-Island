using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace DynamicIsland
{
    /// <summary>
    /// 播放器不给我们进度时（网易云 / 汽水音乐的 SMTC 位置恒为 0），唯一还能拿到
    /// 「用户跳到哪一句了」的地方，就是播放器自己画在屏幕上的歌词。
    /// 这里在后台每 2 秒找一次网易云的「桌面歌词」窗口（窗口类名 DesktopLyrics），
    /// 把那一行字截图下来交给系统 OCR（本机自带简体中文识别），再和 LRC 里已有的句子比对：
    /// 认出来了、而且和我们现在显示的那句差得够远，就把时间锚回正确的句子。
    ///
    /// 全程只读屏幕像素，不注入、不读内存、不改播放器任何设置；桌面歌词没开的话这个循环
    /// 每个 2 秒只是枚举一次窗口，什么都不做。暂停时也会尝试（方便暂停拖进度后对齐）。
    /// </summary>
    internal static class LyricWatch
    {
        private static bool _started;
        private static OcrEngine _ocr;

        public static void Start()
        {
            if (_started) return;
            _started = true;
            Task.Run(new Action(Loop));
        }

        private static void Loop()
        {
            while (true)
            {
                try { Tick(); }
                catch { }
                Thread.Sleep(2000);
            }
        }

        private static void Tick()
        {
            // 诊断用：ISLAND_LW_FORCE=1 时跳过「在播」和「有真实进度」两道闸门，
            // 便于在暂停状态下单独验证找窗口 / 截图 / OCR 这条链路
            if (Environment.GetEnvironmentVariable("ISLAND_LW_FORCE") == null)
            {
                // 暂停时也允许回锚（用户可能在暂停时拖动进度）；
                // 刚收到可用的 SMTC 更新时不要用 OCR 拽走时间线。
                if (MediaSession.Title.Length == 0 && !MediaSession.Playing) return;
                if (!MediaSession.AllowsExternalResync) return;
            }

            IntPtr h = FindLyricWindow();
            if (h == IntPtr.Zero) return;

            IntPtr child = FindDrawnChild(h);
            if (child != IntPtr.Zero) h = child;

            RECT r;
            if (!GetWindowRect(h, out r)) return;
            int w = r.R - r.L, ht = r.B - r.T;
            if (w < 60 || ht < 20 || w > 4000 || ht > 600) return;

            using (var bmp = new Bitmap(w, ht))
            {
                // 先直接抓屏幕：桌面歌词是置顶窗口，屏幕上是什么就是什么，
                // 而且它自己不一定实现了 WM_PRINT，PrintWindow 很可能只给一张黑图。
                using (var g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(r.L, r.T, 0, 0, new Size(w, ht));
                }
                if (IsBlank(bmp))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.Black);
                        IntPtr hdc = g.GetHdc();
                        bool ok = PrintWindow(h, hdc, 2);
                        if (!ok) PrintWindow(h, hdc, 0);
                        g.ReleaseHdc(hdc);
                    }
                }
                string text = Ocr(bmp);
                string lwLog = Environment.GetEnvironmentVariable("ISLAND_LW_LOG");
                if (lwLog != null)
                {
                    try { File.AppendAllText(lwLog, DateTime.Now.ToString("HH:mm:ss") + " ocr=" + (text ?? "") + " w=" + w + "x" + ht + "\n"); } catch { }
                }
                if (!string.IsNullOrEmpty(text)) MediaSession.ResyncFromText(text);
            }
        }

        /// <summary>整张图几乎是纯色（黑/单色）说明没抓到内容，需要换抓法。</summary>
        private static bool IsBlank(Bitmap bmp)
        {
            int min = 255, max = 0;
            for (int y = 0; y < bmp.Height; y += 3)
            {
                for (int x = 0; x < bmp.Width; x += 3)
                {
                    var c = bmp.GetPixel(x, y);
                    int v = c.R + c.G + c.B;
                    if (v < min) min = v;
                    if (v > max) max = v;
                }
            }
            return (max - min) < 40;   // 对比度太低 = 没内容
        }

        private static OcrEngine Engine()
        {
            if (_ocr == null)
            {
                try
                {
                    _ocr = OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("zh-Hans-CN"));
                    if (_ocr == null) _ocr = OcrEngine.TryCreateFromUserProfileLanguages();
                }
                catch { }
            }
            return _ocr;
        }

        private static string Ocr(Bitmap bmp)
        {
            var eng = Engine();
            if (eng == null) return null;
            string tmp = Path.Combine(Path.GetTempPath(), "island_lw.png");
            try
            {
                bmp.Save(tmp, ImageFormat.Png);
                var file = Windows.Storage.StorageFile.GetFileFromPathAsync(tmp).AsTask().GetAwaiter().GetResult();
                var stream = file.OpenAsync(Windows.Storage.FileAccessMode.Read).AsTask().GetAwaiter().GetResult();
                var dec = BitmapDecoder.CreateAsync(stream).AsTask().GetAwaiter().GetResult();
                var sb = dec.GetSoftwareBitmapAsync().AsTask().GetAwaiter().GetResult();
                var res = eng.RecognizeAsync(sb).AsTask().GetAwaiter().GetResult();
                if (res == null) return null;
                var sbText = new StringBuilder();
                foreach (var line in res.Lines) sbText.Append(line.Text).Append(' ');
                return sbText.ToString();
            }
            catch (Exception e)
            {
                string lg = Environment.GetEnvironmentVariable("ISLAND_LW_LOG");
                if (lg != null) { try { File.AppendAllText(lg, "OCR异常: " + e.Message + "\n"); } catch { } }
                return null;
            }
        }

        private static IntPtr FindLyricWindow()
        {
            IntPtr found = IntPtr.Zero;
            EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                if (!IsWindowVisible(h)) return true;
                var cls = new StringBuilder(128);
                GetClassName(h, cls, 128);
                string c = cls.ToString();
                // 网易云：DesktopLyrics；其它播放器如果也用类似命名，一并接受
                if (c.IndexOf("Lyric", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    found = h;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        private static IntPtr FindDrawnChild(IntPtr parent)
        {
            IntPtr found = IntPtr.Zero;
            EnumChildWindows(parent, delegate(IntPtr h, IntPtr l)
            {
                if (!IsWindowVisible(h)) return true;
                RECT r;
                if (!GetWindowRect(h, out r)) return true;
                // 找面积最大的可见子窗口——歌词真正画在这上面
                if ((r.R - r.L) > 80 && (r.B - r.T) > 16)
                {
                    found = h;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int L, T, R, B; }

        private delegate bool EnumProc(IntPtr h, IntPtr l);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumProc cb, IntPtr l);
        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr l);
        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")]
        private static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    }
}

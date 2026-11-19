using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DynamicIsland
{
    /// <summary>
    /// 桌面宠物：一张抠好的图贴在桌面上，独立透明窗口，待机微动、可拖动、点一下有反应。
    /// 只接受「带真实透明通道的 PNG」——不带透明的图贴到桌面上会带一块白底方块，
    /// 那正是这个小组件最忌讳的东西，所以宁可拒绝也不硬套。
    /// </summary>
    internal static class Pet
    {
        // 四个内置预设，图放在 data\pet\presets\<id>.png
        public static readonly string[] Ids = { "cat", "shiba", "penguin", "dino" };
        public static readonly string[] Names = { "橘猫", "柴犬", "企鹅", "小恐龙" };

        private const double DefaultHeight = 170;   // 桌面上的默认高度：1080p 下大约一屏的 1/6，不挡东西
        private const double MinHeight = 70;
        private const double MaxHeight = 460;
        private static double _h = DefaultHeight;

        private static Window _win;
        private static Image _img;
        private static BitmapImage _bmp;
        private static ScaleTransform _scale;
        private static TranslateTransform _move;
        private static DateTime _last;
        private static double _t;
        private static double _poke = -1;

        private static Point _dragStart;
        private static bool _dragging;
        private static double _dragL, _dragT;

        private static string _id;          // 预设 id，或 "custom"
        private static bool _on;            // 这次开机是否显示

        public static string CurrentId { get { return _id; } }
        public static bool Visible { get { return _on && _win != null; } }

        public static string DataDir
        {
            get
            {
                try
                {
                    string dir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "pet");
                    System.IO.Directory.CreateDirectory(dir);
                    System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dir, "presets"));
                    return dir;
                }
                catch { return null; }
            }
        }

        public static string PresetPath(string id)
        {
            string dir = DataDir;
            if (dir == null) return null;
            if (id == "custom") return System.IO.Path.Combine(dir, "custom.png");
            return System.IO.Path.Combine(System.IO.Path.Combine(dir, "presets"), id + ".png");
        }

        private static string StatePath
        {
            get
            {
                string dir = DataDir;
                return dir == null ? null : System.IO.Path.Combine(dir, "pet.txt");
            }
        }

        // ------------------------------------------------------------------
        //  配置
        // ------------------------------------------------------------------
        public static void Load()
        {
            try
            {
                string f = StatePath;
                if (f == null || !System.IO.File.Exists(f)) return;
                foreach (string line in System.IO.File.ReadAllLines(f))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq).Trim();
                    string v = line.Substring(eq + 1).Trim();
                    if (k == "id") _id = v;
                    else if (k == "on") _on = v == "1";
                    else if (k == "x") _savedL = double.Parse(v);
                    else if (k == "y") _savedT = double.Parse(v);
                    else if (k == "h") _h = Math.Max(MinHeight, Math.Min(MaxHeight, double.Parse(v)));
                }
            }
            catch { }
            if (_on && !string.IsNullOrEmpty(_id)) Show(_id);
        }

        private static double _savedL = double.NaN, _savedT = double.NaN;

        public static void Save()
        {
            try
            {
                string f = StatePath;
                if (f == null) return;
                double l = _win != null ? _win.Left : _savedL;
                double t = _win != null ? _win.Top : _savedT;
                var sb = new System.Text.StringBuilder();
                sb.Append("id=").Append(_id ?? "").Append('\n');
                sb.Append("on=").Append(_on ? "1" : "0").Append('\n');
                if (!double.IsNaN(l)) sb.Append("x=").Append(Math.Round(l)).Append('\n');
                if (!double.IsNaN(t)) sb.Append("y=").Append(Math.Round(t)).Append('\n');
                sb.Append("h=").Append(Math.Round(_h)).Append('\n');
                System.IO.File.WriteAllText(f, sb.ToString());
            }
            catch { }
        }

        // ------------------------------------------------------------------
        //  显示 / 隐藏
        // ------------------------------------------------------------------
        public static void Show(string id)
        {
            string path = PresetPath(id);
            if (path == null || !System.IO.File.Exists(path)) return;
            _id = id;
            _on = true;
            Save();

            var bmp = Load(path);
            if (bmp == null) return;
            _bmp = bmp;
            EnsureWindow(bmp);
            _img.Source = bmp;
            Resize(bmp);
            _win.Show();
            _win.Topmost = true;
        }

        public static void Hide()
        {
            _on = false;
            Save();
            if (_win != null) _win.Hide();
        }

        public static void Toggle()
        {
            if (_on) Hide();
            else if (!string.IsNullOrEmpty(_id)) Show(_id);
            else Show(Ids[0]);
        }

        private static BitmapImage Load(string path)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(path);
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }

        private static void EnsureWindow(BitmapImage bmp)
        {
            if (_win != null) return;

            _win = new Window
            {
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                Topmost = true,
                ShowInTaskbar = false,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                Focusable = false,
                ShowActivated = false
            };
            _img = new Image { Stretch = Stretch.Fill, SnapsToDevicePixels = true };
            var host = new Grid();
            host.Children.Add(_img);
            _win.Content = host;

            _win.MouseLeftButtonDown += OnDown;
            _win.MouseMove += OnMove;
            _win.MouseLeftButtonUp += OnUp;
            _win.MouseRightButtonDown += delegate { Hide(); };
            _win.MouseWheel += OnWheel;

            CompositionTarget.Rendering += Tick;
        }

        private static void Resize(BitmapImage bmp)
        {
            if (_win == null || bmp == null) return;
            double w = (double)bmp.PixelWidth / bmp.PixelHeight * _h;
            _win.Width = w;
            _win.Height = _h;

            _scale = new ScaleTransform(1, 1, w / 2, _h);   // 以双脚为原点：挤压时脚不离地
            _move = new TranslateTransform(0, 0);
            var tg = new TransformGroup();
            tg.Children.Add(_scale);
            tg.Children.Add(_move);
            ((Grid)_win.Content).RenderTransform = tg;

            // 缩放时保持右下角不动：脚底和右边缘贴着原地，不会一跳一跳
            if (double.IsNaN(_savedL))
                _win.Left = SystemParameters.WorkArea.Right - w - 28;
            else
                _win.Left = Math.Min(_savedL, SystemParameters.WorkArea.Right - w);
            if (double.IsNaN(_savedT))
                _win.Top = SystemParameters.WorkArea.Bottom - _h - 6;
            else
                _win.Top = Math.Min(_savedT, SystemParameters.WorkArea.Bottom - _h);
        }

        private static void OnWheel(object s, MouseWheelEventArgs e)
        {
            double step = e.Delta > 0 ? 1.10 : 0.91;
            _h = Math.Max(MinHeight, Math.Min(MaxHeight, _h * step));
            Resize(_bmp);
            _savedL = _win.Left;
            _savedT = _win.Top;
            Save();
            e.Handled = true;
        }

        private static void OnDown(object s, MouseButtonEventArgs e)
        {
            _dragging = false;
            _dragStart = e.GetPosition(_win);
            _dragL = _win.Left;
            _dragT = _win.Top;
            _win.CaptureMouse();
        }

        private static void OnMove(object s, MouseEventArgs e)
        {
            if (!_win.IsMouseCaptured || e.LeftButton != MouseButtonState.Pressed) return;
            var p = e.GetPosition(_win);
            if (Math.Abs(p.X - _dragStart.X) > 3 || Math.Abs(p.Y - _dragStart.Y) > 3) _dragging = true;
            if (_dragging)
            {
                _win.Left = _dragL + (p.X - _dragStart.X);
                _win.Top = _dragT + (p.Y - _dragStart.Y);
            }
        }

        private static void OnUp(object s, MouseButtonEventArgs e)
        {
            if (_win.IsMouseCaptured) _win.ReleaseMouseCapture();
            if (_dragging) Save();
            else _poke = 0;
            _dragging = false;
        }

        private static void Tick(object s, EventArgs e)
        {
            if (_win == null || _scale == null) return;
            var now = DateTime.Now;
            if (_last == default(DateTime)) _last = now;
            double dt = (now - _last).TotalSeconds;
            if (dt > 0.05) dt = 0.05;
            _last = now;
            _t += dt;

            double breathe = 1 + 0.012 * Math.Sin(_t * 1.6);
            double sy = breathe, sx = 1.0 / Math.Sqrt(breathe), mv = 0.006 * _h * Math.Sin(_t * 1.6 + 0.6);

            if (_poke >= 0)
            {
                _poke += dt;
                if (_poke < 0.07) { sy = 1 - 0.10 * (_poke / 0.07); }
                else if (_poke < 0.24)
                {
                    double u = (_poke - 0.07) / 0.17;
                    sy = 0.90 + 0.16 * u;
                    mv -= 0.14 * Math.Sin(u * Math.PI) * _h;
                }
                else if (_poke < 0.40)
                {
                    double u = (_poke - 0.24) / 0.16;
                    sy = 1.06 - 0.06 * u;
                }
                else _poke = -1;
                sx = 1.0 / Math.Sqrt(Math.Max(0.5, sy));
            }

            _scale.ScaleY = sy;
            _scale.ScaleX = sx;
            _move.Y = mv;
        }

        // ------------------------------------------------------------------
        //  上传（A 方案：只收带透明通道的 PNG）
        // ------------------------------------------------------------------
        /// <summary>
        /// 检查图片是否真的有透明背景。要求：存在透明的像素（A 很低），同时主体是不透明的。
        /// 全不透明的图直接拒绝——贴桌面上会是一块带颜色的方块。
        /// </summary>
        public static bool HasTransparency(string path, out string why)
        {
            why = "";
            try
            {
                using (var bmp = new System.Drawing.Bitmap(path))
                {
                    int w = bmp.Width, h = bmp.Height;
                    var bd = bmp.LockBits(new System.Drawing.Rectangle(0, 0, w, h),
                        System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    int[] buf = new int[w * h];
                    System.Runtime.InteropServices.Marshal.Copy(bd.Scan0, buf, 0, buf.Length);
                    bmp.UnlockBits(bd);

                    int clear = 0, solid = 0, total = 0;
                    int step = Math.Max(1, (w * h) / 200000);
                    for (int i = 0; i < buf.Length; i += step)
                    {
                        int a = (buf[i] >> 24) & 0xFF;
                        total++;
                        if (a < 24) clear++;
                        else if (a > 200) solid++;
                    }
                    if (total == 0) { why = "读不出像素"; return false; }
                    double clearRatio = (double)clear / total;
                    if (clearRatio < 0.02) { why = "这张图没有透明背景"; return false; }
                    if ((double)solid / total < 0.05) { why = "这张图主体几乎是空的"; return false; }
                    return true;
                }
            }
            catch (Exception e) { why = "读图失败：" + e.Message; return false; }
        }

        /// <summary>把用户选的图拷进 data\pet\custom.png，然后立刻显示。</summary>
        public static bool UseCustom(string srcPath, out string why)
        {
            if (!HasTransparency(srcPath, out why)) return false;
            try
            {
                string dst = PresetPath("custom");
                if (dst == null) { why = "数据目录不可写"; return false; }
                System.IO.File.Copy(srcPath, dst, true);
                Show("custom");
                return true;
            }
            catch (Exception e) { why = "复制失败：" + e.Message; return false; }
        }
    }
}

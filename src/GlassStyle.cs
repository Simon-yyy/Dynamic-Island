using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace DynamicIsland
{
    internal static class GlassStyle
    {
        public static readonly Color Ink = Color.FromRgb(0xF2, 0xF6, 0xFF);
        public static readonly Color InkDim = Color.FromRgb(0x93, 0xA2, 0xBF);
        public static readonly Color Accent = Color.FromRgb(0x74, 0xA6, 0xFF);

        public static LinearGradientBrush BodyBrush()
        {
            return BodyBrush(
                Color.FromArgb(232, 0x1C, 0x23, 0x36),
                Color.FromArgb(242, 0x12, 0x17, 0x26),
                Color.FromArgb(248, 0x0B, 0x0E, 0x1A),
                Color.FromArgb(252, 0x06, 0x08, 0x10));
        }

        public static LinearGradientBrush BodyBrush(Color c0, Color c1, Color c2, Color c3)
        {
            var b = new LinearGradientBrush { StartPoint = new Point(0.08, 0), EndPoint = new Point(0.92, 1) };
            b.GradientStops.Add(new GradientStop(c0, 0.00));
            b.GradientStops.Add(new GradientStop(c1, 0.38));
            b.GradientStops.Add(new GradientStop(c2, 0.72));
            b.GradientStops.Add(new GradientStop(c3, 1.00));
            return b;
        }

        /// <summary>
        /// 液态玻璃的主光：顶部一道亮带 + 中段几乎透明 + 底部一点点反光，
        /// 叠在内容之上，让整块看起来像一块真的玻璃，而不是一块半透明色块。
        /// </summary>
        public static LinearGradientBrush LiquidBrush()
        {
            var b = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
            b.GradientStops.Add(new GradientStop(Color.FromArgb(64, 0xFF, 0xFF, 0xFF), 0.000));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(30, 0xFF, 0xFF, 0xFF), 0.075));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(10, 0xFF, 0xFF, 0xFF), 0.230));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xFF, 0xFF, 0xFF), 0.520));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(8, 0xFF, 0xFF, 0xFF), 0.760));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(30, 0xFF, 0xFF, 0xFF), 0.960));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(44, 0xFF, 0xFF, 0xFF), 1.000));
            return b;
        }

        /// <summary>液态玻璃的边：只在顶部和两侧留一道细高光，像玻璃被光擦到的边沿。</summary>
        public static LinearGradientBrush LiquidEdgeBrush()
        {
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            b.GradientStops.Add(new GradientStop(Color.FromArgb(120, 0xFF, 0xFF, 0xFF), 0.00));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(210, 0xFF, 0xFF, 0xFF), 0.34));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(170, 0xFF, 0xFF, 0xFF), 0.68));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(96, 0xFF, 0xFF, 0xFF), 1.00));
            return b;
        }

        public static LinearGradientBrush RimBrush()
        {
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            b.GradientStops.Add(new GradientStop(Color.FromArgb(150, 0xFF, 0xFF, 0xFF), 0.00));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(96, 0xD6, 0xE6, 0xFF), 0.28));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(20, 0xFF, 0xFF, 0xFF), 0.60));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(86, 0xB4, 0xCB, 0xF6), 0.88));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(140, 0xFF, 0xFF, 0xFF), 1.00));
            return b;
        }

        public static LinearGradientBrush DividerBrush()
        {
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.00));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(46, 255, 255, 255), 0.30));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(46, 255, 255, 255), 0.70));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1.00));
            return b;
        }

        public static TextBlock Label(string text, double size, Color color)
        {
            return new TextBlock
            {
                Text = text,
                FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
                FontSize = size,
                Foreground = new SolidColorBrush(color)
            };
        }

        /// <summary>Pill-shaped translucent tile used for the metric cards and buttons.</summary>
        public static Border Tile(double radius, double alpha = 30)
        {
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0.6, 1) };
            b.GradientStops.Add(new GradientStop(Color.FromArgb((byte)alpha, 255, 255, 255), 0));
            b.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(alpha * 0.35), 220, 232, 255), 1));
            var tile = new Border
            {
                CornerRadius = new CornerRadius(radius),
                Background = b,
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(26, 255, 255, 255))
            };
            return tile;
        }
    }

    /// <summary>Thin capsule progress bar drawn with two rounded borders.</summary>
    internal sealed class GlassBar : Grid
    {
        private readonly Border _fill;
        private readonly Border _track;
        private readonly Grid _fillHost;

        public GlassBar(Color color)
        {
            Height = 5;
            _track = new Border
            {
                CornerRadius = new CornerRadius(2.5),
                Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255))
            };
            Children.Add(_track);

            _fillHost = new Grid { HorizontalAlignment = HorizontalAlignment.Left, ClipToBounds = true };
            _fill = new Border
            {
                CornerRadius = new CornerRadius(2.5),
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = new LinearGradientBrush(
                    new GradientStopCollection
                    {
                        new GradientStop(color, 0),
                        new GradientStop(Color.FromArgb(255, 255, 255, 255), 1)
                    }, new Point(0, 0), new Point(1, 0))
            };
            _fill.Effect = new DropShadowEffect { Color = color, BlurRadius = 9, ShadowDepth = 0, Opacity = 0.55 };
            _fillHost.Children.Add(_fill);
            Children.Add(_fillHost);

            SizeChanged += delegate { ApplyRatio(_ratio); };
        }

        private double _ratio;
        public double Ratio
        {
            get { return _ratio; }
            set { _ratio = Math.Max(0, Math.Min(1, value)); ApplyRatio(_ratio); }
        }

        private void ApplyRatio(double ratio)
        {
            double w = Math.Max(0, ActualWidth * ratio);
            _fillHost.Width = w;
            _fill.Width = Math.Max(w, ratio > 0.001 ? 5 : 0);
        }
    }
}
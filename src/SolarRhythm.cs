using System;
using System.Windows;
using System.Windows.Media;

namespace DynamicIsland
{
    /// <summary>
    /// 自然日太阳轨迹与天色状态快照（按北京时间 UTC+8 实时流转）。
    /// </summary>
    public struct SolarFrame
    {
        public Color Bg0;
        public Color Bg1;
        public Color Bg2;
        public Color Bg3;

        public Point PosA;
        public Point PosB;
        public Point PosC;

        public Color ColA;
        public Color ColB;
        public Color ColC;

        public string PhaseName;
        public DateTime BeijingTime;
    }

    /// <summary>
    /// 日光律动（Solar Rhythm）核心算法 — 北京时间（UTC+8）全天候明显色温流转版：
    /// 1. 严格以北京时间（UTC+8）为时间基准；
    /// 2. 彻底解除旧版“L<=18% 压黑”限制，让白昼（上午/正午/午后）呈现真正通透明亮的晴空蔚蓝与暖阳金晕，
    ///    清晨呈现破晓橙粉与朝阳金，傍晚呈现熔金晚霞与玫瑰紫，夜晚回归深邃星空蓝；
    /// 3. 结合正弦太阳弧线轨迹与 SmoothStep 余弦平滑插值，随时间每分钟连续变化。
    /// </summary>
    public static class SolarRhythm
    {
        private struct SolarAnchor
        {
            public double Hour;
            public Color Bg0, Bg1, Bg2, Bg3;
            public Point PosA, PosB, PosC;
            public Color ColA, ColB, ColC;
            public string Name;
        }

        private static readonly SolarAnchor[] Anchors = new SolarAnchor[]
        {
            // 00:00 静谧子夜（深空夜蓝）
            new SolarAnchor
            {
                Hour = 0.0,
                Name = "静谧子夜",
                Bg0 = Color.FromArgb(244, 0x0E, 0x13, 0x26),
                Bg1 = Color.FromArgb(248, 0x0A, 0x0E, 0x1D),
                Bg2 = Color.FromArgb(252, 0x07, 0x0A, 0x14),
                Bg3 = Color.FromArgb(254, 0x04, 0x06, 0x0C),
                PosA = new Point(55, 60),
                PosB = new Point(160, 20),
                PosC = new Point(-35, 150),
                ColA = Color.FromArgb(65, 0x32, 0x46, 0x8E),
                ColB = Color.FromArgb(55, 0x24, 0x3A, 0x70),
                ColC = Color.FromArgb(50, 0x42, 0x2A, 0x78)
            },
            // 05:00 拂晓破晓（东方微露晨曦橙粉与晨雾蓝紫）
            new SolarAnchor
            {
                Hour = 5.0,
                Name = "拂晓破晓",
                Bg0 = Color.FromArgb(244, 0x22, 0x26, 0x4A),
                Bg1 = Color.FromArgb(248, 0x26, 0x24, 0x46),
                Bg2 = Color.FromArgb(252, 0x19, 0x1B, 0x35),
                Bg3 = Color.FromArgb(254, 0x0F, 0x11, 0x24),
                PosA = new Point(185, -25),
                PosB = new Point(40, 55),
                PosC = new Point(-45, 145),
                ColA = Color.FromArgb(155, 0xFF, 0x8C, 0x6E), // 破晓橙粉
                ColB = Color.FromArgb(125, 0x58, 0xB4, 0xDC), // 晨雾青蓝
                ColC = Color.FromArgb(110, 0x82, 0x64, 0xCC)  // 残夜暮紫
            },
            // 06:30 朝阳初升（日出东方：暖阳金橙与清透晨天蓝交织）
            new SolarAnchor
            {
                Hour = 6.5,
                Name = "朝阳初升",
                Bg0 = Color.FromArgb(244, 0x2E, 0x48, 0x74),
                Bg1 = Color.FromArgb(248, 0x33, 0x3D, 0x62),
                Bg2 = Color.FromArgb(252, 0x24, 0x2A, 0x48),
                Bg3 = Color.FromArgb(254, 0x15, 0x19, 0x2E),
                PosA = new Point(165, -35),
                PosB = new Point(-25, 20),
                PosC = new Point(85, 145),
                ColA = Color.FromArgb(190, 0xFF, 0xB8, 0x4D), // 朝阳暖金
                ColB = Color.FromArgb(150, 0x5C, 0xB8, 0xFF), // 晨天晴蓝
                ColC = Color.FromArgb(135, 0xFF, 0x7A, 0x66)  // 朝霞珊瑚红
            },
            // 08:30 朗润上午（清朗天青蓝 + 明亮晨光）
            new SolarAnchor
            {
                Hour = 8.5,
                Name = "朗润上午",
                Bg0 = Color.FromArgb(244, 0x25, 0x5E, 0x9A),
                Bg1 = Color.FromArgb(248, 0x1E, 0x4D, 0x82),
                Bg2 = Color.FromArgb(252, 0x16, 0x3A, 0x66),
                Bg3 = Color.FromArgb(254, 0x0F, 0x28, 0x4A),
                PosA = new Point(115, -48),
                PosB = new Point(-35, 15),
                PosC = new Point(65, 145),
                ColA = Color.FromArgb(180, 0x64, 0xD2, 0xFF), // 清朗晴空青蓝
                ColB = Color.FromArgb(155, 0xFF, 0xE2, 0x84), // 上午暖白日光
                ColC = Color.FromArgb(130, 0x48, 0xE5, 0xC6)  // 通透薄荷青
            },
            // 12:00 晴空正午（11:00~14:00 明亮蔚蓝晴空 + 天顶正午暖阳）
            new SolarAnchor
            {
                Hour = 12.0,
                Name = "晴空正午",
                Bg0 = Color.FromArgb(244, 0x2C, 0x6E, 0xB4),
                Bg1 = Color.FromArgb(248, 0x22, 0x58, 0x94),
                Bg2 = Color.FromArgb(252, 0x19, 0x42, 0x72),
                Bg3 = Color.FromArgb(254, 0x11, 0x2D, 0x52),
                PosA = new Point(40, -55),
                PosB = new Point(140, -25),
                PosC = new Point(-35, 135),
                ColA = Color.FromArgb(195, 0x72, 0xDC, 0xFF), // 正午澄净天光蓝
                ColB = Color.FromArgb(165, 0xFF, 0xEA, 0x98), // 正午天顶暖阳金白
                ColC = Color.FromArgb(140, 0x48, 0xBE, 0xFF)  // 晴空蔚蓝底光
            },
            // 15:00 午后暖晴（蔚蓝天色中融入温暖午后香槟金）
            new SolarAnchor
            {
                Hour = 15.0,
                Name = "午后暖晴",
                Bg0 = Color.FromArgb(244, 0x2B, 0x5E, 0x9C),
                Bg1 = Color.FromArgb(248, 0x27, 0x4C, 0x80),
                Bg2 = Color.FromArgb(252, 0x1E, 0x39, 0x62),
                Bg3 = Color.FromArgb(254, 0x13, 0x25, 0x44),
                PosA = new Point(-25, -45),
                PosB = new Point(145, 35),
                PosC = new Point(-35, 145),
                ColA = Color.FromArgb(185, 0xFF, 0xCE, 0x68), // 午后暖阳金
                ColB = Color.FromArgb(160, 0x64, 0xBA, 0xFF), // 午后晴空蓝
                ColC = Color.FromArgb(125, 0x56, 0xD6, 0xC8)  // 暖青光晕
            },
            // 17:45 日落熔金（夕阳西沉：浓郁晚霞熔金橙、玫瑰红与暮紫）
            new SolarAnchor
            {
                Hour = 17.75,
                Name = "日落熔金",
                Bg0 = Color.FromArgb(244, 0x52, 0x2B, 0x40),
                Bg1 = Color.FromArgb(248, 0x3E, 0x21, 0x38),
                Bg2 = Color.FromArgb(252, 0x2A, 0x16, 0x2B),
                Bg3 = Color.FromArgb(254, 0x17, 0x0C, 0x1C),
                PosA = new Point(-85, -25),
                PosB = new Point(135, 65),
                PosC = new Point(20, 150),
                ColA = Color.FromArgb(200, 0xFF, 0x7A, 0x38), // 落日熔金橙
                ColB = Color.FromArgb(165, 0xFA, 0x54, 0x86), // 晚霞玫瑰粉红
                ColC = Color.FromArgb(135, 0xA2, 0x4A, 0xEA)  // 暮云紫
            },
            // 19:15 蓝调薄暮（经典 Blue Hour：深邃皇家宝石蓝与暮夜幽紫）
            new SolarAnchor
            {
                Hour = 19.25,
                Name = "蓝调薄暮",
                Bg0 = Color.FromArgb(244, 0x24, 0x23, 0x5C),
                Bg1 = Color.FromArgb(248, 0x1B, 0x1A, 0x48),
                Bg2 = Color.FromArgb(252, 0x12, 0x11, 0x34),
                Bg3 = Color.FromArgb(254, 0x0A, 0x0A, 0x20),
                PosA = new Point(-65, 10),
                PosB = new Point(135, 25),
                PosC = new Point(-45, 150),
                ColA = Color.FromArgb(170, 0x4E, 0x74, 0xFF), // 皇家宝石蓝
                ColB = Color.FromArgb(155, 0x98, 0x48, 0xF2), // 暮夜幽紫
                ColC = Color.FromArgb(110, 0xF2, 0x66, 0x4A)  // 西天残照余晖
            },
            // 21:15 入夜星沉（夜幕渐深，星蓝静谧）
            new SolarAnchor
            {
                Hour = 21.25,
                Name = "入夜星沉",
                Bg0 = Color.FromArgb(244, 0x16, 0x1C, 0x38),
                Bg1 = Color.FromArgb(248, 0x10, 0x15, 0x2A),
                Bg2 = Color.FromArgb(252, 0x0B, 0x0E, 0x1E),
                Bg3 = Color.FromArgb(254, 0x06, 0x08, 0x12),
                PosA = new Point(15, 45),
                PosB = new Point(155, 30),
                PosC = new Point(-35, 150),
                ColA = Color.FromArgb(100, 0x3C, 0x54, 0xBA),
                ColB = Color.FromArgb(85, 0x5A, 0x3A, 0xAC),
                ColC = Color.FromArgb(65, 0x28, 0x42, 0x82)
            },
            // 24:00 子夜闭环
            new SolarAnchor
            {
                Hour = 24.0,
                Name = "静谧子夜",
                Bg0 = Color.FromArgb(244, 0x0E, 0x13, 0x26),
                Bg1 = Color.FromArgb(248, 0x0A, 0x0E, 0x1D),
                Bg2 = Color.FromArgb(252, 0x07, 0x0A, 0x14),
                Bg3 = Color.FromArgb(254, 0x04, 0x06, 0x0C),
                PosA = new Point(55, 60),
                PosB = new Point(160, 20),
                PosC = new Point(-35, 150),
                ColA = Color.FromArgb(65, 0x32, 0x46, 0x8E),
                ColB = Color.FromArgb(55, 0x24, 0x3A, 0x70),
                ColC = Color.FromArgb(50, 0x42, 0x2A, 0x78)
            }
        };

        /// <summary>将任意 DateTime 转换为北京时间（UTC+8）。</summary>
        public static DateTime ToBeijingTime(DateTime time)
        {
            DateTime utc = (time.Kind == DateTimeKind.Utc) ? time : time.ToUniversalTime();
            return utc.AddHours(8.0);
        }

        /// <summary>
        /// 根据北京时间（UTC+8）计算当前分钟对应的连续日光律动天色快照。
        /// </summary>
        public static SolarFrame Compute(DateTime now)
        {
            DateTime bj = ToBeijingTime(now);
            double curHour = bj.TimeOfDay.TotalHours;
            if (curHour < 0) curHour = 0;
            if (curHour >= 24) curHour = 23.9999;

            int idx = 0;
            for (int i = 0; i < Anchors.Length - 1; i++)
            {
                if (curHour >= Anchors[i].Hour && curHour <= Anchors[i + 1].Hour)
                {
                    idx = i;
                    break;
                }
            }

            SolarAnchor a = Anchors[idx];
            SolarAnchor b = Anchors[idx + 1];

            double span = b.Hour - a.Hour;
            double u = (span > 0.001) ? ((curHour - a.Hour) / span) : 0.0;
            u = Math.Max(0.0, Math.Min(1.0, u));

            // SmoothStep 余弦缓动曲线，保证每分钟天色连续渐进、无整点突变
            double t = u * u * (3.0 - 2.0 * u);

            var f = new SolarFrame();
            f.BeijingTime = bj;
            f.Bg0 = Lerp(a.Bg0, b.Bg0, t);
            f.Bg1 = Lerp(a.Bg1, b.Bg1, t);
            f.Bg2 = Lerp(a.Bg2, b.Bg2, t);
            f.Bg3 = Lerp(a.Bg3, b.Bg3, t);

            // 白昼时段（05:30 ~ 18:30）主太阳光斑沿正弦弧线自右（东）向左（西）在面板顶部可见天幕内平滑移动
            if (curHour >= 5.5 && curHour <= 18.5)
            {
                double dayProgress = (curHour - 5.5) / 13.0; // 0 = 日出东侧, 0.5 = 12:00 正午天顶, 1 = 日落西侧
                double arcX = 175.0 - dayProgress * 265.0;   // +175 -> +42.5 -> -90
                double arcY = -20.0 - Math.Sin(dayProgress * Math.PI) * 38.0; // -20 -> -58 -> -20 (保持在顶部可见天幕内，不被裁出窗外)
                f.PosA = new Point(arcX, arcY);
            }
            else
            {
                f.PosA = Lerp(a.PosA, b.PosA, t);
            }

            f.PosB = Lerp(a.PosB, b.PosB, t);
            f.PosC = Lerp(a.PosC, b.PosC, t);

            f.ColA = Lerp(a.ColA, b.ColA, t);
            f.ColB = Lerp(a.ColB, b.ColB, t);
            f.ColC = Lerp(a.ColC, b.ColC, t);

            f.PhaseName = ResolvePhaseName(curHour);
            return f;
        }

        private static string ResolvePhaseName(double h)
        {
            if (h < 5.0) return "静谧子夜";
            if (h < 6.25) return "拂晓破晓";
            if (h < 8.25) return "朝阳初升";
            if (h < 10.75) return "朗润上午";
            if (h < 13.75) return "晴空正午";
            if (h < 16.75) return "午后暖晴";
            if (h < 18.6) return "日落熔金";
            if (h < 20.5) return "蓝调薄暮";
            if (h < 22.75) return "入夜星沉";
            return "静谧子夜";
        }

        private static Color Lerp(Color c1, Color c2, double t)
        {
            byte a = (byte)(c1.A + (c2.A - c1.A) * t);
            byte r = (byte)(c1.R + (c2.R - c1.R) * t);
            byte g = (byte)(c1.G + (c2.G - c1.G) * t);
            byte b = (byte)(c1.B + (c2.B - c1.B) * t);
            return Color.FromArgb(a, r, g, b);
        }

        private static Point Lerp(Point p1, Point p2, double t)
        {
            return new Point(p1.X + (p2.X - p1.X) * t, p1.Y + (p2.Y - p1.Y) * t);
        }
    }
}

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace DynamicIsland
{
    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct FILETIME { public uint Low; public uint High; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint Length;
            public uint MemoryLoad;
            public ulong TotalPhys;
            public ulong AvailPhys;
            public ulong TotalPageFile;
            public ulong AvailPageFile;
            public ulong TotalVirtual;
            public ulong AvailVirtual;
            public ulong AvailExtendedVirtual;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_POWER_STATUS
        {
            public byte AC;
            public byte BatteryFlag;
            public byte BatteryPercent;
            public byte Reserved;
            public uint BatteryLifeTime;
            public uint BatteryFullLifeTime;
        }

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT point);

        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int key);

        [DllImport("user32.dll")]
        public static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern int GetWindowLong32(IntPtr hwnd, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern int SetWindowLong32(IntPtr hwnd, int index, int value);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern int GetWindowRgnBox(IntPtr hWnd, out RECT lprc);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetLayeredWindowAttributes(IntPtr hwnd, out uint crKey, out byte bAlpha, out uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX status);

        [DllImport("kernel32.dll")]
        private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);

        [DllImport("shcore.dll")]
        private static extern int SetProcessDpiAwareness(int value);

        private static ulong _lastIdle, _lastKernel, _lastUser;
        private static bool _hasCpuSample;
        private static readonly uint _selfPid = (uint)Process.GetCurrentProcess().Id;

        public static void EnableDpiAwareness()
        {
            try
            {
                if (!SetProcessDpiAwarenessContext(new IntPtr(-4)))
                    SetProcessDpiAwareness(2);
            }
            catch
            {
                try { SetProcessDpiAwareness(2); } catch { }
            }
        }

        public static void MakeToolWindow(IntPtr hwnd)
        {
            try
            {
                const int GWL_EXSTYLE = -20;
                const int WS_EX_TOOLWINDOW = 0x00000080;
                const int WS_EX_NOACTIVATE = 0x08000000;
                int style = GetWindowLong32(hwnd, GWL_EXSTYLE);
                SetWindowLong32(hwnd, GWL_EXSTYLE, style | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
            }
            catch { }
        }

        /// <summary>
        /// 实时检测屏幕顶部状态栏（如荣耀 Magic 顶部栏、MyDockFinder、顶部自动隐藏任务栏 Shell_TrayWnd 等）当前的可见下边缘物理像素 Y 坐标。
        /// - 当顶部状态栏显示时，返回其下边缘物理像素坐标（例如 32~64px），供灵动岛紧贴在状态栏最下边；
        /// - 当顶部状态栏自动隐藏（滑出屏幕顶边使 Bottom <= 8、隐藏窗口、透明淡出或置为穿透感应条）时，返回 0，供灵动岛自动回贴屏幕顶边。
        /// </summary>
        public static int GetTopBarBottomPhysicalPx(IntPtr selfHwnd, int capLeftPx, int capRightPx, int screenWidthPx)
        {
            int maxBottom = 0;
            if (screenWidthPx <= 0) screenWidthPx = 1920;

            try
            {
                var clsBuf = new StringBuilder(128);
                EnumWindows(delegate (IntPtr hwnd, IntPtr lParam)
                {
                    if (hwnd == IntPtr.Zero || hwnd == selfHwnd) return true;
                    if (!IsWindowVisible(hwnd) || IsIconic(hwnd)) return true;

                    uint pid;
                    GetWindowThreadProcessId(hwnd, out pid);
                    if (pid == _selfPid) return true;

                    RECT r;
                    if (!GetWindowRect(hwnd, out r)) return true;

                    int w = r.Right - r.Left;
                    int h = r.Bottom - r.Top;
                    // 顶部状态栏特征：顶边贴近屏幕顶部（-80 ~ +12px），整体高度在 10 ~ 135px 之间（排除普通大窗口或全屏窗口）
                    if (r.Top < -80 || r.Top > 12) return true;
                    if (h < 10 || h > 135) return true;

                    int effBottom = r.Bottom;
                    RECT rgnBox;
                    int rgnType = GetWindowRgnBox(hwnd, out rgnBox);
                    if (rgnType == 1 /* NULLREGION */) return true;
                    if (rgnType == 2 || rgnType == 3)
                    {
                        int rgnBottom = r.Top + rgnBox.Bottom;
                        if (rgnBottom < effBottom) effBottom = rgnBottom;
                    }

                    // 自动隐藏状态栏收起后通常只留 <= 8px 的顶部唤醒细条，此时视为已隐藏
                    if (effBottom <= 8 || effBottom > 125) return true;

                    // 必须与胶囊所在水平范围有交集，或者是横跨屏幕顶部的通栏
                    bool overlapsCapsuleX = (r.Right > capLeftPx + 6) && (r.Left < capRightPx - 6);
                    bool isWideTopBar = w >= (int)(screenWidthPx * 0.30) && r.Left <= (int)(screenWidthPx * 0.35);
                    if (!overlapsCapsuleX && !isWideTopBar) return true;

                    const int GWL_EXSTYLE = -20;
                    const int WS_EX_TOPMOST = 0x00000008;
                    const int WS_EX_TRANSPARENT = 0x00000020;
                    const int WS_EX_LAYERED = 0x00080000;
                    int exStyle = GetWindowLong32(hwnd, GWL_EXSTYLE);

                    clsBuf.Length = 0;
                    GetClassName(hwnd, clsBuf, clsBuf.Capacity);
                    string cls = clsBuf.ToString();
                    if (cls == "Progman" || cls == "WorkerW" || cls == "XamlExplorerHostIslandWindow")
                        return true;

                    bool isSystemTrayBar = (cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd");

                    // 非系统任务栏若既不是 Topmost 也不是宽通栏，则忽略
                    if (!isSystemTrayBar && (exStyle & WS_EX_TOPMOST) == 0 && !isWideTopBar)
                        return true;

                    // 如果该窗口当前设置了鼠标穿透（许多状态栏隐藏态会置 WS_EX_TRANSPARENT），视为已隐藏
                    if (!isSystemTrayBar && (exStyle & WS_EX_TRANSPARENT) != 0)
                        return true;

                    // 如果该窗口通过 Layered Alpha 淡出隐藏（Alpha <= 18），视为已隐藏
                    if ((exStyle & WS_EX_LAYERED) != 0)
                    {
                        uint crKey, dwFlags;
                        byte bAlpha;
                        if (GetLayeredWindowAttributes(hwnd, out crKey, out bAlpha, out dwFlags))
                        {
                            if ((dwFlags & 0x2 /* LWA_ALPHA */) != 0 && bAlpha <= 18)
                                return true;
                        }
                    }

                    // 排除 DWM Cloaked（隐藏的 UWP / 虚拟桌面后台窗口）
                    int cloaked = 0;
                    if (DwmGetWindowAttribute(hwnd, 14 /* DWMWA_CLOAKED */, out cloaked, 4) == 0 && cloaked != 0)
                        return true;

                    if (effBottom > maxBottom)
                        maxBottom = effBottom;

                    return true;
                }, IntPtr.Zero);
            }
            catch { }

            return maxBottom;
        }

        /// <summary>整窗点击穿透：WS_EX_TRANSPARENT 是跨进程生效的（HTTRANSPARENT 只在同线程有效）。</summary>
        public static void SetClickThrough(IntPtr hwnd, bool on)
        {
            try
            {
                const int GWL_EXSTYLE = -20;
                const int WS_EX_TRANSPARENT = 0x00000020;
                int style = GetWindowLong32(hwnd, GWL_EXSTYLE);
                int want = on ? (style | WS_EX_TRANSPARENT) : (style & ~WS_EX_TRANSPARENT);
                if (want != style) SetWindowLong32(hwnd, GWL_EXSTYLE, want);
            }
            catch { }
        }

        public static int MemoryLoadPercent()
        {
            var status = new MEMORYSTATUSEX();
            status.Length = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            if (GlobalMemoryStatusEx(ref status))
                return (int)status.MemoryLoad;
            return 0;
        }

        public static void MemoryInfo(out double usedGb, out double totalGb)
        {
            var status = new MEMORYSTATUSEX();
            status.Length = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            if (GlobalMemoryStatusEx(ref status))
            {
                totalGb = status.TotalPhys / (1024.0 * 1024 * 1024);
                usedGb = (status.TotalPhys - status.AvailPhys) / (1024.0 * 1024 * 1024);
                return;
            }
            usedGb = totalGb = 0;
        }

        public static int BatteryPercent(out bool onAc)
        {
            bool known;
            int percent = BatteryPercentEx(out onAc, out known);
            return known ? percent : -1;
        }

        public static int BatteryPercentEx(out bool onAc, out bool known)
        {
            onAc = false;
            known = false;
            SYSTEM_POWER_STATUS status;
            if (GetSystemPowerStatus(out status))
            {
                onAc = status.AC == 1;
                if ((status.BatteryFlag & 0x80) == 0 && status.BatteryPercent <= 100)
                {
                    known = true;
                    return status.BatteryPercent;
                }
            }
            return 0;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public int AccentState;
            public int AccentFlags;
            public int GradientColor;
            public int AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CompositionData
        {
            public int Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        [DllImport("user32.dll")]
        private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref CompositionData data);

        public static bool SetAcrylic(IntPtr hwnd, byte a, byte r, byte g, byte b, int state)
        {
            if (hwnd == IntPtr.Zero) return false;
            var policy = new AccentPolicy
            {
                AccentState = state,
                AccentFlags = 2,
                GradientColor = (a << 24) | (b << 16) | (g << 8) | r,
                AnimationId = 0
            };
            int size = Marshal.SizeOf(policy);
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(policy, ptr, false);
                var data = new CompositionData { Attribute = 19, Data = ptr, SizeOfData = size };
                return SetWindowCompositionAttribute(hwnd, ref data) != 0;
            }
            catch { return false; }
            finally { Marshal.FreeHGlobal(ptr); }
        }

        public static void ClearAcrylic(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;
            var policy = new AccentPolicy { AccentState = 0 };
            int size = Marshal.SizeOf(policy);
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(policy, ptr, false);
                var data = new CompositionData { Attribute = 19, Data = ptr, SizeOfData = size };
                SetWindowCompositionAttribute(hwnd, ref data);
            }
            catch { }
            finally { Marshal.FreeHGlobal(ptr); }
        }

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateRoundRectRgn(int l, int t, int r, int b, int w, int h);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("user32.dll")]
        private static extern int SetWindowRgn(IntPtr hwnd, IntPtr hrgn, bool redraw);

        public static void SetBodyRegion(IntPtr hwnd, double x, double y, double w, double h, double radius, double dpi)
        {
            if (hwnd == IntPtr.Zero) return;
            int pad = (int)Math.Round(2 * dpi);
            int px = (int)Math.Round(x * dpi) - pad;
            int py = (int)Math.Round(y * dpi) - pad;
            int pw = (int)Math.Round(w * dpi) + pad * 2;
            int ph = (int)Math.Round(h * dpi) + pad * 2;
            if (pw < 1) pw = 1;
            if (ph < 1) ph = 1;
            var hrgn = CreateRoundRectRgn(px, py, px + pw + 1, py + ph + 1,
                (int)Math.Round((radius + 2) * 2 * dpi), (int)Math.Round((radius + 2) * 2 * dpi));
            if (hrgn != IntPtr.Zero && SetWindowRgn(hwnd, hrgn, true) == 0)
                DeleteObject(hrgn);
        }

        public static void ClearRegion(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;
            SetWindowRgn(hwnd, IntPtr.Zero, true);
        }

        public static bool HasSystemBattery()
        {
            SYSTEM_POWER_STATUS status;
            if (!GetSystemPowerStatus(out status)) return true;
            if ((status.BatteryFlag & 0x80) != 0) return false;
            if ((status.BatteryFlag & 0xFF) == 255) return false;
            if (status.BatteryPercent == 255 && status.BatteryLifeTime == 0xFFFFFFFF) return false;
            return true;
        }

        public static double SystemCpuPercent()
        {
            FILETIME idle, kernel, user;
            if (!GetSystemTimes(out idle, out kernel, out user)) return 0;
            ulong i = ToU64(idle), k = ToU64(kernel), u = ToU64(user);
            if (!_hasCpuSample)
            {
                _lastIdle = i; _lastKernel = k; _lastUser = u;
                _hasCpuSample = true;
                return 0;
            }
            ulong idleDelta = i - _lastIdle;
            ulong totalDelta = (k - _lastKernel) + (u - _lastUser);
            _lastIdle = i; _lastKernel = k; _lastUser = u;
            if (totalDelta == 0) return 0;
            double busy = 1.0 - (double)idleDelta / totalDelta;
            if (busy < 0) busy = 0;
            if (busy > 1) busy = 1;
            return busy * 100.0;
        }

        private static ulong ToU64(FILETIME value)
        {
            return ((ulong)value.High << 32) | value.Low;
        }
    }
}

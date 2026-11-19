using System;
using System.Drawing;
using System.Windows.Forms;

namespace DynamicIsland
{
    /// <summary>
    /// 托盘图标：程序常驻在 Windows 通知区域的「隐藏的图标」里（Win11 新图标默认收进 ^ 面板），
    /// 双击显示/隐藏灵动岛，右键可以退出。窗口本身不进任务栏。
    /// </summary>
    public sealed class TrayIcon : IDisposable
    {
        private NotifyIcon _icon;
        private readonly IslandWindow _owner;

        public TrayIcon(IslandWindow owner)
        {
            _owner = owner;

            Icon icon = null;
            try { icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }
            if (icon == null) icon = SystemIcons.Application;

            var menu = new ContextMenuStrip();
            var toggle = new ToolStripMenuItem("显示 / 隐藏灵动岛");
            toggle.Click += delegate { _owner.ToggleFromTray(); };
            var resetPos = new ToolStripMenuItem("重置胶囊位置 (居中贴顶)");
            resetPos.Click += delegate { _owner.ResetPosition(); };
            var pet = new ToolStripMenuItem("桌面宠物 显示 / 隐藏");
            pet.Click += delegate { Pet.Toggle(); };
            var solar = new ToolStripMenuItem("日光律动天色 (随自然日流转)")
            {
                CheckOnClick = true,
                Checked = _owner.SolarRhythmEnabled
            };
            solar.Click += delegate { _owner.ToggleSolarRhythm(solar.Checked); };
            var ver = new ToolStripMenuItem(BuildInfo.Display);
            ver.Enabled = false;
            var quit = new ToolStripMenuItem("退出");
            quit.Click += delegate { _owner.Close(); };
            menu.Items.Add(toggle);
            menu.Items.Add(resetPos);
            menu.Items.Add(pet);
            menu.Items.Add(solar);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(ver);
            menu.Items.Add(quit);
            menu.Opening += delegate
            {
                solar.Checked = _owner.SolarRhythmEnabled;
                try
                {
                    var sf = SolarRhythm.Compute(DateTime.Now);
                    solar.Text = "日光律动天色 · " + sf.PhaseName + " (北京时间 " + sf.BeijingTime.ToString("HH:mm") + ")";
                }
                catch { }
            };

            string trayTip = BuildInfo.Display ?? "灵动岛";
            if (trayTip.Length > 63) trayTip = trayTip.Substring(0, 63);
            _icon = new NotifyIcon
            {
                Icon = icon,
                Text = trayTip,
                Visible = true,
                ContextMenuStrip = menu
            };
            _icon.DoubleClick += delegate { _owner.ToggleFromTray(); };
        }

        public void Dispose()
        {
            if (_icon == null) return;
            _icon.Visible = false;
            _icon.Dispose();
            _icon = null;
        }
    }
}

using System;
using System.IO;
using System.Windows;

namespace DynamicIsland
{
    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            Native.EnableDpiAwareness();

            string shot = null;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], "--shot", StringComparison.OrdinalIgnoreCase))
                {
                    shot = args[i + 1];
                }
            }

            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            var island = new IslandWindow(shot);
            app.MainWindow = island;
            island.Show();
            Pet.Load();          // 上次选过的宠物，这次开机还站在原来的位置
            app.Run();
        }
    }
}
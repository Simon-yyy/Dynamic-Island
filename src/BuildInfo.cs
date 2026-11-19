namespace DynamicIsland
{
    internal static class BuildInfo
    {
        public const string Version = "2026.11.17.batt-multi13";
        public const string Stamp = "2026-11-19T15:19:22+08:00";
        public const string Feature = "pnp-cfgmgr32+winsock-rfcomm-ch1+multi-device+freebuds-pro5";
        public static string Display
        {
            get { return "灵动岛 " + Version + " (" + Stamp + ")"; }
        }
    }
}

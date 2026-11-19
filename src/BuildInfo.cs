namespace DynamicIsland
{
    internal static class BuildInfo
    {
        public const string Version = "2026.11.17.batt-multi13";
        public const string Stamp = "2026-11-18T15:07:05+08:00";
        public const string Feature = "pnp-cfgmgr32+winsock-rfcomm-ch1+multi-device+freebuds-pro5";
        public static string Display
        {
            get { return "灵动岛 " + Version + " (" + Stamp + ")"; }
        }
    }
}

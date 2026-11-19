using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;

namespace DynamicIsland
{
    /// <summary>一次电量查询的结果分类。界面必须按此展示，不得把失败显示成新鲜读数。</summary>
    public enum BatteryQueryStatus
    {
        None = 0,
        Querying = 1,
        Fresh = 2,
        Stale = 3,
        NoSystemValue = 4,
        Unmatched = 5,
        Timeout = 6,
        AccessDenied = 7,
        Failed = 8
    }

    /// <summary>一台已配对的蓝牙设备（经典和/或 BLE）。</summary>
    public class BtDevice
    {
        public string Name = "";
        public ulong Address;
        public bool Connected;
        public uint ClassOfDevice;
        public bool IsBle;
        public bool IsClassic;
        public string ContainerId = "";
        public string AepId = "";
        public string CategoryHint = "";

        /// <summary>电量百分比（整机/平均）；-1 表示尚未取得有效读数。</summary>
        public int Battery = -1;
        public int BatteryLeft = -1;
        public int BatteryRight = -1;
        public int BatteryCase = -1;
        public DateTime BatterySampledAt = DateTime.MinValue;
        public BatteryQueryStatus QueryStatus = BatteryQueryStatus.None;
        /// <summary>查询来源简记，例如 pnp-devnode / aep-ble / aep-classic / gatt-180f / spp-5a-winsock / spp-5a-winrt / hfp-passive。</summary>
        public string QuerySource = "";

        public bool HasBattery { get { return Battery >= 0 && Battery <= 100; } }
        public bool HasSplit { get { return BatteryLeft >= 0 && BatteryRight >= 0; } }
        public bool IsAudio
        {
            get
            {
                return Bluetooth.IsAudio(ClassOfDevice) ||
                       Bluetooth.CategoryLooksAudio(CategoryHint) ||
                       Bluetooth.NameLooksAudio(Name);
            }
        }
        public bool IsPeripheral
        {
            get
            {
                return Bluetooth.IsPeripheral(ClassOfDevice) ||
                       Bluetooth.CategoryLooksInput(CategoryHint) ||
                       Bluetooth.NameLooksInput(Name);
            }
        }
        public bool IsKeyboard
        {
            get
            {
                return Bluetooth.IsKeyboard(ClassOfDevice) ||
                       Bluetooth.CategoryLooksKeyboard(CategoryHint) ||
                       Bluetooth.NameLooksKeyboard(Name);
            }
        }
        public bool IsMouse
        {
            get
            {
                return Bluetooth.IsMouse(ClassOfDevice) ||
                       Bluetooth.CategoryLooksMouse(CategoryHint) ||
                       Bluetooth.NameLooksMouse(Name);
            }
        }

        /// <summary>已连接且应出现在电量中心：支持多副耳机、音箱、键盘、鼠标、手柄及所有非主机类已连接蓝牙/BLE外设。</summary>
        public bool IsListable
        {
            get
            {
                if (!Connected) return false;
                if (Bluetooth.IsHostComputer(ClassOfDevice)) return false;
                return true;
            }
        }

        public string AddressText
        {
            get { return Bluetooth.FormatAddress(Address); }
        }
    }

    /// <summary>
    /// 蓝牙设备枚举 + 非破坏式多通道电量查询：
    /// 1. 经典蓝牙走 Win32 bthprops.cpl；
    /// 2. 全量蓝牙与 BLE 设备（含多副耳机、BTHHFENUM 免提节点、BLE 键鼠、手柄）及系统已缓存电量走 Win32 SetupAPI + CfgMgr32 父节点回溯
    ///    （读取 DEVPKEY_Device_BatteryLife {104EA319-6EE2-4701-BD47-8DDBF425BBE5} pid=2，并通过 CM_Get_Parent 关联子设备与蓝牙 MAC）；
    /// 3. 补充查询已配对 WinRT DeviceInterface / AEP（严格限制 IsPaired=True 且 IssueInquiry=False，避免 FindAllAsync 挂起超时）；
    /// 4. 针对华为 FreeBuds Pro 5（HarmonyOS 7.0.0.368）及 FreeBuds 全系耳机：
    ///    优先通过 Win32 Winsock2 AF_BTH RFCOMM 直连通道 1（port=1，以及候选通道 16/2/3 与 SPP UUID）发送华为 MBB 0x5A（01 08）查询帧，
    ///    读取左右耳与充电盒分体电量；并修复 WinRT IAsyncOperationWithProgress 反射等待，绝不调用 BluetoothSetServiceState 拆断音频。
    /// </summary>
    public static class Bluetooth
    {
        public const string ProtocolIdClassic = "{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}";
        public const string ProtocolIdBle = "{bb7bb05e-5972-42b5-94fc-76eaa7084d49}";

        // 官方属性：System.Devices.BatteryLife（Byte，101=未知）
        public const string PropBatteryLife = "System.Devices.BatteryLife";
        public const string PropAepAddress = "System.Devices.Aep.DeviceAddress";
        public const string PropAepConnected = "System.Devices.Aep.IsConnected";
        public const string PropAepPaired = "System.Devices.Aep.IsPaired";
        public const string PropAepPresent = "System.Devices.Aep.IsPresent";
        public const string PropAepContainerId = "System.Devices.Aep.ContainerId";
        public const string PropAepCategory = "System.Devices.Aep.Category";
        public const string PropAepProtocolId = "System.Devices.Aep.ProtocolId";
        public const string PropAepId = "System.Devices.Aep.AepId";
        public const string PropContainerIdSystem = "System.Devices.ContainerId";
        public const string PropDeviceConnected = "System.Devices.Connected";
        public const string PropBtDeviceAddress = "System.DeviceInterface.Bluetooth.DeviceAddress";
        public const string PropContainerCategories = "System.Devices.AepContainer.Categories";
        public const string PropContainerChildren = "System.Devices.AepContainer.Children";
        public const string PropContainerId = "System.Devices.AepContainer.ContainerId";
        public const string PropContainerPresent = "System.Devices.AepContainer.IsPresent";
        public const string PropContainerProtocols = "System.Devices.AepContainer.ProtocolIds";

        public static string QueryLogPath;

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEMTIME
        {
            public ushort wYear, wMonth, wDayOfWeek, wDay, wHour, wMinute, wSecond, wMilliseconds;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct BLUETOOTH_DEVICE_INFO
        {
            public int dwSize;
            public ulong Address;
            public uint ulClassofDevice;
            public bool fConnected;
            public bool fRemembered;
            public bool fAuthenticated;
            public SYSTEMTIME stLastSeen;
            public SYSTEMTIME stLastUsed;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)] public string szName;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BLUETOOTH_FIND_RADIO_PARAMS { public int dwSize; }

        [StructLayout(LayoutKind.Sequential)]
        private struct BLUETOOTH_DEVICE_SEARCH_PARAMS
        {
            public int dwSize;
            [MarshalAs(UnmanagedType.Bool)] public bool fReturnAuthenticated;
            [MarshalAs(UnmanagedType.Bool)] public bool fReturnRemembered;
            [MarshalAs(UnmanagedType.Bool)] public bool fReturnUnknown;
            [MarshalAs(UnmanagedType.Bool)] public bool fReturnConnected;
            [MarshalAs(UnmanagedType.Bool)] public bool fIssueInquiry;
            public byte cTimeoutMultiplier;
            public IntPtr hRadio;
        }

        [DllImport("bthprops.cpl", SetLastError = true)]
        private static extern IntPtr BluetoothFindFirstRadio(ref BLUETOOTH_FIND_RADIO_PARAMS p, out IntPtr phRadio);

        [DllImport("bthprops.cpl", SetLastError = true)]
        private static extern bool BluetoothFindNextRadio(IntPtr hFind, out IntPtr phRadio);

        [DllImport("bthprops.cpl")]
        private static extern bool BluetoothFindRadioClose(IntPtr hFind);

        [DllImport("bthprops.cpl", SetLastError = true)]
        private static extern IntPtr BluetoothFindFirstDevice(ref BLUETOOTH_DEVICE_SEARCH_PARAMS p, ref BLUETOOTH_DEVICE_INFO info);

        [DllImport("bthprops.cpl", SetLastError = true)]
        private static extern bool BluetoothFindNextDevice(IntPtr hFind, ref BLUETOOTH_DEVICE_INFO info);

        [DllImport("bthprops.cpl")]
        private static extern bool BluetoothFindDeviceClose(IntPtr hFind);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        // ---- Win32 SetupAPI + CfgMgr32 (PnP DevNode 树枚举、父节点回溯与属性直读) ----
        private const uint DIGCF_PRESENT = 0x00000002;
        private const uint DIGCF_ALLCLASSES = 0x00000004;

        private const uint DEVPROP_TYPE_SBYTE = 0x00000002;
        private const uint DEVPROP_TYPE_BYTE = 0x00000003;
        private const uint DEVPROP_TYPE_INT16 = 0x00000004;
        private const uint DEVPROP_TYPE_UINT16 = 0x00000005;
        private const uint DEVPROP_TYPE_INT32 = 0x00000006;
        private const uint DEVPROP_TYPE_UINT32 = 0x00000007;
        private const uint DEVPROP_TYPE_INT64 = 0x00000008;
        private const uint DEVPROP_TYPE_UINT64 = 0x00000009;
        private const uint DEVPROP_TYPE_GUID = 0x0000000D;
        private const uint DEVPROP_TYPE_BOOLEAN = 0x00000011;
        private const uint DEVPROP_TYPE_STRING = 0x00000012;
        private const uint DEVPROP_TYPE_STRING_LIST = 0x00002012;

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVINFO_DATA
        {
            public uint cbSize;
            public Guid ClassGuid;
            public uint DevInst;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DEVPROPKEY
        {
            public Guid fmtid;
            public uint pid;
            public DEVPROPKEY(string guidText, uint propId)
            {
                fmtid = new Guid(guidText);
                pid = propId;
            }
        }

        // DEVPKEY_Device_BatteryLife: {104EA319-6EE2-4701-BD47-8DDBF425BBE5}, 2 (System.Devices.BatteryLife)
        private static DEVPROPKEY PKEY_Device_BatteryLife = new DEVPROPKEY("104ea319-6ee2-4701-bd47-8ddbf425bbe5", 2);
        // Primary bluetooth battery fallback properties exposed by some HFP / BLE drivers
        private static DEVPROPKEY PKEY_Device_BatteryLifeAlt1 = new DEVPROPKEY("104ea319-6ee2-4701-bd47-8ddbf425bbe5", 3);
        // DEVPKEY_Device_ContainerId: {8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C}, 2
        private static DEVPROPKEY PKEY_Device_ContainerId = new DEVPROPKEY("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c", 2);
        // DEVPKEY_Device_FriendlyName: {A45C254E-DF1C-4EFD-8020-67D146A850E0}, 14
        private static DEVPROPKEY PKEY_Device_FriendlyName = new DEVPROPKEY("a45c254e-df1c-4efd-8020-67D146a850e0", 14);
        // DEVPKEY_Device_DeviceDesc: {A45C254E-DF1C-4EFD-8020-67D146A850E0}, 2
        private static DEVPROPKEY PKEY_Device_DeviceDesc = new DEVPROPKEY("a45c254e-df1c-4efd-8020-67D146a850e0", 2);
        // DEVPKEY_Device_Class: {A45C254E-DF1C-4EFD-8020-67D146A850E0}, 9
        private static DEVPROPKEY PKEY_Device_Class = new DEVPROPKEY("a45c254e-df1c-4efd-8020-67D146a850e0", 9);
        // DEVPKEY_Device_Service: {A45C254E-DF1C-4EFD-8020-67D146A850E0}, 6
        private static DEVPROPKEY PKEY_Device_Service = new DEVPROPKEY("a45c254e-df1c-4efd-8020-67D146a850e0", 6);
        // DEVPKEY_Device_IsConnected: {83DA6326-97A6-4088-9453-A1923F573B29}, 15
        private static DEVPROPKEY PKEY_Device_IsConnected = new DEVPROPKEY("83da6326-97a6-4088-9453-a1923f573b29", 15);
        // PKEY_Devices_Aep_IsConnected: {A35996AB-11CF-4935-8B61-A6761081ECDF}, 7
        private static DEVPROPKEY PKEY_Aep_IsConnected = new DEVPROPKEY("a35996ab-11cf-4935-8b61-a6761081ecdf", 7);
        // PKEY_DeviceInterface_Bluetooth_DeviceAddress: {2BD67D8B-8BEB-48D5-87E0-6CDA3428040A}, 1
        private static DEVPROPKEY PKEY_Bt_DeviceAddress = new DEVPROPKEY("2bd67d8b-8beb-48d5-87e0-6cda3428040a", 1);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevsW(
            IntPtr ClassGuid,
            string Enumerator,
            IntPtr hwndParent,
            uint Flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetupDiEnumDeviceInfo(
            IntPtr DeviceInfoSet,
            uint MemberIndex,
            ref SP_DEVINFO_DATA DeviceInfoData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetupDiGetDeviceInstanceIdW(
            IntPtr DeviceInfoSet,
            ref SP_DEVINFO_DATA DeviceInfoData,
            StringBuilder DeviceInstanceId,
            uint DeviceInstanceIdSize,
            out uint RequiredSize);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetupDiGetDevicePropertyW(
            IntPtr DeviceInfoSet,
            ref SP_DEVINFO_DATA DeviceInfoData,
            ref DEVPROPKEY PropertyKey,
            out uint PropertyType,
            byte[] PropertyBuffer,
            uint PropertyBufferSize,
            out uint RequiredSize,
            uint Flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

        // CfgMgr32: 用于从 BTHHFENUM / HID / BTHLEDEVICE 等子节点向上回溯父节点，获取所属蓝牙设备的 MAC 地址
        [DllImport("cfgmgr32.dll")]
        private static extern uint CM_Get_Parent(out uint pdnDevInst, uint dnDevInst, uint ulFlags);

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        private static extern uint CM_Get_Device_IDW(uint dnDevInst, StringBuilder Buffer, int BufferLen, uint ulFlags);

        // ---- Win32 Winsock2 AF_BTH RFCOMM 原生蓝牙套接字（直连 FreeBuds Pro 5 通道 1） ----
        private const int AF_BTH = 32;
        private const int SOCK_STREAM = 1;
        private const int BTHPROTO_RFCOMM = 3;
        private const uint FIONBIO = 0x8004667E;

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct SOCKADDR_BTH
        {
            public ushort addressFamily;
            public ulong btAddr;
            public Guid serviceClassId;
            public uint port;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct WSADATA
        {
            public ushort wVersion;
            public ushort wHighVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)]
            public string szDescription;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 129)]
            public string szSystemStatus;
            public ushort iMaxSockets;
            public ushort iMaxUdpDg;
            public IntPtr lpVendorInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TIMEVAL
        {
            public int tv_sec;
            public int tv_usec;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FD_SET_1
        {
            public uint fd_count;
            public IntPtr fd_0;
        }

        [DllImport("ws2_32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern int WSAStartup(ushort wVersionRequested, out WSADATA lpWSAData);

        [DllImport("ws2_32.dll", SetLastError = true)]
        private static extern IntPtr socket(int af, int type, int protocol);

        [DllImport("ws2_32.dll", SetLastError = true)]
        private static extern int connect(IntPtr s, ref SOCKADDR_BTH name, int namelen);

        [DllImport("ws2_32.dll", SetLastError = true)]
        private static extern int send(IntPtr s, byte[] buf, int len, int flags);

        [DllImport("ws2_32.dll", SetLastError = true)]
        private static extern int recv(IntPtr s, byte[] buf, int len, int flags);

        [DllImport("ws2_32.dll", SetLastError = true)]
        private static extern int closesocket(IntPtr s);

        [DllImport("ws2_32.dll", SetLastError = true)]
        private static extern int ioctlsocket(IntPtr s, uint cmd, ref uint argp);

        [DllImport("ws2_32.dll", SetLastError = true)]
        private static extern int select(int nfds, ref FD_SET_1 readfds, IntPtr writefds, IntPtr exceptfds, ref TIMEVAL timeout);

        [DllImport("ws2_32.dll", SetLastError = true, EntryPoint = "select")]
        private static extern int selectWrite(int nfds, IntPtr readfds, ref FD_SET_1 writefds, ref FD_SET_1 exceptfds, ref TIMEVAL timeout);

        private static bool _wsaInitialized;
        private static readonly object _wsaLock = new object();
        private static readonly Dictionary<ulong, uint> _rfcommWorkingPort = new Dictionary<ulong, uint>();
        private static readonly Dictionary<ulong, DateTime> _rfcommMissAt = new Dictionary<ulong, DateTime>();

        public static void ClearProbeCache(ulong address)
        {
            lock (_wsaLock)
            {
                if (address == 0)
                {
                    _rfcommWorkingPort.Clear();
                    _rfcommMissAt.Clear();
                }
                else
                {
                    _rfcommWorkingPort.Remove(address);
                    _rfcommMissAt.Remove(address);
                }
            }
        }

        private static void EnsureWsaStarted()
        {
            if (_wsaInitialized) return;
            lock (_wsaLock)
            {
                if (_wsaInitialized) return;
                try
                {
                    WSADATA wsa;
                    if (WSAStartup(0x0202, out wsa) == 0)
                        _wsaInitialized = true;
                }
                catch { }
            }
        }

/// <summary>枚举已配对的经典 + PnP DevNode + BLE 设备并合并同一地址。支持多副耳机及 BLE 键鼠外设。</summary>
        public static List<BtDevice> Enumerate()
        {
            var map = new Dictionary<ulong, BtDevice>();
            try
            {
                MergeDevices(map, EnumerateClassic());
            }
            catch (Exception ex)
            {
                LogQuery("enum-classic", "", BatteryQueryStatus.Failed, "classic_enum " + BriefError(ex));
            }
            try
            {
                MergeDevices(map, EnumeratePnpDevices());
            }
            catch (Exception ex)
            {
                LogQuery("enum-pnp", "", BatteryQueryStatus.Failed, "pnp_enum " + BriefError(ex));
            }
            // 注意：不再在周期枚举中调用 WinRT EnumerateBlePaired / FromBluetoothAddressAsync，
            // 因为 WinRT BLE 接口会触发底层 2.4GHz 无线电寻呼，导致蓝牙耳机 A2DP 音频卡顿或断音。
            // 所有 BLE 与经典蓝牙设备连接状态和内核电量均已由上方 EnumeratePnpDevices (SetupAPI 纯内存读取) 完整覆盖。

            var list = new List<BtDevice>();
            foreach (var kv in map) list.Add(kv.Value);
            return list;
        }

        private static void MergeDevices(Dictionary<ulong, BtDevice> map, List<BtDevice> incoming)
        {
            if (incoming == null) return;
            for (int i = 0; i < incoming.Count; i++)
            {
                BtDevice d = incoming[i];
                if (d == null || d.Address == 0) continue;

                // 如果地址不同但 ContainerId 完全一致，说明是同一台双模设备（Classic + BLE 共享容器）
                BtDevice existing = null;
                if (!map.TryGetValue(d.Address, out existing) && !string.IsNullOrEmpty(d.ContainerId))
                {
                    string cidNorm = NormGuid(d.ContainerId);
                    if (cidNorm.Length > 0)
                    {
                        foreach (var kv in map)
                        {
                            if (kv.Value != null && !string.IsNullOrEmpty(kv.Value.ContainerId) &&
                                string.Equals(NormGuid(kv.Value.ContainerId), cidNorm, StringComparison.OrdinalIgnoreCase))
                            {
                                // 注意：如果两台设备都是独立音频耳机（各有不同经典 MAC），不能合并；
                                // 只有其中一个是 BLE 辅助节点而另一个是 Classic 节点时才按同容器合并。
                                if (kv.Value.IsClassic != d.IsClassic || kv.Value.Address == d.Address)
                                {
                                    existing = kv.Value;
                                    break;
                                }
                            }
                        }
                    }
                }

                if (existing == null)
                {
                    map[d.Address] = d;
                    continue;
                }

                if (IsBetterDeviceName(d.Name, existing.Name))
                    existing.Name = d.Name;
                existing.Connected = existing.Connected || d.Connected;
                existing.IsBle = existing.IsBle || d.IsBle;
                existing.IsClassic = existing.IsClassic || d.IsClassic;
                if (existing.ClassOfDevice == 0) existing.ClassOfDevice = d.ClassOfDevice;
                if (string.IsNullOrEmpty(existing.ContainerId)) existing.ContainerId = d.ContainerId;
                if (string.IsNullOrEmpty(existing.AepId)) existing.AepId = d.AepId;
                AppendCategoryHint(existing, d.CategoryHint);

                if (d.HasBattery && (!existing.HasBattery || d.HasSplit))
                {
                    existing.Battery = d.Battery;
                    existing.QueryStatus = d.QueryStatus;
                    existing.QuerySource = d.QuerySource;
                    existing.BatterySampledAt = d.BatterySampledAt;
                    if (d.HasSplit)
                    {
                        existing.BatteryLeft = d.BatteryLeft;
                        existing.BatteryRight = d.BatteryRight;
                        existing.BatteryCase = d.BatteryCase;
                    }
                }
            }
        }

        private static void AppendCategoryHint(BtDevice target, string hint)
        {
            if (target == null || string.IsNullOrEmpty(hint)) return;
            if (string.IsNullOrEmpty(target.CategoryHint))
            {
                target.CategoryHint = hint;
                return;
            }
            string[] parts = hint.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                if (p.Length == 0) continue;
                if (target.CategoryHint.IndexOf(p, StringComparison.OrdinalIgnoreCase) < 0)
                    target.CategoryHint = target.CategoryHint + ";" + p;
            }
        }

        private static bool IsGenericNodeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return true;
            string n = name.Trim();
            if (n.Length == 0) return true;
            if (HintHas(n, "Generic Attribute") || HintHas(n, "Device Information") ||
                HintHas(n, "Battery Service") || HintHas(n, "Bluetooth LE Generic") ||
                HintHas(n, "Avrcp Transport") || HintHas(n, "Service Discovery") ||
                HintHas(n, "Hands-Free AG") || HintHas(n, "符合 HID 标准的") ||
                HintHas(n, "符合蓝牙低能耗") || HintHas(n, "Bluetooth Low Energy GATT") ||
                HintHas(n, "GATT compliant") || HintHas(n, "GATT 的 HID") ||
                HintHas(n, "HID-compliant") || HintHas(n, "HID Keyboard Device") ||
                HintHas(n, "HID Mouse") || HintHas(n, "HID 设备") ||
                HintHas(n, "HID Device") || HintHas(n, "蓝牙外围设备") ||
                HintHas(n, "Bluetooth Peripheral Device") || HintHas(n, "标准串行") ||
                HintHas(n, "Generic Access") || HintHas(n, "标准蓝牙") ||
                HintHas(n, "Microsoft Bluetooth") || HintHas(n, "Audio Endpoint") ||
                HintHas(n, "音频端点"))
                return true;
            return false;
        }

        public static string CleanDeviceName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            string s = raw.Trim();
            string[] suffixes = new string[]
            {
                " Hands-Free AG Audio",
                " Hands-Free AG",
                " Hands-Free",
                " Avrcp Transport",
                " Stereo",
                " 立体声",
                " 免提"
            };
            for (int i = 0; i < suffixes.Length; i++)
            {
                if (s.EndsWith(suffixes[i], StringComparison.OrdinalIgnoreCase) && s.Length > suffixes[i].Length)
                {
                    s = s.Substring(0, s.Length - suffixes[i].Length).Trim();
                }
            }
            return s;
        }

        private static bool IsBetterDeviceName(string candidate, string current)
        {
            string c = CleanDeviceName(candidate);
            if (string.IsNullOrEmpty(c)) return false;
            string cur = CleanDeviceName(current);
            if (string.IsNullOrEmpty(cur)) return true;
            bool cGeneric = IsGenericNodeName(candidate);
            bool curGeneric = IsGenericNodeName(current);
            if (curGeneric && !cGeneric) return true;
            if (!curGeneric && cGeneric) return false;
            return c.Length > cur.Length;
        }

        /// <summary>
        /// 经典蓝牙枚举。
        /// </summary>
        private static List<BtDevice> EnumerateClassic()
        {
            var list = new List<BtDevice>();
            var rp = new BLUETOOTH_FIND_RADIO_PARAMS();
            rp.dwSize = Marshal.SizeOf(typeof(BLUETOOTH_FIND_RADIO_PARAMS));
            IntPtr hRadio = IntPtr.Zero;
            IntPtr hFindRadio = BluetoothFindFirstRadio(ref rp, out hRadio);
            if (hFindRadio == IntPtr.Zero) return list;

            try
            {
                bool moreRadio = true;
                while (moreRadio)
                {
                    if (hRadio != IntPtr.Zero)
                    {
                        try
                        {
                            var sp = new BLUETOOTH_DEVICE_SEARCH_PARAMS();
                            sp.dwSize = Marshal.SizeOf(typeof(BLUETOOTH_DEVICE_SEARCH_PARAMS));
                            sp.fReturnAuthenticated = true;
                            sp.fReturnRemembered = true;
                            sp.fReturnConnected = true;
                            sp.fReturnUnknown = true;
                            sp.fIssueInquiry = false;
                            sp.cTimeoutMultiplier = 1;
                            sp.hRadio = hRadio;

                            var info = new BLUETOOTH_DEVICE_INFO();
                            info.dwSize = Marshal.SizeOf(typeof(BLUETOOTH_DEVICE_INFO));
                            IntPtr hFind = BluetoothFindFirstDevice(ref sp, ref info);
                            if (hFind != IntPtr.Zero)
                            {
                                try
                                {
                                    bool more = true;
                                    while (more)
                                    {
                                        var d = new BtDevice();
                                        d.Name = CleanDeviceName(info.szName ?? "");
                                        d.Address = info.Address;
                                        d.Connected = info.fConnected;
                                        d.ClassOfDevice = info.ulClassofDevice;
                                        d.IsClassic = true;
                                        list.Add(d);
                                        more = BluetoothFindNextDevice(hFind, ref info);
                                    }
                                }
                                finally
                                {
                                    BluetoothFindDeviceClose(hFind);
                                }
                            }
                        }
                        finally
                        {
                            CloseHandle(hRadio);
                            hRadio = IntPtr.Zero;
                        }
                    }
                    moreRadio = BluetoothFindNextRadio(hFindRadio, out hRadio);
                }
            }
            finally
            {
                if (hRadio != IntPtr.Zero)
                {
                    CloseHandle(hRadio);
                    hRadio = IntPtr.Zero;
                }
                BluetoothFindRadioClose(hFindRadio);
            }
            return list;
        }

        private class PnpNodeEntry
        {
            public string InstanceId = "";
            public string ContainerId = "";
            public ulong Address;
            public string Name = "";
            public string DeviceDesc = "";
            public string ClassName = "";
            public string ServiceName = "";
            public bool IsRootBtNode;
            public bool IsBtFamily;
            public bool IsBleNode;
            public bool IsClassicNode;
            public bool HasConnectedProp;
            public bool IsConnected;
            public int Battery = -1;
            public bool HasBattery;
        }

        /// <summary>
        /// 沿着 CfgMgr32 PnP 设备树向上回溯父节点（最多 6 层），提取所属 BTHENUM\DEV_xxx 或 BTHLE\DEV_xxx 的 12 位蓝牙 MAC 地址。
        /// 这样 BTHHFENUM\BthHFPAudio（耳机免提电量节点）、HID\...（蓝牙键鼠节点）、BTHLEDEVICE\...（BLE GATT 电池服务节点）
        /// 都能直接命中其父蓝牙设备的 MAC 地址。
        /// </summary>
        private static ulong ResolveParentBluetoothAddress(uint devInst, out bool parentIsBle, out bool parentIsClassic)
        {
            parentIsBle = false;
            parentIsClassic = false;
            uint cur = devInst;
            var sb = new StringBuilder(512);
            for (int depth = 0; depth < 6; depth++)
            {
                uint parent;
                if (CM_Get_Parent(out parent, cur, 0) != 0 || parent == 0)
                    break;
                sb.Length = 0;
                if (CM_Get_Device_IDW(parent, sb, sb.Capacity, 0) == 0 && sb.Length > 0)
                {
                    string pid = sb.ToString();
                    string up = pid.ToUpperInvariant();
                    if (up.StartsWith("BTHLE\\") || up.StartsWith("BTHLEDEVICE\\"))
                        parentIsBle = true;
                    if (up.StartsWith("BTHENUM\\") || up.StartsWith("BTHHFENUM\\"))
                        parentIsClassic = true;
                    ulong addr = ExtractBluetoothAddressFromInstanceId(pid);
                    if (addr != 0)
                        return addr;
                }
                cur = parent;
            }
            return 0;
        }

        private static List<PnpNodeEntry> ReadPresentPnpNodes()
        {
            var nodes = new List<PnpNodeEntry>();
            IntPtr hDevInfo = SetupDiGetClassDevsW(IntPtr.Zero, null, IntPtr.Zero, DIGCF_ALLCLASSES | DIGCF_PRESENT);
            if (hDevInfo == IntPtr.Zero || hDevInfo == new IntPtr(-1)) return nodes;

            try
            {
                var devData = new SP_DEVINFO_DATA();
                devData.cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVINFO_DATA));
                var idBuf = new StringBuilder(512);
                byte[] propBuf = new byte[512];

                uint index = 0;
                while (SetupDiEnumDeviceInfo(hDevInfo, index, ref devData))
                {
                    index++;
                    idBuf.Length = 0;
                    uint reqSize;
                    if (!SetupDiGetDeviceInstanceIdW(hDevInfo, ref devData, idBuf, (uint)idBuf.Capacity, out reqSize))
                        continue;

                    string instId = idBuf.ToString();
                    if (string.IsNullOrEmpty(instId)) continue;

                    // 先尝试读取电量属性：任何暴露了 DEVPKEY_Device_BatteryLife 的节点都必须保留，绝不因前缀过滤而漏掉！
                    int batt;
                    bool hasBatt = TryGetPnpBattery(hDevInfo, ref devData, propBuf, out batt);

                    string upper = instId.ToUpperInvariant();
                    bool isBthEnum = upper.StartsWith("BTHENUM\\");
                    bool isBthHfEnum = upper.StartsWith("BTHHFENUM\\");
                    bool isBthLe = upper.StartsWith("BTHLE\\");
                    bool isBthLeDev = upper.StartsWith("BTHLEDEVICE\\");
                    bool isHid = upper.StartsWith("HID\\");
                    bool isAudio = upper.StartsWith("SWD\\MMDEVAPI\\") || upper.StartsWith("BTHHFP\\");

                    if (!hasBatt && !isBthEnum && !isBthHfEnum && !isBthLe && !isBthLeDev && !isHid && !isAudio)
                        continue;

                    var entry = new PnpNodeEntry();
                    entry.InstanceId = instId;
                    entry.IsBtFamily = isBthEnum || isBthHfEnum || isBthLe || isBthLeDev;
                    entry.IsClassicNode = isBthEnum || isBthHfEnum;
                    entry.IsBleNode = isBthLe || isBthLeDev;
                    entry.IsRootBtNode = upper.StartsWith("BTHENUM\\DEV_") || upper.StartsWith("BTHLE\\DEV_");
                    entry.HasBattery = hasBatt;
                    entry.Battery = hasBatt ? batt : -1;

                    string cid;
                    if (TryGetPnpGuidString(hDevInfo, ref devData, ref PKEY_Device_ContainerId, propBuf, out cid))
                        entry.ContainerId = cid;

                    ulong addr = ExtractBluetoothAddressFromInstanceId(instId);
                    if (addr == 0)
                    {
                        string addrProp;
                        if (TryGetPnpString(hDevInfo, ref devData, ref PKEY_Bt_DeviceAddress, propBuf, out addrProp))
                            addr = ParseAddress(addrProp);
                    }
                    if (addr == 0 && devData.DevInst != 0)
                    {
                        bool pBle, pClassic;
                        addr = ResolveParentBluetoothAddress(devData.DevInst, out pBle, out pClassic);
                        if (pBle) entry.IsBleNode = true;
                        if (pClassic) entry.IsClassicNode = true;
                    }
                    entry.Address = addr;

                    bool conn;
                    if (TryGetPnpBool(hDevInfo, ref devData, ref PKEY_Device_IsConnected, propBuf, out conn))
                    {
                        entry.HasConnectedProp = true;
                        entry.IsConnected = conn;
                    }
                    else if (TryGetPnpBool(hDevInfo, ref devData, ref PKEY_Aep_IsConnected, propBuf, out conn))
                    {
                        entry.HasConnectedProp = true;
                        entry.IsConnected = conn;
                    }

                    string fname;
                    if (TryGetPnpString(hDevInfo, ref devData, ref PKEY_Device_FriendlyName, propBuf, out fname))
                        entry.Name = fname;
                    string desc;
                    if (TryGetPnpString(hDevInfo, ref devData, ref PKEY_Device_DeviceDesc, propBuf, out desc))
                        entry.DeviceDesc = desc;
                    string cls;
                    if (TryGetPnpString(hDevInfo, ref devData, ref PKEY_Device_Class, propBuf, out cls))
                        entry.ClassName = cls;
                    string svc;
                    if (TryGetPnpString(hDevInfo, ref devData, ref PKEY_Device_Service, propBuf, out svc))
                        entry.ServiceName = svc;

                    nodes.Add(entry);
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(hDevInfo);
            }
            return nodes;
        }

        private static List<BtDevice> EnumeratePnpDevices()
        {
            List<PnpNodeEntry> nodes = ReadPresentPnpNodes();
            var containerToAddr = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);
            var addrToDevice = new Dictionary<ulong, BtDevice>();
            var addrBatteries = new Dictionary<ulong, List<int>>();

            // 第一遍：收集所有明确带蓝牙地址的节点，建立 ContainerId -> Address 映射
            for (int i = 0; i < nodes.Count; i++)
            {
                PnpNodeEntry n = nodes[i];
                if (n.Address == 0) continue;
                string cid = NormGuid(n.ContainerId);
                if (cid.Length > 0 && !containerToAddr.ContainsKey(cid))
                    containerToAddr[cid] = n.Address;
            }

            // 第二遍：把有地址或能通过 ContainerId 关联到蓝牙地址的节点汇总成 BtDevice
            for (int i = 0; i < nodes.Count; i++)
            {
                PnpNodeEntry n = nodes[i];
                ulong addr = n.Address;
                string cid = NormGuid(n.ContainerId);
                if (addr == 0 && cid.Length > 0)
                {
                    containerToAddr.TryGetValue(cid, out addr);
                }
                if (addr == 0) continue;

                BtDevice d;
                if (!addrToDevice.TryGetValue(addr, out d))
                {
                    d = new BtDevice();
                    d.Address = addr;
                    addrToDevice[addr] = d;
                }

                if (n.IsClassicNode) d.IsClassic = true;
                if (n.IsBleNode) d.IsBle = true;
                if (string.IsNullOrEmpty(d.ContainerId) && !string.IsNullOrEmpty(n.ContainerId))
                    d.ContainerId = n.ContainerId;

                string candidateName = !string.IsNullOrEmpty(n.Name) ? n.Name : n.DeviceDesc;
                if (n.IsRootBtNode && !string.IsNullOrEmpty(candidateName) && !IsGenericNodeName(candidateName))
                {
                    d.Name = CleanDeviceName(candidateName);
                }
                else if ((string.IsNullOrEmpty(d.Name) || IsGenericNodeName(d.Name)) &&
                         !string.IsNullOrEmpty(candidateName) && !IsGenericNodeName(candidateName))
                {
                    d.Name = CleanDeviceName(candidateName);
                }
                else if (IsBetterDeviceName(candidateName, d.Name) && n.IsRootBtNode)
                {
                    d.Name = CleanDeviceName(candidateName);
                }

                // 连接状态：优先采用根设备节点或显式 IsConnected 属性
                if (n.HasConnectedProp)
                {
                    if (n.IsRootBtNode)
                        d.Connected = n.IsConnected;
                    else if (n.IsConnected)
                        d.Connected = true;
                }

                // 分类线索：结合 PnP Class、Service、InstanceId UUID、节点描述
                string hint = InferCategoryFromNode(n);
                AppendCategoryHint(d, hint);

                if (n.HasBattery && n.Battery >= 0 && n.Battery <= 100)
                {
                    List<int> bList;
                    if (!addrBatteries.TryGetValue(addr, out bList))
                    {
                        bList = new List<int>();
                        addrBatteries[addr] = bList;
                    }
                    bList.Add(n.Battery);

                    d.Battery = n.Battery;
                    d.QueryStatus = BatteryQueryStatus.Fresh;
                    d.QuerySource = "pnp-devnode";
                    d.BatterySampledAt = DateTime.Now;
                }
            }

            // 对多组件电量节点（如部分 BLE 双耳暴露两个独立 0x180F 节点）填充左右耳
            foreach (var kv in addrBatteries)
            {
                List<int> bList = kv.Value;
                BtDevice d;
                if (bList != null && bList.Count >= 2 && addrToDevice.TryGetValue(kv.Key, out d) && d.IsAudio && !d.HasSplit)
                {
                    d.BatteryLeft = bList[0];
                    d.BatteryRight = bList[1];
                    if (bList.Count >= 3) d.BatteryCase = bList[2];
                    d.Battery = (d.BatteryLeft + d.BatteryRight) / 2;
                }
            }

            // 纯内存返回，绝不调用 TryCheckBleConnection (FromBluetoothAddressAsync)，杜绝后台 BLE 无线电寻呼干扰耳机音频
            var result = new List<BtDevice>();
            foreach (var kv in addrToDevice)
            {
                result.Add(kv.Value);
            }
            return result;
        }

        private static string InferCategoryFromNode(PnpNodeEntry n)
        {
            if (n == null) return "";
            var sb = new StringBuilder();
            string inst = (n.InstanceId ?? "").ToLowerInvariant();
            string cls = (n.ClassName ?? "").ToLowerInvariant();
            string svc = (n.ServiceName ?? "").ToLowerInvariant();
            string desc = ((n.Name ?? "") + " " + (n.DeviceDesc ?? "")).ToLowerInvariant();

            if (cls == "keyboard" || svc == "kbdhid" || desc.IndexOf("keyboard", StringComparison.Ordinal) >= 0 || desc.IndexOf("键盘", StringComparison.Ordinal) >= 0)
                sb.Append("Input.Keyboard;");
            if (cls == "mouse" || svc == "mouhid" || desc.IndexOf("mouse", StringComparison.Ordinal) >= 0 || desc.IndexOf("鼠标", StringComparison.Ordinal) >= 0)
                sb.Append("Input.Mouse;");
            if (cls == "hidclass" || inst.IndexOf("00001812-", StringComparison.Ordinal) >= 0 || inst.IndexOf("00001124-", StringComparison.Ordinal) >= 0)
                sb.Append("Input.HID;");
            if (cls == "audioendpoint" || cls == "media" ||
                inst.StartsWith("bthhfenum\\") ||
                inst.IndexOf("0000111e-", StringComparison.Ordinal) >= 0 ||
                inst.IndexOf("0000110b-", StringComparison.Ordinal) >= 0 ||
                inst.IndexOf("0000184e-", StringComparison.Ordinal) >= 0 ||
                CategoryLooksAudio(desc))
                sb.Append("Audio.Headphone;");
            return sb.ToString();
        }

        /// <summary>
        /// 从 PnP DeviceInstanceId 提取 12 位蓝牙 MAC 地址。
        /// 先剔除 {...} GUID 段（避免把蓝牙基础 UUID 末尾 00805F9B34FB 误判为设备地址），
        /// 再提取 DEV_XXXXXXXXXXXX、BLUETOOTHDEVICE_XXXXXXXXXXXX 或任意 12 位十六进制地址段。
        /// </summary>
        public static ulong ExtractBluetoothAddressFromInstanceId(string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId)) return 0;

            var stripped = new StringBuilder(instanceId.Length);
            bool inBraces = false;
            for (int i = 0; i < instanceId.Length; i++)
            {
                char c = instanceId[i];
                if (c == '{') { inBraces = true; stripped.Append('_'); continue; }
                if (c == '}') { inBraces = false; stripped.Append('_'); continue; }
                if (!inBraces) stripped.Append(char.ToUpperInvariant(c));
            }

            string s = stripped.ToString();

            int searchFrom = 0;
            while (searchFrom < s.Length)
            {
                int devIdx = s.IndexOf("DEV_", searchFrom, StringComparison.Ordinal);
                if (devIdx < 0 || devIdx + 16 > s.Length) break;
                string cand = s.Substring(devIdx + 4, 12);
                if (IsValidMacHex12(cand)) return ParseAddress(cand);
                searchFrom = devIdx + 4;
            }

            int btIdx = s.IndexOf("BLUETOOTHDEVICE_", StringComparison.Ordinal);
            if (btIdx >= 0 && btIdx + 28 <= s.Length)
            {
                string cand = s.Substring(btIdx + 16, 12);
                if (IsValidMacHex12(cand)) return ParseAddress(cand);
            }

            string[] tokens = s.Split(new char[] { '\\', '&', '_', '#', '-', ':', ';' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < tokens.Length; i++)
            {
                string tok = tokens[i];
                if (tok.Length == 12 && IsValidMacHex12(tok))
                    return ParseAddress(tok);
            }
            return 0;
        }

        private static bool IsValidMacHex12(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length != 12) return false;
            if (s == "000000000000" || s == "FFFFFFFFFFFF" || s == "00805F9B34FB") return false;
            for (int i = 0; i < 12; i++)
            {
                char c = s[i];
                bool ok = (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f');
                if (!ok) return false;
            }
            return true;
        }

        private static bool TryGetPnpBattery(IntPtr hDevInfo, ref SP_DEVINFO_DATA devData, byte[] buf, out int percent)
        {
            if (TryGetPnpBatteryByKey(hDevInfo, ref devData, ref PKEY_Device_BatteryLife, buf, out percent))
                return true;
            if (TryGetPnpBatteryByKey(hDevInfo, ref devData, ref PKEY_Device_BatteryLifeAlt1, buf, out percent))
                return true;
            return false;
        }

        private static bool TryGetPnpBatteryByKey(IntPtr hDevInfo, ref SP_DEVINFO_DATA devData, ref DEVPROPKEY key, byte[] buf, out int percent)
        {
            percent = -1;
            uint propType;
            uint reqSize;
            if (!SetupDiGetDevicePropertyW(hDevInfo, ref devData, ref key, out propType, buf, (uint)buf.Length, out reqSize, 0))
                return false;
            if (reqSize == 0) return false;

            int val = -1;
            if (propType == DEVPROP_TYPE_BYTE && reqSize >= 1)
                val = buf[0];
            else if (propType == DEVPROP_TYPE_SBYTE && reqSize >= 1)
                val = (sbyte)buf[0];
            else if ((propType == DEVPROP_TYPE_INT16 || propType == DEVPROP_TYPE_UINT16) && reqSize >= 2)
                val = BitConverter.ToInt16(buf, 0);
            else if ((propType == DEVPROP_TYPE_INT32 || propType == DEVPROP_TYPE_UINT32) && reqSize >= 4)
                val = BitConverter.ToInt32(buf, 0);

            if (val >= 0 && val <= 100)
            {
                percent = val;
                return true;
            }
            return false;
        }

        private static bool TryGetPnpBool(IntPtr hDevInfo, ref SP_DEVINFO_DATA devData, ref DEVPROPKEY key, byte[] buf, out bool value)
        {
            value = false;
            uint propType;
            uint reqSize;
            if (!SetupDiGetDevicePropertyW(hDevInfo, ref devData, ref key, out propType, buf, (uint)buf.Length, out reqSize, 0))
                return false;
            if (propType == DEVPROP_TYPE_BOOLEAN && reqSize >= 1)
            {
                value = buf[0] != 0;
                return true;
            }
            return false;
        }

        private static bool TryGetPnpGuidString(IntPtr hDevInfo, ref SP_DEVINFO_DATA devData, ref DEVPROPKEY key, byte[] buf, out string guidText)
        {
            guidText = "";
            uint propType;
            uint reqSize;
            if (!SetupDiGetDevicePropertyW(hDevInfo, ref devData, ref key, out propType, buf, (uint)buf.Length, out reqSize, 0))
                return false;
            if (propType == DEVPROP_TYPE_GUID && reqSize >= 16)
            {
                byte[] gbytes = new byte[16];
                Array.Copy(buf, 0, gbytes, 0, 16);
                Guid g = new Guid(gbytes);
                if (g == Guid.Empty) return false;
                guidText = g.ToString("B");
                return true;
            }
            if (propType == DEVPROP_TYPE_STRING && reqSize >= 2)
            {
                guidText = DecodeUtf16Buffer(buf, (int)reqSize);
                return !string.IsNullOrEmpty(guidText);
            }
            return false;
        }

        private static bool TryGetPnpString(IntPtr hDevInfo, ref SP_DEVINFO_DATA devData, ref DEVPROPKEY key, byte[] buf, out string text)
        {
            text = "";
            uint propType;
            uint reqSize;
            if (!SetupDiGetDevicePropertyW(hDevInfo, ref devData, ref key, out propType, buf, (uint)buf.Length, out reqSize, 0))
                return false;
            if ((propType == DEVPROP_TYPE_STRING || propType == DEVPROP_TYPE_STRING_LIST) && reqSize >= 2)
            {
                text = DecodeUtf16Buffer(buf, (int)reqSize);
                return !string.IsNullOrEmpty(text);
            }
            return false;
        }

        private static string DecodeUtf16Buffer(byte[] buf, int byteCount)
        {
            if (buf == null || byteCount <= 0) return "";
            int len = Math.Min(buf.Length, byteCount);
            string s = Encoding.Unicode.GetString(buf, 0, len);
            int nul = s.IndexOf('\0');
            if (nul >= 0) s = s.Substring(0, nul);
            return s.Trim();
        }
        private class PropHit
        {
            public string AddressKey;
            public string ContainerId;
            public string AepId;
            public string Name;
            public string Source;
            public string Categories;
            public int Battery = -1;
            public int BatteryLeft = -1;
            public int BatteryRight = -1;
            public int BatteryCase = -1;
            public bool HasBatteryValue;
            public bool MatchedProtocol;
        }

        private class PropSnapshot
        {
            public DateTime TakenUtc;
            public BatteryQueryStatus Status = BatteryQueryStatus.Failed;
            public string Error = "";
            public bool HadTimeout;
            public List<PropHit> Hits = new List<PropHit>();
        }

        private static readonly object _snapLock = new object();
        private static PropSnapshot _snapshot;
        private const int SnapshotFreshMs = 3500;

        /// <summary>
        /// 对多台设备读取电量：
        /// 1. 先走共享快照（SetupAPI PnP DevNode 树 + CfgMgr32 父节点回溯 + 已配对 DeviceInterface + 已配对 AEP + 父容器）；
        /// 2. 针对华为 FreeBuds Pro 5 等支持 5A 协议的音频耳机，优先走非破坏式 SPP 5A（Winsock2 AF_BTH RFCOMM port=1/16/2/3 + WinRT 回退），直接拿到左右耳与充电盒电量；
        /// 3. 尚未拿到电量的设备走标准 BLE GATT 0x180F / 0x2A19（含通过 ContainerId 查找关联 BLE 节点）；
        /// 4. 音频设备兜底尝试被动非破坏式 HFP 读取，全程绝不调用 BluetoothSetServiceState 断链。
        /// </summary>
        public static void FillBatteries(IList<BtDevice> devices, int timeoutMs, bool allowGattFallback)
        {
            if (devices == null || devices.Count == 0) return;
            PropSnapshot snap = GetSnapshot(timeoutMs);
            for (int i = 0; i < devices.Count; i++)
                ApplySnapshot(devices[i], snap);

            if (!allowGattFallback) return;
            for (int i = 0; i < devices.Count; i++)
            {
                BtDevice d = devices[i];
                if (d == null) continue;

                // 通道 2：针对华为/荣耀耳机，若尚未在当前连接会话中被标记为无 5A 响应，则尝试轻量非破坏式 SPP 5A（若已记住有效端口则仅连单端口，一旦未响应则 30 分钟内不再打扰蓝牙基带）
                bool isHuaweiLike = LooksLikeSppCapableAudio(d.Name);
                bool canTrySpp = false;
                if (d.IsAudio && isHuaweiLike)
                {
                    lock (_wsaLock)
                    {
                        DateTime missTime;
                        canTrySpp = !_rfcommMissAt.TryGetValue(d.Address, out missTime) ||
                                    (DateTime.UtcNow - missTime).TotalMinutes >= 30.0 ||
                                    !d.HasBattery;
                    }
                }
                if (canTrySpp)
                {
                    try
                    {
                        if (TrySpp5ABattery(d, Math.Min(2500, timeoutMs)))
                        {
                            d.QueryStatus = BatteryQueryStatus.Fresh;
                            if (string.IsNullOrEmpty(d.QuerySource)) d.QuerySource = "spp-5a";
                            d.BatterySampledAt = DateTime.Now;
                            LogQuery(d, BatteryQueryStatus.Fresh, d.QuerySource + " batt=" + d.Battery + " split=" + d.BatteryLeft + "/" + d.BatteryRight + "/" + d.BatteryCase);
                        }
                        else
                        {
                            lock (_wsaLock) { _rfcommMissAt[d.Address] = DateTime.UtcNow; }
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (_wsaLock) { _rfcommMissAt[d.Address] = DateTime.UtcNow; }
                        LogQuery(d, ClassifyException(ex), "spp-5a " + BriefError(ex));
                    }
                }

                // 通道 3：标准 BLE GATT 0x180F / 0x2A19（仅在 PnP 内核属性未提供电量时才读取，且优先读系统 Cached 缓存，零空中包干扰键鼠/音频）
                if (!d.HasBattery)
                {
                    try
                    {
                        if (TryBleGatt(d, snap, Math.Min(3500, timeoutMs)))
                        {
                            d.QueryStatus = BatteryQueryStatus.Fresh;
                            d.QuerySource = "gatt-180f";
                            d.BatterySampledAt = DateTime.Now;
                            LogQuery(d, BatteryQueryStatus.Fresh, "gatt-180f batt=" + d.Battery);
                        }
                    }
                    catch (Exception ex)
                    {
                        BatteryQueryStatus st = ClassifyException(ex);
                        if (d.QueryStatus != BatteryQueryStatus.NoSystemValue && d.QueryStatus != BatteryQueryStatus.Unmatched)
                            d.QueryStatus = st;
                        LogQuery(d, st, "gatt " + BriefError(ex));
                    }
                }

                // 通道 4：非华为命名但仍属音频且尚未拿到电量的设备，也尝试一次非破坏式 SPP 5A
                if (d.IsAudio && !isHuaweiLike && !d.HasBattery)
                {
                    try
                    {
                        if (TrySpp5ABattery(d, Math.Min(3500, timeoutMs)))
                        {
                            d.QueryStatus = BatteryQueryStatus.Fresh;
                            if (string.IsNullOrEmpty(d.QuerySource)) d.QuerySource = "spp-5a";
                            d.BatterySampledAt = DateTime.Now;
                            LogQuery(d, BatteryQueryStatus.Fresh, d.QuerySource + " batt=" + d.Battery);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogQuery(d, ClassifyException(ex), "spp-5a-generic " + BriefError(ex));
                    }
                }

                // 注意：绝不主动连接 0000111e (HFP 免提通道)，避免触发耳机在立体声 A2DP 与通话模式之间切换导致断音
            }
        }

        /// <summary>单台查询（内部仍走共享快照）。</summary>
        public static void FillBattery(BtDevice d)
        {
            if (d == null) return;
            var list = new List<BtDevice>();
            list.Add(d);
            FillBatteries(list, 8000, true);
        }

        private static PropSnapshot GetSnapshot(int timeoutMs)
        {
            lock (_snapLock)
            {
                if (_snapshot != null &&
                    _snapshot.Status == BatteryQueryStatus.Fresh &&
                    (DateTime.UtcNow - _snapshot.TakenUtc).TotalMilliseconds < SnapshotFreshMs)
                    return _snapshot;
            }

            PropSnapshot snap = new PropSnapshot();
            snap.TakenUtc = DateTime.UtcNow;
            try
            {
                // 仅扫描系统当前 PnP DevNode 树上的 DEVPKEY_Device_BatteryLife（纯内核内存直读，0 蓝牙无线电开销，不调 WinRT FindAllAsync）
                CollectPnpHits(snap);

                snap.Status = (snap.Hits.Count > 0 || !snap.HadTimeout)
                    ? BatteryQueryStatus.Fresh
                    : BatteryQueryStatus.Timeout;
            }
            catch (Exception ex)
            {
                snap.Status = snap.Hits.Count > 0 ? BatteryQueryStatus.Fresh : ClassifyException(ex);
                snap.Error = BriefError(ex);
                LogQuery("snapshot", "", snap.Status, snap.Error);
            }

            lock (_snapLock) { _snapshot = snap; }
            return snap;
        }

        private static void CollectPnpHits(PropSnapshot snap)
        {
            try
            {
                List<BtDevice> pnpDevs = EnumeratePnpDevices();
                for (int i = 0; i < pnpDevs.Count; i++)
                {
                    BtDevice p = pnpDevs[i];
                    if (p == null || p.Address == 0) continue;
                    var hit = new PropHit();
                    hit.Source = "pnp-devnode";
                    hit.Name = p.Name ?? "";
                    hit.AddressKey = NormalizeAddressKey(p.AddressText);
                    hit.ContainerId = p.ContainerId ?? "";
                    hit.Categories = p.CategoryHint ?? "";
                    hit.MatchedProtocol = true;
                    if (p.HasBattery)
                    {
                        hit.Battery = p.Battery;
                        hit.BatteryLeft = p.BatteryLeft;
                        hit.BatteryRight = p.BatteryRight;
                        hit.BatteryCase = p.BatteryCase;
                        hit.HasBatteryValue = true;
                    }
                    snap.Hits.Add(hit);
                }
            }
            catch (Exception ex)
            {
                LogQuery("snapshot", "pnp", BatteryQueryStatus.Failed, "pnp_scan " + BriefError(ex));
            }
        }
        private static void ApplySnapshot(BtDevice d, PropSnapshot snap)
        {
            if (d == null) return;
            if (snap == null)
            {
                if (!d.HasBattery) d.QueryStatus = BatteryQueryStatus.Failed;
                return;
            }

            string key = NormalizeAddressKey(d.AddressText);
            string container = d.ContainerId == null ? "" : NormGuid(d.ContainerId);

            // 第一轮先从已匹配地址或同名节点的 hit 中补齐 ContainerId、AepId 与 CategoryHint
            for (int i = 0; i < snap.Hits.Count; i++)
            {
                PropHit h = snap.Hits[i];
                if (h == null) continue;
                bool addrMatch = (key.Length == 12 && h.AddressKey == key);
                bool nameMatch = (!string.IsNullOrEmpty(d.Name) && !string.IsNullOrEmpty(h.Name) &&
                                  string.Equals(CleanDeviceName(d.Name), CleanDeviceName(h.Name), StringComparison.OrdinalIgnoreCase));
                if (addrMatch || nameMatch)
                {
                    if (container.Length == 0 && !string.IsNullOrEmpty(h.ContainerId))
                    {
                        d.ContainerId = h.ContainerId;
                        container = NormGuid(h.ContainerId);
                    }
                    if (string.IsNullOrEmpty(d.AepId) && !string.IsNullOrEmpty(h.AepId) && h.Source == "if-ble")
                        d.AepId = h.AepId;
                    AppendCategoryHint(d, h.Categories);
                }
            }

            PropHit best = null;
            PropHit containerHit = null;
            bool sawNode = false;

            for (int i = 0; i < snap.Hits.Count; i++)
            {
                PropHit h = snap.Hits[i];
                if (h == null) continue;
                if (key.Length == 12 && h.AddressKey == key)
                {
                    sawNode = true;
                    if (h.HasBatteryValue)
                    {
                        best = h;
                        break;
                    }
                    if (best == null) best = h;
                }
                if (container.Length > 0 && !string.IsNullOrEmpty(h.ContainerId) &&
                    string.Equals(NormGuid(h.ContainerId), container, StringComparison.OrdinalIgnoreCase))
                {
                    sawNode = true;
                    if (h.HasBatteryValue && containerHit == null) containerHit = h;
                    if (string.IsNullOrEmpty(d.AepId) && !string.IsNullOrEmpty(h.AepId) && h.Source == "if-ble")
                        d.AepId = h.AepId;
                    AppendCategoryHint(d, h.Categories);
                }
            }

            if ((best == null || !best.HasBatteryValue) && containerHit != null && containerHit.HasBatteryValue)
                best = containerHit;

            if (best != null && best.HasBatteryValue)
            {
                d.Battery = best.Battery;
                if (best.BatteryLeft >= 0 && best.BatteryRight >= 0)
                {
                    d.BatteryLeft = best.BatteryLeft;
                    d.BatteryRight = best.BatteryRight;
                    d.BatteryCase = best.BatteryCase;
                }
                d.QueryStatus = BatteryQueryStatus.Fresh;
                d.QuerySource = best.Source;
                d.BatterySampledAt = DateTime.Now;
                AppendCategoryHint(d, best.Categories);
                if (!string.IsNullOrEmpty(best.ContainerId) && string.IsNullOrEmpty(d.ContainerId))
                    d.ContainerId = best.ContainerId;
                LogQuery(d, BatteryQueryStatus.Fresh, best.Source + " batt=" + d.Battery);
                return;
            }

            if (d.HasBattery && d.QueryStatus == BatteryQueryStatus.Fresh)
                return;

            if (snap.Status == BatteryQueryStatus.Timeout && !sawNode)
            {
                d.QueryStatus = BatteryQueryStatus.Timeout;
                d.QuerySource = "snapshot";
                LogQuery(d, BatteryQueryStatus.Timeout, "snapshot_timeout");
                return;
            }

            if (sawNode || best != null)
            {
                d.QueryStatus = BatteryQueryStatus.NoSystemValue;
                d.QuerySource = best != null ? best.Source : "pnp";
                LogQuery(d, BatteryQueryStatus.NoSystemValue, d.QuerySource + " present_no_value");
                return;
            }

            d.QueryStatus = BatteryQueryStatus.Unmatched;
            d.QuerySource = "snapshot";
            LogQuery(d, BatteryQueryStatus.Unmatched, "no_node");
        }

        /// <summary>
        /// 标准 BLE 电池服务 0x180F / 0x2A19，支持单实例（键鼠/单电量耳机）及多实例（左右耳/盒）。
        /// 同时支持通过同 ContainerId 或同名的 BLE AepId 跨模读取双模设备（如 M87-BT1）的 GATT 电量。
        /// </summary>
        private static bool TryBleGatt(BtDevice d, PropSnapshot snap, int timeoutMs)
        {
            if (d == null || d.Address == 0) return false;
            var candidateIds = new List<string>();
            if (!string.IsNullOrEmpty(d.AepId)) candidateIds.Add(d.AepId);

            if (snap != null && snap.Hits != null)
            {
                string cid = NormGuid(d.ContainerId);
                for (int i = 0; i < snap.Hits.Count; i++)
                {
                    PropHit h = snap.Hits[i];
                    if (h == null || string.IsNullOrEmpty(h.AepId)) continue;
                    bool sameContainer = cid.Length > 0 && string.Equals(NormGuid(h.ContainerId), cid, StringComparison.OrdinalIgnoreCase);
                    bool sameName = !string.IsNullOrEmpty(d.Name) && string.Equals(CleanDeviceName(h.Name), CleanDeviceName(d.Name), StringComparison.OrdinalIgnoreCase);
                    if ((sameContainer || sameName) && !candidateIds.Contains(h.AepId))
                        candidateIds.Add(h.AepId);
                }
            }

            Windows.Devices.Bluetooth.BluetoothLEDevice le = null;
            for (int i = 0; i < candidateIds.Count; i++)
            {
                try
                {
                    le = (Windows.Devices.Bluetooth.BluetoothLEDevice)AwaitWith(
                        Windows.Devices.Bluetooth.BluetoothLEDevice.FromIdAsync(candidateIds[i]),
                        typeof(Windows.Devices.Bluetooth.BluetoothLEDevice), Math.Min(2000, timeoutMs));
                    if (le != null) break;
                }
                catch { le = null; }
            }
            if (le == null)
            {
                try
                {
                    le = (Windows.Devices.Bluetooth.BluetoothLEDevice)AwaitWith(
                        Windows.Devices.Bluetooth.BluetoothLEDevice.FromBluetoothAddressAsync(d.Address),
                        typeof(Windows.Devices.Bluetooth.BluetoothLEDevice), Math.Min(2500, timeoutMs));
                }
                catch { le = null; }
            }
            if (le == null) return false;

            var battSvcUuid = new Guid("0000180f-0000-1000-8000-00805f9b34fb");
            var chrUuid = new Guid("00002a19-0000-1000-8000-00805f9b34fb");

            var svc = (Windows.Devices.Bluetooth.GenericAttributeProfile.GattDeviceServicesResult)AwaitWith(
                le.GetGattServicesForUuidAsync(battSvcUuid),
                typeof(Windows.Devices.Bluetooth.GenericAttributeProfile.GattDeviceServicesResult), timeoutMs);
            if (svc == null || svc.Status != Windows.Devices.Bluetooth.GenericAttributeProfile.GattCommunicationStatus.Success || svc.Services.Count == 0)
                return false;

            var values = new List<int>();
            for (int s = 0; s < svc.Services.Count; s++)
            {
                var service = svc.Services[s];
                if (service == null) continue;
                var chr = (Windows.Devices.Bluetooth.GenericAttributeProfile.GattCharacteristicsResult)AwaitWith(
                    service.GetCharacteristicsForUuidAsync(chrUuid),
                    typeof(Windows.Devices.Bluetooth.GenericAttributeProfile.GattCharacteristicsResult), timeoutMs);
                if (chr == null || chr.Status != Windows.Devices.Bluetooth.GenericAttributeProfile.GattCommunicationStatus.Success || chr.Characteristics.Count == 0)
                    continue;

                for (int c = 0; c < chr.Characteristics.Count; c++)
                {
                    var ch = chr.Characteristics[c];
                    if (ch == null) continue;
                    int pct;
                    if (TryReadGattCharacteristicPercent(ch, Windows.Devices.Bluetooth.BluetoothCacheMode.Cached, timeoutMs, out pct) ||
                        TryReadGattCharacteristicPercent(ch, Windows.Devices.Bluetooth.BluetoothCacheMode.Uncached, timeoutMs, out pct))
                    {
                        values.Add(pct);
                    }
                }
            }

            if (values.Count == 0) return false;
            if (values.Count >= 2 && d.IsAudio)
            {
                d.BatteryLeft = values[0];
                d.BatteryRight = values[1];
                if (values.Count >= 3) d.BatteryCase = values[2];
                d.Battery = (d.BatteryLeft + d.BatteryRight) / 2;
            }
            else
            {
                d.Battery = values[0];
            }
            return true;
        }

        private static bool TryReadGattCharacteristicPercent(
            Windows.Devices.Bluetooth.GenericAttributeProfile.GattCharacteristic ch,
            Windows.Devices.Bluetooth.BluetoothCacheMode mode,
            int timeoutMs,
            out int percent)
        {
            percent = -1;
            try
            {
                var val = (Windows.Devices.Bluetooth.GenericAttributeProfile.GattReadResult)AwaitWith(
                    ch.ReadValueAsync(mode),
                    typeof(Windows.Devices.Bluetooth.GenericAttributeProfile.GattReadResult), Math.Min(2000, timeoutMs));
                if (val == null || val.Status != Windows.Devices.Bluetooth.GenericAttributeProfile.GattCommunicationStatus.Success || val.Value == null || val.Value.Length == 0)
                    return false;
                var reader = Windows.Storage.Streams.DataReader.FromBuffer(val.Value);
                byte pct = reader.ReadByte();
                if (pct <= 100)
                {
                    percent = pct;
                    return true;
                }
            }
            catch { }
            return false;
        }

        // ---- 非破坏式 SPP 5A（Winsock2 AF_BTH RFCOMM port=1 + WinRT 双路）与被动 HFP 电量查询 ----

        private static bool LooksLikeSppCapableAudio(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string n = name.ToLowerInvariant();
            return n.IndexOf("huawei", StringComparison.Ordinal) >= 0 ||
                   n.IndexOf("freebuds", StringComparison.Ordinal) >= 0 ||
                   n.IndexOf("freelace", StringComparison.Ordinal) >= 0 ||
                   n.IndexOf("freeclip", StringComparison.Ordinal) >= 0 ||
                   n.IndexOf("honor", StringComparison.Ordinal) >= 0 ||
                   n.IndexOf("earbuds", StringComparison.Ordinal) >= 0 ||
                   n.IndexOf("华为", StringComparison.Ordinal) >= 0 ||
                   n.IndexOf("荣耀", StringComparison.Ordinal) >= 0;
        }

        public static ushort Crc16Xmodem(byte[] data)
        {
            ushort crc = 0;
            for (int idx = 0; idx < data.Length; idx++)
            {
                crc ^= (ushort)(data[idx] << 8);
                for (int i = 0; i < 8; i++)
                    crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ 0x1021) : (ushort)(crc << 1);
            }
            return crc;
        }

        public static byte[] BuildHuawei5AFrame(byte serviceId, byte commandId, byte[] tlvPayload)
        {
            int payloadLen = tlvPayload == null ? 0 : tlvPayload.Length;
            int dataLen = payloadLen + 3;
            byte[] body = new byte[4 + 2 + payloadLen];
            body[0] = 0x5A;
            body[1] = (byte)((dataLen >> 8) & 0xFF);
            body[2] = (byte)(dataLen & 0xFF);
            body[3] = 0x00;
            body[4] = serviceId;
            body[5] = commandId;
            if (payloadLen > 0)
                Array.Copy(tlvPayload, 0, body, 6, payloadLen);

            ushort crc = Crc16Xmodem(body);
            byte[] frame = new byte[body.Length + 2];
            Array.Copy(body, 0, frame, 0, body.Length);
            frame[body.Length] = (byte)((crc >> 8) & 0xFF);
            frame[body.Length + 1] = (byte)(crc & 0xFF);
            return frame;
        }

        private static bool ConnectWinsockRfcomm(IntPtr sock, ref SOCKADDR_BTH addr, int timeoutMs)
        {
            uint nonBlock = 1;
            ioctlsocket(sock, FIONBIO, ref nonBlock);
            int rc = connect(sock, ref addr, Marshal.SizeOf(typeof(SOCKADDR_BTH)));
            if (rc == 0) return true;

            var writeSet = new FD_SET_1 { fd_count = 1, fd_0 = sock };
            var exceptSet = new FD_SET_1 { fd_count = 1, fd_0 = sock };
            var tv = new TIMEVAL
            {
                tv_sec = timeoutMs / 1000,
                tv_usec = (timeoutMs % 1000) * 1000
            };
            int sel = selectWrite(0, IntPtr.Zero, ref writeSet, ref exceptSet, ref tv);
            if (sel <= 0) return false;
            if (exceptSet.fd_count > 0 && writeSet.fd_count == 0) return false;
            return writeSet.fd_count > 0;
        }

        private static bool ApplyParsedHuaweiBattery(BtDevice d, int overall, int left, int right, int casePct, string source)
        {
            if (d == null) return false;
            if (left >= 0 && right >= 0)
            {
                d.BatteryLeft = left;
                d.BatteryRight = right;
                d.BatteryCase = casePct;
            }
            else if (left >= 0 && right < 0)
            {
                d.BatteryLeft = left;
                d.BatteryRight = left;
                d.BatteryCase = casePct;
            }
            else if (right >= 0 && left < 0)
            {
                d.BatteryLeft = right;
                d.BatteryRight = right;
                d.BatteryCase = casePct;
            }

            if (overall >= 0 && overall <= 100)
                d.Battery = overall;
            else if (d.BatteryLeft >= 0 && d.BatteryRight >= 0)
                d.Battery = (d.BatteryLeft + d.BatteryRight) / 2;
            else if (d.BatteryLeft >= 0)
                d.Battery = d.BatteryLeft;
            else if (d.BatteryRight >= 0)
                d.Battery = d.BatteryRight;

            if (d.HasBattery)
            {
                d.QuerySource = source;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 使用 Win32 Winsock2 AF_BTH RFCOMM 原生套接字连接华为 FreeBuds Pro 5（HarmonyOS 7.0.0.368）：
        /// OpenFreebuds 与 BudsBar 实测表明，华为 FreeBuds Pro 3/4/5 的私有 MBB (0x5A) 控制服务监听在 RFCOMM Channel 1（port=1），
        /// 部分双连或固件状态下位于通道 16 / 2 / 3 或 SPP UUID 00001101。
        /// </summary>
        private static bool TryWinsockSpp5ABattery(BtDevice d, int timeoutMs)
        {
            if (d == null || d.Address == 0) return false;
            EnsureWsaStarted();

            byte[] frameSimple = BuildHuawei5AFrame(0x01, 0x08, null);
            byte[] frameExplicit = BuildHuawei5AFrame(0x01, 0x08, new byte[] { 0x01, 0x00, 0x02, 0x00, 0x03, 0x00 });
            byte[] frameExtended = BuildHuawei5AFrame(0x01, 0x08, new byte[] { 0x01, 0x00, 0x02, 0x00, 0x03, 0x00, 0x04, 0x00, 0x05, 0x00 });

            uint rememberedPort = 999;
            lock (_wsaLock)
            {
                if (!_rfcommWorkingPort.TryGetValue(d.Address, out rememberedPort))
                    rememberedPort = 999;
            }
            // 仅连接华为 MBB 专用通道 port 1，绝不触碰其他 RFCOMM 通道（如 HFP 音频通道），避免影响耳机声音
            uint[] ports = new uint[] { 1 };
            Guid sppUuid = new Guid("00001101-0000-1000-8000-00805f9b34fb");

            for (int pIdx = 0; pIdx < ports.Length; pIdx++)
            {
                uint port = ports[pIdx];
                IntPtr sock = socket(AF_BTH, SOCK_STREAM, BTHPROTO_RFCOMM);
                if (sock == IntPtr.Zero || sock == new IntPtr(-1))
                    continue;

                try
                {
                    var sa = new SOCKADDR_BTH();
                    sa.addressFamily = (ushort)AF_BTH;
                    sa.btAddr = d.Address;
                    sa.serviceClassId = (port == 0) ? sppUuid : Guid.Empty;
                    sa.port = port;

                    int connTimeout = (port == 1) ? Math.Min(1100, timeoutMs) : 650;
                    if (!ConnectWinsockRfcomm(sock, ref sa, connTimeout))
                        continue;

                    LogQuery(d, BatteryQueryStatus.Querying, "winsock_rfcomm_connected port=" + port);

                    // 发送三种兼容格式的 01 08 电量请求帧（兼容 FreeBuds 4i / Pro 3 / Pro 4 / Pro 5 HarmonyOS 7.0.0.368）
                    send(sock, frameSimple, frameSimple.Length, 0);
                    send(sock, frameExplicit, frameExplicit.Length, 0);
                    send(sock, frameExtended, frameExtended.Length, 0);

                    var acc = new List<byte>();
                    byte[] recvBuf = new byte[512];
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    while (sw.ElapsedMilliseconds < 950)
                    {
                        var readSet = new FD_SET_1 { fd_count = 1, fd_0 = sock };
                        var tv = new TIMEVAL { tv_sec = 0, tv_usec = 250000 }; // 250ms
                        int ready = select(0, ref readSet, IntPtr.Zero, IntPtr.Zero, ref tv);
                        if (ready <= 0)
                        {
                            // 若首轮未回包，补发一次显式 TLV 帧
                            if (acc.Count == 0 && sw.ElapsedMilliseconds < 400)
                                send(sock, frameExplicit, frameExplicit.Length, 0);
                            continue;
                        }

                        int n = recv(sock, recvBuf, recvBuf.Length, 0);
                        if (n <= 0) break;
                        for (int k = 0; k < n; k++) acc.Add(recvBuf[k]);

                        int overall, left, right, casePct;
                        if (TryParseHuawei5ABattery(acc, out overall, out left, out right, out casePct))
                        {
                            if (ApplyParsedHuaweiBattery(d, overall, left, right, casePct, "spp-5a-ch" + port))
                            {
                                lock (_wsaLock) { _rfcommWorkingPort[d.Address] = port; }
                                return true;
                            }
                        }

                        // 如果收到的是 HFP AT 文本而非 5A 二进制帧，也顺带解析一次
                        string ascii = Encoding.ASCII.GetString(recvBuf, 0, n);
                        if (TryParseHfpAtBattery(ascii, out overall, out left, out right, out casePct))
                        {
                            if (ApplyParsedHuaweiBattery(d, overall, left, right, casePct, "rfcomm-at-ch" + port))
                                return true;
                        }
                    }
                }
                catch { }
                finally
                {
                    try { closesocket(sock); } catch { }
                }
            }
            return false;
        }

        private static bool IsAudioOrAvrcpServiceUuid(string uuidLower)
        {
            if (string.IsNullOrEmpty(uuidLower)) return true;
            return uuidLower.StartsWith("0000111e") || // HFP
                   uuidLower.StartsWith("0000111f") || // HFP AG
                   uuidLower.StartsWith("00001108") || // HSP
                   uuidLower.StartsWith("0000110b") || // AudioSink
                   uuidLower.StartsWith("0000110a") || // AudioSource
                   uuidLower.StartsWith("0000110d") || // A2DP
                   uuidLower.StartsWith("0000110e") || // AVRCP
                   uuidLower.StartsWith("0000110c") || // AVCTP
                   uuidLower.StartsWith("00001200");   // PnP Information
        }

        private static bool TrySpp5ABattery(BtDevice d, int timeoutMs)
        {
            if (d == null || d.Address == 0) return false;

            // 1) 首选 Win32 Winsock2 AF_BTH RFCOMM 原生直连（直接支持 FreeBuds Pro 5 的 RFCOMM Channel 1）
            if (TryWinsockSpp5ABattery(d, timeoutMs))
                return true;

            // 2) 回退 WinRT RfcommDeviceService 枚举（使用修复后的 AwaitWithProgress 支持 IAsyncOperationWithProgress）
            var bt = (Windows.Devices.Bluetooth.BluetoothDevice)AwaitWith(
                Windows.Devices.Bluetooth.BluetoothDevice.FromBluetoothAddressAsync(d.Address),
                typeof(Windows.Devices.Bluetooth.BluetoothDevice), Math.Min(2000, timeoutMs));
            if (bt == null) return false;

            var svcs = (Windows.Devices.Bluetooth.Rfcomm.RfcommDeviceServicesResult)AwaitWith(
                bt.GetRfcommServicesAsync(),
                typeof(Windows.Devices.Bluetooth.Rfcomm.RfcommDeviceServicesResult), Math.Min(2000, timeoutMs));
            if (svcs == null || svcs.Services == null || svcs.Services.Count == 0) return false;

            var candidates = new List<Windows.Devices.Bluetooth.Rfcomm.RfcommDeviceService>();
            for (int i = 0; i < svcs.Services.Count; i++)
            {
                var s = svcs.Services[i];
                if (s == null) continue;
                string u = s.ServiceId.Uuid.ToString().ToLowerInvariant();
                if (u.StartsWith("00001101")) candidates.Add(s);
            }
            for (int i = 0; i < svcs.Services.Count; i++)
            {
                var s = svcs.Services[i];
                if (s == null) continue;
                string u = s.ServiceId.Uuid.ToString().ToLowerInvariant();
                if (!u.StartsWith("00001101") && !IsAudioOrAvrcpServiceUuid(u))
                    candidates.Add(s);
            }

            byte[] frameSimple = BuildHuawei5AFrame(0x01, 0x08, null);
            byte[] frameExplicit = BuildHuawei5AFrame(0x01, 0x08, new byte[] { 0x01, 0x00, 0x02, 0x00, 0x03, 0x00 });
            byte[] frameExtended = BuildHuawei5AFrame(0x01, 0x08, new byte[] { 0x01, 0x00, 0x02, 0x00, 0x03, 0x00, 0x04, 0x00, 0x05, 0x00 });

            for (int i = 0; i < candidates.Count; i++)
            {
                var svc = candidates[i];
                var sock = new Windows.Networking.Sockets.StreamSocket();
                try
                {
                    object connRes = AwaitActionWith(
                        sock.ConnectAsync(svc.ConnectionHostName, svc.ConnectionServiceName),
                        Math.Min(1500, timeoutMs));
                    if (connRes == null) { try { sock.Dispose(); } catch { } continue; }

                    try
                    {
                        AwaitWithProgress(
                            sock.OutputStream.WriteAsync(Windows.Security.Cryptography.CryptographicBuffer.CreateFromByteArray(frameSimple)),
                            typeof(uint), typeof(uint), 500);
                        AwaitWithProgress(
                            sock.OutputStream.WriteAsync(Windows.Security.Cryptography.CryptographicBuffer.CreateFromByteArray(frameExplicit)),
                            typeof(uint), typeof(uint), 500);
                        AwaitWithProgress(
                            sock.OutputStream.WriteAsync(Windows.Security.Cryptography.CryptographicBuffer.CreateFromByteArray(frameExtended)),
                            typeof(uint), typeof(uint), 500);
                    }
                    catch { }

                    var acc = new List<byte>();
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    while (sw.ElapsedMilliseconds < 800)
                    {
                        var buf = new Windows.Storage.Streams.Buffer(512);
                        var ib = (Windows.Storage.Streams.IBuffer)AwaitWithProgress(
                            sock.InputStream.ReadAsync(buf, 512, Windows.Storage.Streams.InputStreamOptions.Partial),
                            typeof(Windows.Storage.Streams.IBuffer), typeof(uint), 450);
                        if (ib == null || ib.Length == 0) break;
                        var dr = Windows.Storage.Streams.DataReader.FromBuffer(ib);
                        byte[] raw = new byte[ib.Length];
                        dr.ReadBytes(raw);
                        acc.AddRange(raw);

                        int overall, left, right, casePct;
                        if (TryParseHuawei5ABattery(acc, out overall, out left, out right, out casePct))
                        {
                            if (ApplyParsedHuaweiBattery(d, overall, left, right, casePct, "spp-5a-winrt"))
                            {
                                try { sock.Dispose(); } catch { }
                                return true;
                            }
                        }
                        if (acc.Count > 0 && acc[0] == 0x7F && !acc.Contains(0x5A))
                            break;
                    }
                }
                catch { }
                finally
                {
                    try { sock.Dispose(); } catch { }
                }
            }
            return false;
        }

        public static bool TryParseHuawei5ABattery(IList<byte> buf, out int overall, out int left, out int right, out int casePct)
        {
            overall = -1;
            left = -1;
            right = -1;
            casePct = -1;
            if (buf == null || buf.Count < 8) return false;

            int i = 0;
            while (i + 8 <= buf.Count)
            {
                if (buf[i] != 0x5A) { i++; continue; }
                int len = (buf[i + 1] << 8) | buf[i + 2];
                int frameLen = len + 5;
                if (frameLen < 8 || frameLen > 512)
                {
                    i++;
                    continue;
                }
                if (i + frameLen > buf.Count)
                    break;

                int cmd = (buf[i + 4] << 8) | buf[i + 5];
                if (cmd == 0x0127 || cmd == 0x0108)
                {
                    int p = i + 6;
                    int end = i + frameLen - 2;
                    while (p + 1 < end)
                    {
                        int type = buf[p];
                        int tlen = buf[p + 1];
                        int vp = p + 2;
                        if (vp + tlen > end) break;
                        if (type == 1 && tlen >= 1)
                        {
                            int v = buf[vp];
                            if (v > 0 && v <= 100) overall = v;
                            else if (v == 0 && overall < 0) overall = 0;
                        }
                        else if ((type == 2 || type == 4) && tlen >= 3)
                        {
                            int l = buf[vp];
                            int r = buf[vp + 1];
                            int c = buf[vp + 2];
                            if (l > 0 && l <= 100) left = l;
                            if (r > 0 && r <= 100) right = r;
                            if (c > 0 && c <= 100) casePct = c;
                        }
                        else if (type == 2 && tlen == 2)
                        {
                            int l = buf[vp];
                            int r = buf[vp + 1];
                            if (l > 0 && l <= 100) left = l;
                            if (r > 0 && r <= 100) right = r;
                        }
                        p = vp + tlen;
                    }
                    if (overall >= 0 || left >= 0 || right >= 0)
                    {
                        if (overall <= 0)
                        {
                            if (left > 0 && right > 0) overall = (left + right) / 2;
                            else if (left > 0) overall = left;
                            else if (right > 0) overall = right;
                        }
                        if (overall >= 0) return true;
                    }
                }
                i += frameLen;
            }
            return false;
        }
        public static bool TryParseHfpAtBattery(string text, out int overall, out int left, out int right, out int casePct)
        {
            overall = -1;
            left = -1;
            right = -1;
            casePct = -1;
            if (string.IsNullOrEmpty(text)) return false;

            int ip = text.IndexOf("IPHONEACCEV=", StringComparison.OrdinalIgnoreCase);
            if (ip >= 0)
            {
                List<int> na = ExtractInts(text.Substring(ip + "IPHONEACCEV=".Length));
                int level = -1;
                for (int k = 1; k + 1 < na.Count; k += 2)
                {
                    if (na[k] == 1) level = na[k + 1];
                }
                if (level >= 0 && level <= 9)
                {
                    overall = (level + 1) * 10;
                    if (overall > 100) overall = 100;
                    return true;
                }
            }

            int biev = text.IndexOf("BIEV=", StringComparison.OrdinalIgnoreCase);
            if (biev >= 0)
            {
                List<int> nb = ExtractInts(text.Substring(biev + "BIEV=".Length));
                if (nb.Count >= 2 && nb[0] == 2 && nb[1] >= 0 && nb[1] <= 100)
                {
                    overall = nb[1];
                    return true;
                }
            }

            int at = text.IndexOf("UPDATEHUAWEIBATTERY=", StringComparison.OrdinalIgnoreCase);
            if (at >= 0)
            {
                List<int> nums = ExtractInts(text.Substring(at + "UPDATEHUAWEIBATTERY=".Length));
                if (nums.Count >= 3 && nums[0] == (nums.Count - 1) / 2)
                {
                    var comps = new List<int>();
                    for (int i = 1; i + 1 < nums.Count; i += 2)
                    {
                        int id = nums[i], val = nums[i + 1];
                        if ((id == 2 || id == 4 || id == 6) && val >= 0 && val <= 100) comps.Add(val);
                    }
                    if (comps.Count == 3)
                    {
                        left = comps[0];
                        right = comps[1];
                        casePct = comps[2];
                        overall = (left + right) / 2;
                        return true;
                    }
                }
            }
            return false;
        }

        private static List<int> ExtractInts(string s)
        {
            var list = new List<int>();
            if (string.IsNullOrEmpty(s)) return list;
            var tok = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                if (ch == '\r' || ch == '\n')
                {
                    if (list.Count > 0 || tok.Length > 0) break;
                }
                if (ch >= '0' && ch <= '9') tok.Append(ch);
                else if (tok.Length > 0)
                {
                    int v;
                    if (int.TryParse(tok.ToString(), out v)) list.Add(v);
                    tok.Length = 0;
                }
            }
            if (tok.Length > 0)
            {
                int v;
                if (int.TryParse(tok.ToString(), out v)) list.Add(v);
            }
            return list;
        }

        // ---- 地址 / 属性辅助（纯逻辑，便于对照测试） ----------------------

        public static string FormatAddress(ulong address)
        {
            string s = address.ToString("X12");
            if (s.Length < 12) s = s.PadLeft(12, '0');
            if (s.Length > 12) s = s.Substring(s.Length - 12);
            return s.Substring(0, 2) + ":" + s.Substring(2, 2) + ":" + s.Substring(4, 2) + ":" +
                   s.Substring(6, 2) + ":" + s.Substring(8, 2) + ":" + s.Substring(10, 2);
        }

        public static string NormalizeAddressKey(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if ((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))
                    sb.Append(char.ToUpperInvariant(c));
            }
            string hex = sb.ToString();
            if (hex.Length > 12) hex = hex.Substring(hex.Length - 12);
            if (hex.Length < 12) return hex;
            return hex;
        }

        public static ulong ParseAddress(string text)
        {
            string key = NormalizeAddressKey(text);
            if (key.Length != 12) return 0;
            try { return Convert.ToUInt64(key, 16); }
            catch { return 0; }
        }

        public static string AddressTag(ulong address)
        {
            string s = address.ToString("X12");
            if (s.Length < 12) s = s.PadLeft(12, '0');
            if (s.Length > 12) s = s.Substring(s.Length - 12);
            return s.Substring(8, 4);
        }

        public static string StatusText(BatteryQueryStatus status)
        {
            switch (status)
            {
                case BatteryQueryStatus.Querying: return "读取中";
                case BatteryQueryStatus.Fresh: return "";
                case BatteryQueryStatus.Stale: return "历史";
                case BatteryQueryStatus.NoSystemValue: return "系统未提供";
                case BatteryQueryStatus.Unmatched: return "未匹配";
                case BatteryQueryStatus.Timeout: return "超时";
                case BatteryQueryStatus.AccessDenied: return "无权限";
                case BatteryQueryStatus.Failed: return "失败";
                default: return "未上报";
            }
        }

        public static bool IsHostComputer(uint classOfDevice)
        {
            uint major = (classOfDevice >> 8) & 0x1F;
            return major == 0x01; // Computer (desktop, notebook, server)
        }

        public static bool IsAudio(uint classOfDevice)
        {
            return ((classOfDevice >> 8) & 0x1F) == 0x04;
        }

        public static bool IsPeripheral(uint classOfDevice)
        {
            return ((classOfDevice >> 8) & 0x1F) == 0x05;
        }

        public static bool IsKeyboard(uint classOfDevice)
        {
            return (classOfDevice & 0x05C0) == 0x0540;
        }

        public static bool IsMouse(uint classOfDevice)
        {
            return (classOfDevice & 0x05C0) == 0x0580;
        }

        public static bool CategoryLooksAudio(string hint)
        {
            return HintHas(hint, "audio") || HintHas(hint, "headphone") || HintHas(hint, "headset") ||
                   HintHas(hint, "earbud") || HintHas(hint, "speaker") || HintHas(hint, "耳机") || HintHas(hint, "音箱");
        }

        public static bool CategoryLooksInput(string hint)
        {
            return HintHas(hint, "input") || HintHas(hint, "keyboard") || HintHas(hint, "mouse") ||
                   HintHas(hint, "hid") || HintHas(hint, "gaming") || HintHas(hint, "键盘") || HintHas(hint, "鼠标");
        }

        public static bool CategoryLooksKeyboard(string hint)
        {
            return HintHas(hint, "keyboard") || HintHas(hint, "键盘");
        }

        public static bool CategoryLooksMouse(string hint)
        {
            return HintHas(hint, "mouse") || HintHas(hint, "鼠标");
        }

        public static bool NameLooksAudio(string name)
        {
            return CategoryLooksAudio(name) || HintHas(name, "freebuds") || HintHas(name, "airpods") ||
                   HintHas(name, "buds") || HintHas(name, "freelace") || HintHas(name, "freeclip") ||
                   HintHas(name, "wh-") || HintHas(name, "wf-");
        }

        public static bool NameLooksKeyboard(string name)
        {
            return CategoryLooksKeyboard(name) || HintHas(name, "keychron") || HintHas(name, "mx keys") ||
                   HintHas(name, "kbd") || HintHas(name, "m87") || HintHas(name, "k87") ||
                   HintHas(name, "g87") || HintHas(name, "rk87") || HintHas(name, "rk61") ||
                   HintHas(name, "akko") || HintHas(name, "nuphy") || HintHas(name, "lofree") ||
                   HintHas(name, "hhkb") || HintHas(name, "leopold") || HintHas(name, "filco") ||
                   HintHas(name, "vgn") || HintHas(name, "aula");
        }

        public static bool NameLooksMouse(string name)
        {
            return CategoryLooksMouse(name) || HintHas(name, "mx master") || HintHas(name, "mx anywhere") ||
                   HintHas(name, "basilisk") || HintHas(name, "deathadder") || HintHas(name, "viper") ||
                   HintHas(name, "g pro") || HintHas(name, "superlight");
        }

        public static bool NameLooksInput(string name)
        {
            return NameLooksKeyboard(name) || NameLooksMouse(name) || CategoryLooksInput(name) ||
                   HintHas(name, "controller") || HintHas(name, "xbox") || HintHas(name, "dualsense") || HintHas(name, "手柄");
        }

        private static bool HintHas(string hint, string token)
        {
            if (string.IsNullOrEmpty(hint) || string.IsNullOrEmpty(token)) return false;
            return hint.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string NormGuid(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("{", "").Replace("}", "").Trim().ToLowerInvariant();
        }

        private static object PropRaw(IReadOnlyDictionary<string, object> props, string key)
        {
            if (props == null || string.IsNullOrEmpty(key) || !props.ContainsKey(key)) return null;
            return props[key];
        }
        /// <summary>BatteryLife 为 Byte；官方枚举 101=未知状态，不得当作 101%。</summary>
        public static bool TryReadBatteryPercent(IReadOnlyDictionary<string, object> props, out int percent)
        {
            percent = -1;
            object raw = PropRaw(props, PropBatteryLife);
            return TryReadBatteryPercentValue(raw, out percent);
        }

        public static bool TryReadBatteryPercentValue(object raw, out int percent)
        {
            percent = -1;
            if (raw == null) return false;
            try
            {
                int v = Convert.ToInt32(raw);
                if (v >= 0 && v <= 100)
                {
                    percent = v;
                    return true;
                }
                return false;
            }
            catch { return false; }
        }

        private static BatteryQueryStatus ClassifyException(Exception ex)
        {
            if (ex == null) return BatteryQueryStatus.Failed;
            string name = ex.GetType().FullName ?? "";
            string msg = ex.Message ?? "";
            if (name.IndexOf("Unauthorized", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("AccessDenied", StringComparison.OrdinalIgnoreCase) >= 0 ||
                msg.IndexOf("Access is denied", StringComparison.OrdinalIgnoreCase) >= 0 ||
                msg.IndexOf("Unauthorized", StringComparison.OrdinalIgnoreCase) >= 0)
                return BatteryQueryStatus.AccessDenied;
            if (name.IndexOf("Timeout", StringComparison.OrdinalIgnoreCase) >= 0 ||
                msg.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0)
                return BatteryQueryStatus.Timeout;
            if (ex.InnerException != null) return ClassifyException(ex.InnerException);
            return BatteryQueryStatus.Failed;
        }

        private static string BriefError(Exception ex)
        {
            if (ex == null) return "error";
            return ex.GetType().Name;
        }

        private static void LogQuery(BtDevice d, BatteryQueryStatus status, string detail)
        {
            string tag = d == null ? "" : AddressTag(d.Address);
            string kind = "dev";
            if (d != null)
            {
                if (d.IsKeyboard) kind = "keyboard";
                else if (d.IsMouse) kind = "mouse";
                else if (d.IsAudio) kind = "audio";
                else if (d.IsPeripheral) kind = "peripheral";
                else if (d.IsBle) kind = "ble";
                else if (d.IsClassic) kind = "classic";
            }
            LogQuery(kind, tag, status, detail);
        }

        private static readonly object _logLock = new object();

        private static void LogQuery(string kind, string addrTag, BatteryQueryStatus status, string detail)
        {
            string path = QueryLogPath;
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") +
                    " kind=" + kind +
                    " addr=" + (string.IsNullOrEmpty(addrTag) ? "-" : addrTag) +
                    " status=" + status +
                    " detail=" + (detail ?? "") +
                    "\r\n";
                lock (_logLock)
                {
                    System.IO.File.AppendAllText(path, line, Encoding.UTF8);
                }
            }
            catch { }
        }

        // ---- WinRT 等待（反射 AsTask，同时支持 IAsyncOperation<T>、IAsyncOperationWithProgress<T,P> 与 IAsyncAction） ----
        private static MethodInfo _asTaskGeneric;
        private static MethodInfo _asTaskProgress;
        private static MethodInfo _asTaskAction;
        private static bool _asTaskResolved;

        private static void ResolveAsTaskMethods()
        {
            if (_asTaskResolved) return;
            _asTaskResolved = true;
            try
            {
                var asm = Assembly.Load("System.Runtime.WindowsRuntime, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089");
                var extType = asm.GetType("System.WindowsRuntimeSystemExtensions");
                foreach (var m in extType.GetMethods())
                {
                    if (m.Name != "AsTask") continue;
                    var ps = m.GetParameters();
                    if (ps.Length != 1) continue;
                    if (m.IsGenericMethodDefinition && ps[0].ParameterType.Name == "IAsyncOperation`1")
                        _asTaskGeneric = m;
                    else if (m.IsGenericMethodDefinition && ps[0].ParameterType.Name == "IAsyncOperationWithProgress`2")
                        _asTaskProgress = m;
                    else if (!m.IsGenericMethodDefinition && ps[0].ParameterType.Name == "IAsyncAction")
                        _asTaskAction = m;
                }
            }
            catch
            {
                _asTaskGeneric = null;
                _asTaskProgress = null;
                _asTaskAction = null;
            }
        }

        private static object AwaitWith(object op, Type resultType, int timeoutMs)
        {
            if (op == null) return null;
            ResolveAsTaskMethods();
            if (_asTaskGeneric == null) return null;
            var task = (Task)_asTaskGeneric.MakeGenericMethod(resultType).Invoke(null, new object[] { op });
            if (!task.Wait(timeoutMs)) return null;
            if (task.IsFaulted)
            {
                if (task.Exception != null) throw task.Exception.GetBaseException();
                return null;
            }
            if (!task.IsCompleted) return null;
            return task.GetType().GetProperty("Result").GetValue(task, null);
        }

        private static object AwaitWithProgress(object op, Type resultType, Type progressType, int timeoutMs)
        {
            if (op == null) return null;
            ResolveAsTaskMethods();
            if (_asTaskProgress == null) return null;
            var task = (Task)_asTaskProgress.MakeGenericMethod(resultType, progressType).Invoke(null, new object[] { op });
            if (!task.Wait(timeoutMs)) return null;
            if (task.IsFaulted)
            {
                if (task.Exception != null) throw task.Exception.GetBaseException();
                return null;
            }
            if (!task.IsCompleted) return null;
            return task.GetType().GetProperty("Result").GetValue(task, null);
        }

        private static object AwaitActionWith(object action, int timeoutMs)
        {
            if (action == null) return null;
            ResolveAsTaskMethods();
            if (_asTaskAction == null) return null;
            var task = (Task)_asTaskAction.Invoke(null, new object[] { action });
            if (!task.Wait(timeoutMs)) return null;
            if (task.IsFaulted || !task.IsCompleted) return null;
            return BooleanBoxes.True;
        }

        private static class BooleanBoxes
        {
            public static readonly object True = true;
        }
    }
}

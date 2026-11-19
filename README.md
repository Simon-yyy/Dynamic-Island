# 灵动岛 / Liquid Island（Windows 10/11 桌面组件）

Windows 10 / 11 桌面顶部「灵动岛」常驻小组件：收起态为悬浮毛玻璃胶囊，实时显示时钟、日期、本机电池及正在播放的歌曲与动态歌词；悬停或点击后通过临界阻尼弹簧动画展开为 420×500 的液态玻璃控制面板，集成**多设备蓝牙电量中心**、**北京时间日光律动天色**、**四维系统硬件监控**、**WASAPI 28 段实时音律频谱**、**微信通知摘要**与**交互式桌面宠物**。

---

## 目录结构

项目已完成冗余文件与死代码清理，保持纯净的工程结构：

```text
灵动岛/
├─ 一键编译并运行.bat      双击自动关闭旧实例、调用系统自带 .NET 4.x 编译并启动最新程序
├─ build.ps1             PowerShell 零依赖编译脚本（带 UTF-8 BOM，自动定位系统 csc.exe）
├─ install.ps1           绿色便携安装脚本（不写注册表、不写 C 盘、生成安全安装清单）
├─ uninstall.ps1         安全卸载脚本（严格校验 .island_manifest，绝不误删用户文件）
├─ island.ico            多尺寸内嵌程序图标（256/64/48/32/16）
├─ README.md             项目说明与使用文档
├─ 问题排查与解决方案记录.md 历次问题现象、根因分析与最终技术解决方案完整记录
├─ MEMORY.md             项目架构约束与工程经验库（Knowledge Runtime）
├─ src/                  核心 C# 源码（共 17 个模块，严格兼容 .NET Framework 4.x C# 5）
│  ├─ Program.cs         程序入口、Per-Monitor DPI 感知初始化与命令行参数解析
│  ├─ IslandWindow.cs    主窗口视觉树、弹簧动画调度、设备电量中心、媒体控制与面板交互
│  ├─ Bluetooth.cs       零音频干扰蓝牙枚举与多通道电量引擎（SetupAPI + CfgMgr32 + Winsock2 5A）
│  ├─ SolarRhythm.cs     北京时间（UTC+8）24 小时日光律动天色与太阳正弦弧线轨迹算法
│  ├─ GlassStyle.cs      液态玻璃拟态视觉材质、高光边缘与 GlassBar 渐变进度条控件
│  ├─ Spring.cs          高刷自适应临界阻尼物理弹簧（支持 60/120/144/240Hz）
│  ├─ AudioTap.cs        WASAPI Loopback 系统音频回环捕获与 2048 点 FFT 28 段频谱分析
│  ├─ MediaSession.cs    Windows GSMTC 系统媒体会话监听、封面主副色提取与在线 LRC 歌词匹配
│  ├─ MediaTimeline.cs   媒体时间轴外推与暂停/跳转（Seek）精确落锚算法
│  ├─ LyricWatch.cs      桌面歌词窗口 OCR 识别回锚（无系统时间轴播放器兜底）
│  ├─ ScreenClock.cs     播放器底部时间码 OCR 识别回锚
│  ├─ WeChatFeed.cs      Windows 通知库（wpndatabase.db / winsqlite3.dll）微信消息只读解析
│  ├─ Pet.cs             桌面宠物渲染、呼吸/挤压动效、滚轮缩放与透明 PNG 校验
│  ├─ TrayIcon.cs        系统托盘常驻菜单（显示/隐藏、宠物开关、实时北京时间天色阶段、版本核对）
│  ├─ Native.cs          Win32 原生 API 封装（无焦点工具窗 WS_EX_NOACTIVATE、CPU/内存/电源状态）
│  ├─ WinRtEvent.cs      兼容 .NET Framework 4.x csc.exe 的 WinRT 事件反射绑定器
│  └─ BuildInfo.cs       构建版本与时间戳标识（由 build.ps1 自动写入）
├─ winmd/                编译期 WinRT 合约元数据（Windows.WinMD 等）
├─ data/                 运行时本地数据目录（电量缓存 btbattery.txt、日光配置、桌面宠物预设图）
├─ tools/                纯逻辑自动化回归测试脚本（test_battery_logic.py、test_media_timeline.py）
└─ screenshots/          界面实机效果参考截图
```

---

## 快速编译与运行

### 1. 一键编译并运行（推荐）
无需安装 Visual Studio 或额外 .NET SDK，直接利用 Windows 10/11 自带的 `.NET Framework 4.x (v4.0.30319\csc.exe)`：
- 解压后直接双击根目录下的 **`一键编译并运行.bat`**。
- 脚本会自动终止后台旧版 `灵动岛.exe` 进程、编译全部 17 个 C# 源文件并立即启动新生成的 `灵动岛.exe`。

### 2. 绿色安装与卸载（不写注册表 · 不写 C 盘）
```powershell
# 安装到任意非系统盘目录（自动编译最新源码、迁移旧缓存并生成 .island_manifest 清单）
powershell -ExecutionPolicy Bypass -File .\install.ps1 -InstallDir "E:\DynamicIsland"

# 安全卸载（校验清单后仅清理灵动岛自身文件）
powershell -ExecutionPolicy Bypass -File .\uninstall.ps1 -InstallDir "E:\DynamicIsland"
```

---

## 核心特性与技术架构

### 1. 零音频干扰 · 多设备蓝牙电量中心（`src/Bluetooth.cs`）
面板中部提供 4 槽位「设备电量中心」（第 1 槽为本机电源/电池，后 3 槽按音频 > 键盘 > 鼠标 > 其他外设排序展示已连接蓝牙设备；连接超过 3 台设备时点击右上角 `切换 (1/N) ›` 可轮换浏览全部设备）：

- **三层无感非破坏式读取架构**：
  1. **第一层：Windows 内核 PnP 属性表直读（每 3 秒一次，0 无线电通信，耗时 `< 1ms`）**
     通过 `SetupDiGetClassDevsW` 遍历当前在线节点，读取 `DEVPKEY_Device_BatteryLife`（`{104EA319-6EE2-4701-BD47-8DDBF425BBE5}, pid=2`），并结合 `cfgmgr32.dll` 的 `CM_Get_Parent` + `CM_Get_Device_IDW` 向上回溯最多 6 层父节点。这样无论是 `BTHHFENUM\BthHFPAudio`（蓝牙免提音频节点）、`HID\...`（如 `M87-BT1` 机械键盘）还是 `BTHLEDEVICE\...`（BLE GATT 电池服务子节点），都能在纯内存态关联到根蓝牙设备的 12 位 MAC 地址并拿到实时电量，**不向蓝牙设备发送任何无线数据包，对听歌、打字、游戏延迟完全零影响**。
  2. **第二层：华为 FreeBuds Pro 5（HarmonyOS 7.0+）Winsock2 `AF_BTH` RFCOMM 通道 1 分体电量直读**
     针对华为 FreeBuds Pro 3 / 4 / 5 等使用私有 MBB（`0x5A`）协议的耳机，通过原生 `ws2_32.dll` 蓝牙套接字直连 **RFCOMM Channel 1（`port = 1`）** 发送 `0x01 0x08` 电量查询帧并解析 TLV `0x01`（整机）与 `0x02`（左耳 / 右耳 / 充电盒三字节分体电量），在界面展示 `左 / 右` 及 `盒XX%`。
  3. **第三层：会话级免打扰与播放期禁扰保护**
     - 周期枚举中**严禁调用** `BluetoothLEDevice.FromBluetoothAddressAsync` / `EnumerateBlePaired` 等会触发 2.4GHz 天线寻呼的 WinRT 接口，彻底消除背景蓝牙扫描抢占 A2DP 音频时隙导致的听歌微卡顿。
     - 严禁主动连接 `0000111e`（HFP 免提通话服务），防止耳机在立体声 A2DP 与免提通话模式之间切换导致断音。
     - `port = 1` 私有通道仅在耳机刚连上（且当前未在播放音乐）时静默探测 **1 次**并缓存分体电量；听歌、暂停/恢复播放或展开面板时 **100% 走内核内存直读（0 蓝牙发包）**，仅在用户手动点击右上角「读取」按钮时才重新发起主动协议刷新。

### 2. 北京时间（UTC+8）日光律动天色（`src/SolarRhythm.cs`）
- **严格锁定北京时间（UTC+8）**：内部统一通过 `ToUniversalTime().AddHours(8.0)` 换算北京时间，不受机器本地时区偏移影响；展开面板左上角日期旁与托盘右键菜单均实时显示当前天色阶段名称。
- **9 段全天候鲜明色温锚点 + 连续正弦太阳轨迹**：
  - `00:00 ~ 05:00` **静谧子夜**：深空夜蓝（`#0E1326`），星夜微光收敛
  - `05:00 ~ 06:15` **拂晓破晓**：晨雾蓝紫（`#22264A`），东侧微露破晓橙粉（`#FF8C6E`）与晨雾青
  - `06:15 ~ 08:15` **朝阳初升**：晨天蓝金（`#2E4874`），朝阳暖金（`#FFB84D`）与朝霞珊瑚红自右上升起
  - `08:15 ~ 10:45` **朗润上午**：清朗天青蓝（`#255E9A`），通透上午晴空青与暖白日光
  - `10:45 ~ 13:45` **晴空正午**：明亮正午蔚蓝（`#2C6EB4`），天顶澄净天光蓝（`#72DCFF`）与正午暖阳金白光晕（`#FFEA98`）
  - `13:45 ~ 16:45` **午后暖晴**：湛蓝微暖（`#2B5E9C`），太阳西斜并透出温润午后香槟金（`#FFCE68`）
  - `16:45 ~ 18:45` **日落熔金**：晚霞琥珀紫红（`#522B40`），落日熔金橙（`#FF7A38`）与玫瑰粉红（`#FA5486`）
  - `18:45 ~ 20:45` **蓝调薄暮**：经典皇家宝石蓝（`#24235C` + `#4E74FF`）与暮夜幽紫交织
  - `20:45 ~ 24:00` **入夜星沉**：夜色渐深，平滑归于子夜
- 白昼期间（`05:30 ~ 18:30`）主太阳光斑沿正弦弧线自右（东）经过正午天顶向左（西）每分钟连续平滑移动，配合 SmoothStep 余弦插值实现全天无突变过渡。

### 3. 音乐胶囊、无焦点媒体控制与实时歌词（`src/MediaSession.cs` / `src/AudioTap.cs`）
- **无焦点媒体控制（`WS_EX_NOACTIVATE`）**：主窗口设置 `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`，点击「上一首 / 播放暂停 / 下一首」时不会抢夺当前播放器、浏览器或全屏游戏的窗口焦点。
- **双通道歌词同步**：优先订阅 Windows GSMTC 时间轴事件并按流逝时间线性外推；当播放器未上报系统时间轴时，自动降级至桌面歌词 OCR（`LyricWatch.cs`）与窗口底部时间码 OCR（`ScreenClock.cs`）校准。
- **封面流光与 WASAPI 频谱**：自动提取专辑封面主色与副色渲染双色氛围光；通过 WASAPI Loopback 实时计算 2048 点 FFT 驱动 28 段音律频谱。

---

## 自动化自检与排障

- **本地逻辑自检（无需 Windows 环境）**：
  ```bash
  python3 tools/test_battery_logic.py
  python3 tools/test_media_timeline.py
  ```
- **运行日志**：蓝牙电量查询日志保存在 `data\island_query.log`（仅记录设备类别、MAC 后 4 位掩码、查询状态与通道来源，保护隐私）；电量持久化缓存在 `data\btbattery.txt`。

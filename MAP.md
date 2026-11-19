# 项目模块地图 (MAP.md / Agent Navigation)

## 一、 项目架构与工程定位
本项目为 **灵动岛 / Liquid Island（Windows 10 / 11 原生桌面常驻组件）**，基于 `.NET Framework 4.x`（WPF + Win32 P/Invoke + WinRT 合约反射）零外部依赖构建，通过系统自带 `csc.exe` 一键编译为单可执行文件 `灵动岛.exe`，严禁使用 C# 6+ 语法。

---

## 二、 核心入口与脚本矩阵
| 文件 / 入口 | 职责与技术栈 | 使用方式 | 关键约束 / 依赖 |
| :--- | :--- | :--- | :--- |
| **`一键编译并运行.bat`** | 关闭旧实例、调用 `build.ps1` 并启动 `灵动岛.exe` | 双击直接运行 | 自动绕过 PowerShell 执行策略 |
| **`build.ps1`** | 定位系统 `.NET 4.0.30319\csc.exe` 编译 `src/*.cs` | `.\build.ps1` | 必须保留 UTF-8 BOM + CRLF |
| **`install.ps1`** | 绿色便携安装并生成 `.island_installed` / `.island_manifest` | `.\install.ps1 -InstallDir ...` | 不写注册表、不写 C 盘 |
| **`uninstall.ps1`** | 按清单安全卸载 | `.\uninstall.ps1 -InstallDir ...` | 严禁无条件递归删除目录 |
| **`tools/test_battery_logic.py`** | 蓝牙地址、PnP ID、华为 `5A` 帧、电量状态回归测试 | `python3 tools/test_battery_logic.py` | 无需 Windows 环境即可执行 |
| **`tools/test_media_timeline.py`** | 媒体时间轴外推、Seek 与 LRC 索引回归测试 | `python3 tools/test_media_timeline.py` | 无需 Windows 环境即可执行 |

---

## 三、 模块拓扑与职责分工

### 1. 主窗口与视觉交互
- `src/Program.cs`：开启 Per-Monitor DPI 感知、解析 `--shot` 截图模式参数并启动 WPF 应用与桌面宠物。
- `src/IslandWindow.cs`：主窗口视觉树、收起态胶囊/展开态控制面板/蓝牙弹窗切换、鼠标拖拽与顶部状态栏动态贴靠、电量中心 UI 与双源基线隔离缓存调度、子控件 MouseLeftButtonDown 冒泡拦截、音乐卡片与歌词显示。
- `src/GlassStyle.cs`：液态玻璃拟态材质、高光描边与 `GlassBar` 渐变进度条控件。
- `src/Spring.cs`：高刷自适应临界阻尼弹簧（支持 60/120/144/240Hz）。
- `src/SolarRhythm.cs`：北京时间（UTC+8）9 段全天候天色锚点与白昼太阳正弦弧线轨迹计算。
- `src/TrayIcon.cs`：系统托盘图标与右键菜单（显示/隐藏、重置位置、宠物开关、日光律动开关与实时天色阶段、版本号显示与 63 字符安全截断）。
- `src/Native.cs`：Win32 互操作封装（`WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`、`GetTopBarBottomPhysicalPx` 顶部状态栏检测、`SetBodyRegion` 圆角窗口区域与 GDI 句柄安全释放、CPU/内存/本机电源状态）。

### 2. 蓝牙与多设备电量引擎
- `src/Bluetooth.cs`：
  - 经典蓝牙枚举（`bthprops.cpl`，`fIssueInquiry=false`）与 PnP 设备树纯内存枚举（`SetupAPI` + `CfgMgr32` 6 层父节点 MAC 回溯）；
  - 华为 FreeBuds Pro 3/4/5 私有 `0x5A` 协议（Win32 Winsock2 `AF_BTH` 直连 RFCOMM `port=1`，辅以 WinRT 回退）；
  - 标准 BLE GATT `0x180F / 0x2A19` 电量读取（仅限 BLE 设备，自动 `Dispose()` 句柄）；
  - 隐私脱敏查询日志 `data/island_query.log`（1MB 自动轮转）。
- `src/WinRtEvent.cs`：兼容 `.NET Framework 4.x` 的 WinRT 事件反射绑定器。

### 3. 媒体、歌词、音频频谱与桌面扩展
- `src/MediaSession.cs`：Windows GSMTC 会话监听、封面主副色提取与在线 LRC 歌词匹配。
- `src/MediaTimeline.cs`：时间轴播放态线性外推、暂停冻结与 Seek 精确落锚。
- `src/LyricWatch.cs`：桌面歌词窗口截图与系统 OCR 回锚（无时间轴播放器兜底）。
- `src/ScreenClock.cs`：播放器底部时间码 OCR 回锚。
- `src/AudioTap.cs`：WASAPI Loopback 回环音频捕获与 2048 点 FFT 28 段频谱分析。
- `src/WeChatFeed.cs`：只读解析系统通知库（`wpndatabase.db` / `winsqlite3.dll`）与拉起微信窗口。
- `src/Pet.cs`：桌面宠物渲染、呼吸/挤压动效、滚轮缩放与自定义透明 PNG 支持。
- `src/BuildInfo.cs`：由 `build.ps1` 自动生成的构建版本与时间戳标识。

---

## 四、 运行时数据目录（`data/`）
- `data/btbattery.txt`：设备电量与分体电量持久化缓存（格式：`MAC=整机;左;右;盒;时间戳`）。
- `data/island_query.log`：蓝牙电量查询诊断日志（超过 1MB 自动轮转至 `.old`）。
- `data/island_query.log.old`：轮转备份的旧查询日志。
- `data/island_pos.txt`：胶囊自定义位置与贴顶状态持久化配置。
- `data/solar_config.txt`：日光律动天色开关状态持久化配置。
- `data/pet.txt`：桌面宠物预设、自定义图片与缩放比例持久化配置。
- `data/pet/presets/`：内置桌面宠物预设透明 PNG 资源。

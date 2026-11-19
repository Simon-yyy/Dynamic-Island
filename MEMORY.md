# 灵动岛 项目工程经验与约束记忆 (Knowledge Runtime)

### [ID: Bluetooth-ZeroRadioInterferencePolling]
- **角色**: Constraint
- **生效范围**: `src/Bluetooth.cs`, `src/IslandWindow.cs`
- **核心结论**: 后台周期性蓝牙枚举与面板展开（`Expand`）严禁调用 `BluetoothLEDevice.FromBluetoothAddressAsync`、`FromIdAsync`、`DeviceInformation.FindAllAsync` 或主动连接 RFCOMM / HFP（`0000111e`）套接字；周期轮询必须 100% 走 Win32 `SetupAPI`（`SetupDiGetClassDevsW` + `SetupDiGetDevicePropertyW`）纯内核内存直读。主动协议探测（如华为 `port = 1` 的 `5A` 分体电量查询）仅允许在设备首次连接（且当前无音频播放）或用户手动点击「读取」按钮时单次触发。
- **成立证据**: WinRT BLE 接口会触发共享 2.4GHz 天线发起无线电寻呼，周期调用会抢占经典蓝牙 A2DP 音频时隙导致听歌持续微卡顿；而在播放或暂停/恢复瞬间建立或断开 RFCOMM / HFP 连接会触发耳机（如 HUAWEI FreeBuds Pro 5）重协商蓝牙链路，造成 2~4 秒的声音丢失。
- **失效条件**: Windows 蓝牙音频底层完全隔离经典/BLE 无线电调度且耳机固件支持无损并发控制通道后废止。

### [ID: Bluetooth-CfgMgr32ParentTraversal]
- **角色**: Constraint
- **生效范围**: `src/Bluetooth.cs`
- **核心结论**: 读取 Windows PnP 设备树中的蓝牙电量（`DEVPKEY_Device_BatteryLife`）与设备类别时，必须保留 `BTHHFENUM\`、`HID\`、`BTHLEDEVICE\` 等子节点，并通过 `cfgmgr32.dll` 的 `CM_Get_Parent` + `CM_Get_Device_IDW` 向上回溯父节点提取所属 `BTHENUM\DEV_<12HEX>` 或 `BTHLE\DEV_<12HEX>` 的 12 位蓝牙 MAC 地址；同时必须过滤通用驱动描述（如 `"符合蓝牙低能耗 GATT 的 HID 设备"`），禁止子节点通用描述覆盖根设备真实名称。
- **成立证据**: Windows 10/11 下免提音频电量位于 `BTHHFENUM\BthHFPAudio` 子节点，BLE 键盘（如 `M87-BT1`）的电量与键盘类别位于 `BTHLEDEVICE\` / `HID\` 子节点，其自身 `InstanceId` 往往不含蓝牙 MAC 地址，不回溯父节点会导致电量漏读或图标误判。
- **失效条件**: 微软统一在根 `BTHENUM\DEV_` 与 `BTHLE\DEV_` 节点直接聚合所有子服务电量与设备分类属性后废止。

### [ID: Bluetooth-WinRtAsyncWithProgressReflection]
- **角色**: Constraint
- **生效范围**: `src/Bluetooth.cs`
- **核心结论**: 在 `.NET Framework 4.x` 下通过反射调用 `WindowsRuntimeSystemExtensions.AsTask` 等待 WinRT 异步操作时，除匹配 `IAsyncOperation<TResult>`（`IAsyncOperation`1`）和 `IAsyncAction` 外，针对 `StreamSocket` 的 `InputStream.ReadAsync` 和 `OutputStream.WriteAsync` 必须同时反射匹配双泛型参数的 `IAsyncOperationWithProgress<TResult, TProgress>`（`IAsyncOperationWithProgress`2`）。
- **成立证据**: `ReadAsync` 与 `WriteAsync` 返回类型为 `IAsyncOperationWithProgress`2`，若传入单泛型 `AsTask<TResult>` 会直接抛出 `ArgumentException` 导致读写静默失败。
- **失效条件**: 迁移至原生支持 WinRT 异步等待的现代 SDK 或完全改用 Win32 Winsock2 `AF_BTH` 原生套接字后废止。

### [ID: Script-PowerShellUtf8Bom]
- **角色**: Constraint
- **生效范围**: `build.ps1`, `install.ps1`, `uninstall.ps1`
- **核心结论**: 所有包含中文字符或中文标点的 PowerShell 脚本（`.ps1`）必须以带 BOM 的 UTF-8（`EF BB BF`）编码保存，并使用 `CRLF` 换行。
- **成立证据**: Windows 10/11 自带的 Windows PowerShell 5.1 对无 BOM 的 `.ps1` 文件默认按系统活动代码页（GBK/CP936）解析，UTF-8 中文字符尾字节会吞噬后续单引号 `'` 或右花括号 `}`，导致编译脚本报 `Unexpected token` 解析失败。
- **失效条件**: 运行环境全面切换至默认无 BOM UTF-8 的 PowerShell 7+ (`pwsh.exe`) 后废止。

### [ID: Bluetooth-RadioHandleLeak]
- **角色**: Constraint
- **生效范围**: `src/Bluetooth.cs`
- **核心结论**: 使用 Windows 原生蓝牙 API 时，`BluetoothFindFirstRadio` / `BluetoothFindNextRadio` 输出的 `hRadio` 必须显式调用 `CloseHandle`，严禁仅调用 `BluetoothFindRadioClose`（后者仅关闭搜索句柄）；`SetupDiGetClassDevsW` 返回的设备信息集句柄必须在 `finally` 中调用 `SetupDiDestroyDeviceInfoList` 释放。
- **成立证据**: 微软官方文档规定 `phRadio` 句柄归调用者所有，必须调用 `CloseHandle` 释放；高频定时轮询下未释放句柄会导致常驻进程句柄持续泄漏。
- **失效条件**: 彻底移除 `bthprops.cpl` 与 `setupapi.dll` 原生 P/Invoke 后废止。

### [ID: Script-UninstallSafetyBoundary]
- **角色**: Constraint
- **生效范围**: `uninstall.ps1`, `install.ps1`
- **核心结论**: 卸载脚本严禁直接对目标路径执行无条件递归删除，必须校验应用专属标记（`.island_installed`）与文件清单（`.island_manifest`），严禁删除非清单内的外部文件，且严禁接受盘符根目录作为卸载目标。
- **成立证据**: 用户输入错误或选定共享父目录时，无条件递归删除会导致目录内所有无关用户文件被误清空。
- **失效条件**: 引入打包 MSI/MSIX 官方安装程序规范接管生命周期后废止。

### [ID: Window-DpiHitTestAndNoActivate]
- **角色**: Constraint
- **生效范围**: `src/IslandWindow.cs`, `src/Native.cs`
- **核心结论**:
  1. 在启用 Per-Monitor DPI 感知的 WPF 应用中，严禁将 Win32 `GetCursorPos`（物理像素）与 WPF 逻辑尺寸（`_cw`, `_ch`）直接相加做矩形包围盒判断；必须使用 `PointToScreen` 分别计算起点 `(0, 0)` 与对角点 `(_cw, _ch)` 的物理坐标后再进行区间比对。
  2. 常驻顶层灵动岛窗口必须同时设置 `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`，防止点击媒体控制按钮时抢夺当前活动窗口焦点。
- **成立证据**: 150%/200% 缩放下逻辑像素小于物理像素，单点叠加会导致面板右侧及下方大面积内部区域被误判为外部失焦；缺少 `WS_EX_NOACTIVATE` 会导致点击暂停/播放时打断前台应用焦点。
- **失效条件**: 窗口架构脱离 Win32 互操作后废止。

### [ID: Animation-ExpandCenterMargin]
- **角色**: Constraint
- **生效范围**: `src/IslandWindow.cs`
- **核心结论**: 动态变形外壳居中生长时，内部固定尺寸图层（`ExpandedW`）如需相对屏幕绝对居中钉住，其相对外壳的 `Margin.Left` 补偿量必须为 `(_cw - ExpandedW) / 2.0`（符号为负），严禁写为 `(ExpandedW - _cw) / 2.0`。
- **成立证据**: 正负号反转会导致外壳变大时内容反而向反方向横移，造成严重的横向漂移与重叠。
- **失效条件**: 布局容器改为独立分层窗口且外壳与内容解耦独立渲染后废止。

### [ID: UI-SmallFontClearTypeRendering]
- **角色**: Constraint
- **生效范围**: `src/IslandWindow.cs`
- **核心结论**: 在深色毛玻璃背景下的小号文本（`<=13px`），严禁附加 `BlurRadius > 0` 的 `DropShadowEffect`，且必须显式启用 `TextFormattingMode.Display` 与 `TextRenderingMode.ClearType`。
- **成立证据**: 小号中文字符笔画密集，高斯模糊投影会导致笔画黏连发糊；BitmapCache 会默认降级为灰度反走样。
- **失效条件**: 整体迁移至 DirectWrite 现代矢量渲染管道后废止。

### [ID: UI-SolarRhythmBeijingTimeAndVisibility]
- **角色**: Constraint
- **生效范围**: `src/SolarRhythm.cs`, `src/IslandWindow.cs`
- **核心结论**: 日光律动天色必须严格以北京时间（`UTC+8`）为基准计算，包含子夜、破晓、朝阳（06:15~08:15）、上午、正午、午后、日落、蓝调、入夜 9 个明显色温锚点；开启日光律动时必须关闭展开态的纯黑遮罩（`_veil.Opacity = 0`），并将主光斑尺寸（`>=300px`）与正弦轨迹（`Y = -20 ~ -58`）保持在面板可见区域内，同时在大标题文字上配合 `SoftShadow` 保障可读性。
- **成立证据**: 若叠加上层纯黑遮罩 `_veil` 或将底色压暗至 `L <= 18%` 且把光斑置于 `Y = -94` 窗外，白昼正午（如 11:41）面板在视觉上会退化为近纯黑底色，完全丧失昼夜天色律动感。
- **失效条件**: 无。

### [ID: Media-GsmtcTimelineInterpolation]
- **角色**: Constraint
- **生效范围**: `src/MediaSession.cs`, `src/MediaTimeline.cs`
- **核心结论**: Windows GSMTC `GetTimelineProperties()` 返回的 `Position` 是截至 `LastUpdatedTime` 的静态快照，播放态下必须叠加 `Math.Max(0, (now - LastUpdatedTime).TotalSeconds) * rate` 外推实时进度，暂停态下不叠加流逝时间。
- **成立证据**: 播放器广播快照周期通常较长，直接使用静态 `Position` 会导致歌词滞后或被误判为时间轴未更新。
- **失效条件**: 播放器接入专用实时长连接 SDK 后废止。

### [ID: UI-ConcentricRingGeometry]
- **角色**: Constraint
- **生效范围**: `src/IslandWindow.cs`
- **核心结论**: 在 WPF 中使用 `Ellipse` 与 `System.Windows.Shapes.Path` 叠合同心进度环时，严禁为 `Path` 施加非零 Margin；`Path` 必须设为 `HorizontalAlignment=Left, VerticalAlignment=Top, Margin=0` 且其内部绝对几何圆心必须与 `Ellipse` 的布局中心严格一致。
- **成立证据**: `Path` 在 `Stretch.None` 模式下若被施加 Margin，其内部绝对坐标会叠加外边距，导致底环与进度弧产生偏心错位。
- **失效条件**: 进度环重构为自定义单一渲染图元后废止。

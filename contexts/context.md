# 项目工程上下文 (Operational Context)

## 一、 技术栈与运行环境
- **基础运行环境**: 运行于 64 位 Windows 10 / 11 系统；完全依赖系统自带的 `.NET Framework 4.0.30319`，由 `csc.exe` 实现免安装 Visual Studio 或 .NET SDK 的零依赖单执行文件编译。
- **语言与语法基线**: 严格锁定 **C# 5.0** 语法（严禁使用 C# 6+ 语法，如 `$` 字符串插值、`?.` 空条件操作符、`=>` 表达式主体、属性初始值设定等），确保系统自带低版本 `csc.exe` 100% 成功编译。
- **核心框架与子系统**:
  - **视觉与 UI 架构**: WPF（`PresentationFramework`, `PresentationCore`, `WindowsBase`, `System.Xaml`），纯代码无 XAML 运行时构建视觉树。
  - **底层系统互操作**: Win32 原生 P/Invoke（`User32.dll` 窗口消息与分层样式、`Gdi32.dll` 圆角区域、`SetupAPI.dll` / `CfgMgr32.dll` 设备树回溯、`ws2_32.dll` 蓝牙 Winsock2 套接字通信、`Powrprof.dll` 系统电源状态）。
  - **现代化 Windows 合约**: WinRT 合约元数据（`winmd/` 下 `Windows.WinMD` 等），通过反射与 `WinRtEvent.cs` 动态绑定 GSMTC 媒体会话与 BLE GATT，兼容 .NET 4.x 编译环境。
  - **音频回环捕获**: WASAPI Loopback（`AudioTap.cs`）无损系统回环录制，配合 2048 点复数 FFT 驱动 28 段音律柱状频谱。

---

## 二、 核心业务流与规则基线

### 1. 胶囊展开、状态栏自适应避让与拖拽交互
- **形态流转**: 收起态为紧凑悬浮毛玻璃胶囊（展示时钟、电量、当前曲目与动态歌词）；悬停或点击后通过高刷自适应临界阻尼弹簧（`Spring.cs`）展开为 420×500 液态玻璃控制面板。
- **状态栏避让与拖拽磁吸**:
  - `Native.GetTopBarBottomPhysicalPx` 实时探测顶部状态栏（荣耀顶部栏、MyDockFinder、自动隐藏任务栏）下边界，状态栏展开时平滑下移避让，收回时自动贴顶；
  - 按住胶囊左键位移 > 4px 进入拖拽，接近屏幕中线 `±10px` 自动磁吸居中，松手后持久化坐标到 `data/island_pos.txt`。
- **事件拦截与点击冒泡规则**:
  - 主舞台 `_stage.MouseLeftButtonDown` 会调用 `_stage.CaptureMouse()` 支持拖拽；
  - 面板内所有响应点击的子控件（`_bcRead` 读取、`_bcMore` 更多、设备槽位 `stack`、微信卡片 `card`）**必须在 `MouseLeftButtonDown` 中设置 `e.Handled = true`** 拦截冒泡，防止松开鼠标时的 `MouseLeftButtonUp` 被父容器吞掉导致点击失效。

### 2. 蓝牙多设备电量引擎与双源基线隔离
- **三层梯级查询**:
  - 第一层（PnP 内存态）：`SetupAPI` + `CfgMgr32` 向上回溯最多 6 层父节点关联根蓝牙 MAC，0 无线电发包，零音频打扰；
  - 第二层（华为 `5A` 协议直读）：Winsock2 `AF_BTH` 直连 Channel 1 发送 `0x01 0x08` 查询帧并解析 TLV 分体电量；
  - 第三层（BLE GATT 0x180F 回退）：仅限明确具备 BLE 能力的外设（`d.IsBle || !string.IsNullOrEmpty(d.AepId)`），严禁对纯经典设备触发 2.4GHz BLE 寻呼。
- **双源基线隔离规则**:
  - 华为 `5A` 分体采样成功时，记录此时 Windows PnP 的读数作为基线（`_btBattPartsPnpBaseline`）；
  - 每 3 秒 PnP 轮询仅与自身基线对比，杜绝 Windows 驱动长期停留在 `100%` 的粗糙读数冲掉高精度分体电量；
  - 播放音乐期间 0 RFCOMM 建连；仅当音频完全静默满 15 秒且满 180 秒时静默刷新；配合 `_btQueryingAddrs` 单飞锁杜绝并发冲突。

### 3. GSMTC 媒体、在线歌词与 OCR 双通道兜底
- **媒体会话流**: Windows GSMTC 会话监听 -> 封面主副色提取与双色氛围光 -> 在线 LRC 歌词匹配。
- **时间轴与兜底同步**:
  - 正常播放态根据时间戳线性外推；暂停时落锚冻结时间轴；
  - 播放器未上报系统时间轴时，自动降级至桌面歌词 OCR（`LyricWatch.cs`）与播放器底部时间码 OCR（`ScreenClock.cs`）对齐歌词。

### 4. 日光律动天色算法
- **时间基准**: 统一换算为北京时间（UTC+8），不受本机实际时区影响；
- **全天候色调渐变**: 24 小时划分 9 段天色锚点，实时计算正弦日光轨迹与天空氛围光。

---

## 三、 资源治理与稳定性防线
1. **GDI 与 COM 句柄释放**:
   - `Native.SetBodyRegion` 在 `SetWindowRgn == 0` 时显式调用 `DeleteObject(hrgn)` 释放 GDI 句柄；
   - `Bluetooth.TryBleGatt` 在 `finally` 块显式调用 `service.Dispose()` 与 `le.Dispose()`。
2. **文本溢出与日志轮转**:
   - `TrayIcon.cs` 对 `NotifyIcon.Text` 严格截断至 `<= 63` 字符，防止触发 WinForms 64 字符异常导致启动崩溃；
   - `data/island_query.log` 超过 1MB 自动重命名为 `.old` 轮转切片。
3. **生命周期闭环**:
   - 窗口关闭（`OnClosed`）时必须显式停止全部 10 个 `DispatcherTimer` 并解绑 `CompositionTarget.Rendering` 帧回调。

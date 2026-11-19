using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DynamicIsland
{
    public sealed class IslandWindow : Window
    {
        private const double CollapsedW = 198;
        private const double BubbleW = 300;
        private const double CollapsedH = 42;
        private const double ExpandedW = 420;
        private const double ExpandedH = 516;
        private const double CollapsedR = 21;
        /// <summary>蓝牙卡片尺寸：整个胶囊变形成一张宽卡片（对齐荣耀灵动胶囊的比例）。</summary>
        private const double BtCardW = 330;
        private const double BtCardH = 76;
        private const double BtCardR = 28;
        private const double ExpandedR = 34;
        private const double StagePad = 120;

        private Canvas _stage;
        private Canvas _bodyHost;
        private Grid _body;
        private Border _glass;
        private LinearGradientBrush _glassBg;
        private bool _solarEnabled = true;
        private int _lastSolarMinute = -1;
        public bool SolarRhythmEnabled { get { return _solarEnabled; } }
        private Canvas _ambient;
        private TrayIcon _tray;
        private Border _mediaWash;
        private LinearGradientBrush _mediaWashBg;

        // 缩回态的音乐胶囊
        private Grid _musicLayer;
        private Grid _musicWash;
        private RadialGradientBrush _musicWashA, _musicWashB;
        private SolidColorBrush _mpBarBrush;
        private TextBlock _mpTitle, _mpArtist;
        private TextBlock _mpLyric;
        private TranslateTransform _mpLyricX;
        private TranslateTransform _mpTitleTx;
        private Grid _mpTitleClip;
        private Grid _mpLyricClip;
        private double _mpTitleOff, _mpLyricOff;
        private int _mpTitlePhase, _mpTitleHold, _mpLyricPhase, _mpLyricHold;
        private string _mpLyricText = "";
        private DispatcherTimer _passTimer;
        private TextBlock _expTitle, _expArtist;
        private TextBlock _playGlyph, _playLabel;
        private TextBlock _expLyric;
        private Rectangle[] _mpBars;
        private double _musicT;
        private string _mpTitleText = "", _mpArtistText = "";
        private double _auraT;
        private Ellipse _blobA, _blobB, _blobC;
        private Border _rim;
        private Border _sheen;
        private Border _veil;
        private Border _liquid;
        private Border _liquidEdge;
        private Canvas _specularLayer;
        private Rectangle _specular;
        private Grid _collapsedLayer;
        private Canvas _expandedLayer;
        private readonly List<Border> _metricCards = new List<Border>();

        // 展开面板现在是两页：状态 / 宠物。翻页只换页，不动壳体尺寸。
        private Canvas _page1, _page2;
        private int _page;
        private readonly List<Border> _petTiles = new List<Border>();
        private readonly List<Border> _petTileRings = new List<Border>();
        private TextBlock _petHint;
        private Border _tabPill;
        private TextBlock _tabTextA, _tabTextB;
        private Border _tabChip;

        private TextBlock _tClock, _tDate, _tBatteryPct;
        private TextBlock _hClock, _hDate;
        private TextBlock _cpuVal, _memVal, _batVal, _upVal;
        private readonly bool _hasBattery = Native.HasSystemBattery();
        private List<string> _mpChunks = new List<string>();
        private GlassBar _cpuBar, _memBar, _batBar, _upBar;
        private TextBlock _cpuSub, _memSub, _batSub, _upSub;

        private Spring _sw, _sh, _sr, _shover, _sglow;
        private Spring _sbw, _sbh, _sbr;
        private System.Diagnostics.Stopwatch _animClock = System.Diagnostics.Stopwatch.StartNew();
        private bool _pendingShrink;

        /// <summary>展开面板是否打开（读时间码那条后台通道据此让路）。</summary>
        public static bool ExpandedNow { get; private set; }

        private DateTime _hoverInAt = DateTime.MinValue;
        private DateTime _hoverOutAt = DateTime.MinValue;
        private bool _hoverExpanded;
        private double _cw = CollapsedW, _ch = CollapsedH, _cr = CollapsedR;
        private double _hoverT;
        private ScaleTransform _expScale;
        private TranslateTransform _expShift;
        private double _glowT = 1;

        private bool _expanded;
        private bool _pressed;
        private bool _dragging;
        private Point _press;
        private Native.POINT _pressCursorPx;
        private double _dragStartLeft;
        private double _dragStartTop;
        private bool _hasCustomPos;
        private bool _dockedToTop = true;
        private double _customLeft;
        private double _customTop;

        private DispatcherTimer _clockTimer, _statsTimer, _specTimer, _outTimer, _feedTimer, _bubbleTimer;

        // ---- WeChat on island --------------------------------------------
        private Grid _bubbleLayer;
        private TextBlock _bubSender, _bubBody;
        private Border _mediaCard;
        private Rectangle[] _eqRects;
        private DispatcherTimer _eqTimer;
        private TextBlock[] _wxSender = new TextBlock[3];
        private TextBlock[] _wxBody = new TextBlock[3];
        private TextBlock[] _wxTime = new TextBlock[3];
        private TextBlock _wxEmpty;
        private bool _bubbleOn;
        private readonly List<WeChatFeed.Message> _wxMessages = new List<WeChatFeed.Message>();

        private IntPtr _hwnd;
        private HwndSource _source;
        private bool _cursorWasDown;

        // ---- Bluetooth ---------------------------------------------------
        private Grid _btLayer;
        private Border _btHover;
        private Border _btScrim;
        private Grid _btCard;
        private Grid _btContent;
        private Grid _btInner;
        private TextBlock _btIcon, _btName, _btBatt, _btSub;
        private Grid _btRing;
        private System.Windows.Shapes.Path _btRingArc;
        private readonly List<BcSlot> _bcSlots = new List<BcSlot>();
        private TextBlock _bcMore;
        private TextBlock _bcRead;
        private List<BtDevice> _btConnected = new List<BtDevice>();
        private DispatcherTimer _btTimer, _btHideTimer;
        private bool _btCardOn;
        private bool _btPolling;
        private BtDevice _btAudio;
        private BtDevice _btCardDevice;
        private int _bcPageOffset;
        private readonly Dictionary<ulong, bool> _btLastConn = new Dictionary<ulong, bool>();
        private readonly Dictionary<ulong, int> _btBattKnown = new Dictionary<ulong, int>();
        /// <summary>记录电量缓存的采样时间，用于保鲜度过期检测。</summary>
        private readonly Dictionary<ulong, DateTime> _btBattTimestamp = new Dictionary<ulong, DateTime>();
        /// <summary>按设备地址缓存 [左, 右, 盒]（-1 未知）。</summary>
        private readonly Dictionary<ulong, int[]> _btBattParts = new Dictionary<ulong, int[]>();
        /// <summary>上次发起系统电量查询的时间，用于退避重试。</summary>
        private readonly Dictionary<ulong, DateTime> _btBattRetryAt = new Dictionary<ulong, DateTime>();
        private readonly Dictionary<ulong, int> _btBattFailCount = new Dictionary<ulong, int>();
        private readonly Dictionary<ulong, BatteryQueryStatus> _btBattStatus = new Dictionary<ulong, BatteryQueryStatus>();
        private readonly Dictionary<ulong, bool> _btDeepProbed = new Dictionary<ulong, bool>();
        private readonly Dictionary<ulong, DateTime> _btLastActiveProbeAt = new Dictionary<ulong, DateTime>();
        private int _btBattInFlight;
        private const double BatteryFreshMinutes = 15.0;
        private const double ActivePollSeconds = 120.0;
        private const double ExpandPollSeconds = 20.0;
        private bool _btFirstSweep = true;   // 首次扫描只记基线，开机不弹卡片
        private bool _btPendingShrink;

        private int _lastCpu = -1;
        private CultureInfo _zh = CultureInfo.GetCultureInfo("zh-CN");
        private readonly bool _shotMode;
        private readonly string _shotPath;

        public IslandWindow() : this(null) { }

        public IslandWindow(string shotPath)
        {
            _shotPath = shotPath;
            _shotMode = !string.IsNullOrEmpty(shotPath);
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;   // 只在托盘「隐藏的图标」里出现，不占任务栏
            Topmost = true;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            Title = "Liquid Island";
            Width = ExpandedW + StagePad;
            Height = ExpandedH + StagePad;

            BuildTree();
            BuildSprings();
            _tray = new TrayIcon(this);

            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clockTimer.Tick += delegate { TickClock(); };
            _clockTimer.Start();

            _statsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _statsTimer.Tick += delegate { TickStats(); };
            _statsTimer.Start();

            _specTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(9) };
            _specTimer.Tick += delegate { SweepSpecular(); };

            _outTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
            _outTimer.Tick += delegate { WatchOutsideClick(); };
            _outTimer.Start();

            _feedTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
            _feedTimer.Tick += delegate { TickFeed(); };

            _bubbleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(4500) };
            _bubbleTimer.Tick += delegate { _bubbleTimer.Stop(); HideBubble(); };

            // 光标不在实体上时整窗点击穿透（跨进程有效），贴边时不再挡住下层窗口
            _passTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
            _passTimer.Tick += delegate { UpdateClickThrough(); };
            _passTimer.Start();

            _eqTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            _eqTimer.Tick += delegate
            {
                if (_frameHooked) return;   // 展开/收起正在跑逐帧动画时让路，避免抢帧抖动
                TickEqualizer(); TickMusicAura(); TickMusicPill(); SyncExpandedSong();
            };

            // 蓝牙：1.5 秒扫一次连接状态与 PnP 节点电量（SetupAPI 毫秒级完成，不触发无线扫描）
            _btTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(3000) };
            _btTimer.Tick += delegate { TickBluetooth(); };

            _btHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2600) };
            _btHideTimer.Tick += delegate { _btHideTimer.Stop(); HideBtCard(); };

            SourceInitialized += delegate
            {
                _hwnd = new WindowInteropHelper(this).Handle;
                Native.MakeToolWindow(_hwnd);
                _source = HwndSource.FromHwnd(_hwnd);
            };

            Loaded += delegate
            {
                if (_shotMode) { try { System.IO.File.AppendAllText(_shotPath + ".log", "loaded\n"); } catch { } }
                LoadPositionConfig();
                PlaceCollapsed();
                ApplyGeometry();
                LoadSolarConfig();
                _lastSolarMinute = DateTime.Now.Minute;
                ApplySolarRhythm(DateTime.Now, smooth: false);
                TickClock();
                TickStats();
                _specTimer.Start();
                AudioTap.Start();
            MediaSession.Start();
                try
                {
                    string dir = DataDir;
                    if (dir != null)
                        Bluetooth.QueryLogPath = System.IO.Path.Combine(dir, "island_query.log");
                }
                catch { }
                _eqTimer.Start();
                TickBluetooth();
                _btTimer.Start();
                if (_shotMode) { RunShotSequence(); return; }

                // 自检用：设 ISLAND_PREVIEW=目录 时，离屏把收起态/展开态各渲染一张 PNG 后退出
                string previewDir = Environment.GetEnvironmentVariable("ISLAND_PREVIEW");
                if (!string.IsNullOrEmpty(previewDir))
                {
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        try { RenderPreview(previewDir); } catch (Exception ex) { Console.WriteLine(ex); }
                        Application.Current.Shutdown();
                    }), DispatcherPriority.Loaded);
                    return;
                }

                // 验收用：ISLAND_ANIMDEMO=1 开机自动「展开 -> 停 1.2s -> 收起」，循环跑。
                // 录屏/逐帧核对动画时序时用，不依赖鼠标悬停（悬停会被 220ms 判定和真实消息干扰）。
                if (Environment.GetEnvironmentVariable("ISLAND_ANIMDEMO") != null)
                {
                    var demoTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2600) };
                    demoTimer.Tick += delegate
                    {
                        if (_expanded) Collapse(); else Expand();
                    };
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        _outTimer.Stop();
                        Expand();
                        demoTimer.Start();
                    }), DispatcherPriority.Background);
                }

                // 验收用：设 ISLAND_PANELDEMO 时开机直接展开并停住（不看鼠标，方便截图核对版式）
                if (Environment.GetEnvironmentVariable("ISLAND_PANELDEMO") != null)
                {
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        _outTimer.Stop();
                        Expand();
                    }), DispatcherPriority.Background);
                }


                if (Environment.GetEnvironmentVariable("ISLAND_BTDEMO") != null)
                {
                    // 延后 1.2 秒再弹，这样逐帧录制能拍到「从胶囊长成卡片」的全过程
                    var demoDelay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
                    demoDelay.Tick += delegate
                    {
                        demoDelay.Stop();
                        BtDevice d = _btAudio;
                        if (d == null) d = new BtDevice { Name = "HONOR Earbuds X9", ClassOfDevice = 0x240404, Battery = 100 };
                        ShowBtCard(d);   // 演示：走正常的 2.6 秒自动收回
                    };
                    demoDelay.Start();
                }
                Dispatcher.BeginInvoke(new Action(SweepSpecular), DispatcherPriority.Loaded);
            };

            PreviewKeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.Key == Key.Escape && _expanded) { Collapse(); e.Handled = true; }
            };
        }

        // ------------------------------------------------------------------
        //  Visual tree
        // ------------------------------------------------------------------
        private void BuildTree()
        {
            _stage = new Canvas { Width = ExpandedW + StagePad, Height = ExpandedH + StagePad, ClipToBounds = false };
            Content = _stage;

            // Outer drop shadow that grows with the body.
            _bodyHost = new Canvas { IsHitTestVisible = true };
            // 不要外阴影：窗口形状现在被 SetWindowRgn 收在岛体上，阴影投到外面会被硬切一刀，
            // 边缘看起来就是一圈脏毛边。要层次感靠内描边（_rim/_sheen）就够。

            _body = new Grid { Width = CollapsedW, Height = CollapsedH, ClipToBounds = true };
            // 不给 _body 套几何剪切：WPF 的 Visual.Clip 是硬边不带抗锯齿，
            // 会把 _glass 圆角的抗锯齿边切成一格一格的毛边。圆角交给 _glass/_rim 自己画。
            _bodyHost.Children.Add(_body);
            _stage.Children.Add(_bodyHost);

            _ambient = new Canvas { IsHitTestVisible = false, Opacity = 0.96 };
            _blobA = MakeBlob(Color.FromArgb(185, 0x64, 0xD2, 0xFF), 340);
            _blobB = MakeBlob(Color.FromArgb(155, 0xFF, 0xE2, 0x84), 300);
            _blobC = MakeBlob(Color.FromArgb(135, 0x48, 0xBE, 0xFF), 320);
            Canvas.SetLeft(_blobA, -70); Canvas.SetTop(_blobA, -86);
            Canvas.SetLeft(_blobB, 168); Canvas.SetTop(_blobB, 26);
            Canvas.SetLeft(_blobC, -30); Canvas.SetTop(_blobC, 176);
            _ambient.Children.Add(_blobA);
            _ambient.Children.Add(_blobB);
            _ambient.Children.Add(_blobC);
            // 氛围光的光斑会画到岛体外面（负坐标 + BlurEffect），必须裁到圆角内，
            // 否则圆角外侧会挂一圈淡蓝光晕，看着就是脏毛边。
            // 注意往里缩 3px：剪切边是硬边，贴着岛体裁会在边缘留下一道亮线。
            _ambient.Clip = new RectangleGeometry(new Rect(3, 3, CollapsedW - 6, CollapsedH - 6), CollapsedR - 3, CollapsedR - 3);

            // 1. 底层：实色深黑蓝底板，隔绝桌面杂色
            _glassBg = GlassStyle.BodyBrush();
            _glass = new Border { Background = _glassBg, CornerRadius = new CornerRadius(CollapsedR) };
            _body.Children.Add(_glass);

            // 2. 中层：太阳天色与光斑浮在底板之上，柔和折射发光
            _body.Children.Add(_ambient);
            // 展开态可读性保护：胶囊可以很透（玻璃感），但面板一展开、背后文字会糊上来。
            // 这块「面纱」随面板展开慢慢加上去，收起时消失。
            _veil = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(255, 0x0A, 0x0E, 0x18)),
                CornerRadius = new CornerRadius(CollapsedR),
                Opacity = 0,
                IsHitTestVisible = false
            };
            _body.Children.Add(_veil);


            _rim = new Border
            {
                CornerRadius = new CornerRadius(CollapsedR),
                BorderThickness = new Thickness(1),
                BorderBrush = GlassStyle.LiquidEdgeBrush(),
                IsHitTestVisible = false
            };
            _body.Children.Add(_rim);

            _sheen = new Border
            {
                Margin = new Thickness(1.5),
                CornerRadius = new CornerRadius(CollapsedR),
                BorderThickness = new Thickness(1.6, 1.6, 1.6, 0),
                IsHitTestVisible = false,
                BorderBrush = new LinearGradientBrush(
                    new GradientStopCollection
                    {
                        new GradientStop(Color.FromArgb(236, 255, 255, 255), 0),
                        new GradientStop(Color.FromArgb(56, 255, 255, 255), 0.40),
                        new GradientStop(Color.FromArgb(0, 255, 255, 255), 1)
                    }, new Point(0.06, 0), new Point(0.62, 1))
            };
            _body.Children.Add(_sheen);

            _specularLayer = new Canvas { IsHitTestVisible = false, ClipToBounds = true };
            _specular = new Rectangle
            {
                Width = 150,
                Height = 420,
                Opacity = 0,
                IsHitTestVisible = false,
                RenderTransform = new TransformGroup
                {
                    Children = { new SkewTransform(-15, 0), new TranslateTransform(0, 0) }
                },
                Fill = new LinearGradientBrush(
                    new GradientStopCollection
                    {
                        new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.00),
                        new GradientStop(Color.FromArgb(16, 255, 255, 255), 0.40),
                        new GradientStop(Color.FromArgb(48, 255, 255, 255), 0.50),
                        new GradientStop(Color.FromArgb(16, 255, 255, 255), 0.60),
                        new GradientStop(Color.FromArgb(0, 255, 255, 255), 1.00)
                    }, new Point(0, 0), new Point(1, 0))
            };
            _specularLayer.Children.Add(_specular);
            _body.Children.Add(_specularLayer);

            _collapsedLayer = BuildCollapsed();
            _collapsedLayer.Width = CollapsedW;
            _collapsedLayer.Height = CollapsedH;
            _collapsedLayer.HorizontalAlignment = HorizontalAlignment.Center;
            // 蓝牙卡片会把岛体临时拉高，这里必须钉在顶部，否则时钟会跟着往下漂
            _collapsedLayer.VerticalAlignment = VerticalAlignment.Top;
            _body.Children.Add(_collapsedLayer);

            // 缩回态的音乐胶囊：比时钟层更靠上，有歌在放时淡入盖住它
            _musicLayer = BuildMusicPill();
            _musicLayer.VerticalAlignment = VerticalAlignment.Top;
            _musicLayer.HorizontalAlignment = HorizontalAlignment.Stretch;
            _body.Children.Add(_musicLayer);

            // 蓝牙连接卡片：贴在岛体下方，默认透明，连上设备时滑出
            _btLayer = BuildBtCard();
            _btLayer.Opacity = 0;
            _btLayer.IsHitTestVisible = false;
            // 跟着岛体一起长，不能固定宽高（固定就必然和壳体错位）
            _btLayer.HorizontalAlignment = HorizontalAlignment.Stretch;
            _btLayer.VerticalAlignment = VerticalAlignment.Stretch;
            _btLayer.Margin = new Thickness(0);
            _body.Children.Add(_btLayer);

            _bubbleLayer = BuildBubble();
            _bubbleLayer.Opacity = 0;
            _bubbleLayer.IsHitTestVisible = false;
            _body.Children.Add(_bubbleLayer);

            _expandedLayer = BuildExpanded();
            _expandedLayer.Opacity = 0;
            _expandedLayer.HorizontalAlignment = HorizontalAlignment.Center;
            _expandedLayer.VerticalAlignment = VerticalAlignment.Top;
            _expandedLayer.IsHitTestVisible = false;
            _body.Children.Add(_expandedLayer);
            // 液态玻璃：主光层叠在内容之上（顶部亮带 / 中段透明 / 底部回光），
            // 再加一道只在上沿的细高光，形成“玻璃边缘被光照到”的观感。
            _liquid = new Border
            {
                CornerRadius = new CornerRadius(CollapsedR),
                Background = GlassStyle.LiquidBrush(),
                IsHitTestVisible = false
            };
            _body.Children.Add(_liquid);

            _liquidEdge = new Border
            {
                CornerRadius = new CornerRadius(CollapsedR),
                BorderThickness = new Thickness(1, 1, 1, 0),
                BorderBrush = GlassStyle.LiquidEdgeBrush(),
                IsHitTestVisible = false
            };
            _body.Children.Add(_liquidEdge);

            // 玻璃亮边和顶部高光必须压在「所有内容」之上。
            // 不这么做的话，蓝牙卡那张暗底会盖在亮边上面——卡片自己变成一块没有边的暗板，
            // 亮边只在它外侧露一圈，看着就是「一张卡贴在胶囊上」，也就是那个图层分离。
            _body.Children.Remove(_rim);
            _body.Children.Add(_rim);
            _body.Children.Remove(_sheen);
            _body.Children.Add(_sheen);


            _stage.MouseLeftButtonDown += OnDown;
            _stage.MouseMove += OnMove;
            _stage.MouseLeftButtonUp += OnUp;
        }

        private Grid BuildBtCard()
        {
            // 外层跟着岛体一起长（不写死 330×76）。写死的话，壳体还在 198→330 的过程中，
            // 卡片已经是 330 宽、只能在中间裁一条出来，壳体的圆角和卡片的圆角对不上，
            // 边上就会露出「一圈亮边 + 里面一张暗卡」的缝 —— 这就是那个图层分离。
            var g = new Grid { IsHitTestVisible = false, Cursor = Cursors.Hand, ClipToBounds = true };
            g.RenderTransform = new TranslateTransform(0, 0);
            _btCard = g;

            // 内层是固定的 330×76 版式：文字/电量环位置永远不动，只有外框在长，
            // 长得不够大时被外框裁掉两边 —— 像手机那样「从胶囊里推出来」。
            var card = new Grid
            {
                IsHitTestVisible = false
            };
            _btContent = card;
            g.Children.Add(card);

            // 悬停高亮：整块卡片是入口，点开系统的蓝牙设置
            _btHover = new Border
            {
                CornerRadius = new CornerRadius(BtCardR),
                Background = new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)),
                Opacity = 0
            };
            card.Children.Add(_btHover);

            // 右下角的绿色氛围光（和手机那张卡一样，光在电量环后面）
            var glow = new Ellipse
            {
                Width = 200,
                Height = 200,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 60, -40, -130),
                IsHitTestVisible = false,
                Fill = new RadialGradientBrush(new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(88, 0x2B, 0xD9, 0x6B), 0.0),
                    new GradientStop(Color.FromArgb(34, 0x2B, 0xD9, 0x6B), 0.5),
                    new GradientStop(Color.FromArgb(0, 0x2B, 0xD9, 0x6B), 1.0)
                })
            };
            card.Children.Add(glow);

            // 压一层深蓝玻璃底。必须是圆角，否则方形的暗底会把四个圆角啃掉，
            // 边上就出现「胶囊 + 方卡」两层皮。
            // 透明度是关键：太透的话桌面上的图标、文件夹日期会从卡片里透出来，
            // 用户看到的就是「一张半透明的卡贴在上面」＝图层分离；太实又丢了玻璃感。
            // 这里用上浅下深的深蓝渐变 + 0.82 左右的不透明度，背后只留一层很淡的轮廓。
            var scrim = new Border
            {
                CornerRadius = new CornerRadius(BtCardR),
                Background = new LinearGradientBrush(
                    new GradientStopCollection
                    {
                        new GradientStop(Color.FromArgb(244, 0x24, 0x2E, 0x46), 0.0),
                        new GradientStop(Color.FromArgb(249, 0x14, 0x1B, 0x2C), 0.55),
                        new GradientStop(Color.FromArgb(251, 0x09, 0x0D, 0x18), 1.0)
                    }, new Point(0.5, 0), new Point(0.5, 1)),
                IsHitTestVisible = false
            };
            _btScrim = scrim;
            card.Children.Add(scrim);

            // 内容用固定版式宽度：外框在长的时候只裁不加挤，文字/电量环不会重新排版
            var content = new Grid
            {
                Width = BtCardW - 46,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _btInner = content;
            card.Children.Add(content);

            // 左边：白色耳机图标
            _btIcon = new TextBlock
            {
                Text = "\uE7F6",
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 23,
                Foreground = new SolidColorBrush(Color.FromRgb(0xF2, 0xF6, 0xFF)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 14, 0)
            };
            content.Children.Add(_btIcon);

            // 中间：小字「已连接」+ 设备名
            var col = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            _btSub = new TextBlock
            {
                Text = "已连接",
                FontSize = 9.5,
                Foreground = new SolidColorBrush(Color.FromArgb(165, 0xAE, 0xC2, 0xD8))
            };
            _btName = new TextBlock
            {
                Text = "",
                FontSize = 13.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xF4, 0xF8, 0xFF)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 1, 10, 0)
            };
            col.Children.Add(_btSub);
            col.Children.Add(_btName);
            Grid.SetColumn(col, 1);
            content.Children.Add(col);

            // 右边：电量环（绿色圆弧 + 百分比）
            var ring = new Grid { Width = 48, Height = 48, VerticalAlignment = VerticalAlignment.Center };
            var ringBase = new Ellipse
            {
                Stroke = new SolidColorBrush(Color.FromArgb(80, 0xFF, 0xFF, 0xFF)),
                StrokeThickness = 3
            };
            _btRingArc = new System.Windows.Shapes.Path
            {
                Stroke = new SolidColorBrush(Color.FromRgb(0x2B, 0xD9, 0x6B)),
                StrokeThickness = 3,
                StrokeLineJoin = PenLineJoin.Round,
                IsHitTestVisible = false
            };
            _btBatt = new TextBlock
            {
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x2B, 0xD9, 0x6B)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            ring.Children.Add(ringBase);
            ring.Children.Add(_btRingArc);
            ring.Children.Add(_btBatt);
            // 连接时电量环做一个「弹一下」的入场：从 0.72 弹到 1，和系统那种耳机弹窗一个节奏
            ring.RenderTransformOrigin = new Point(0.5, 0.5);
            ring.RenderTransform = new ScaleTransform(1, 1);
            _btRing = ring;
            Grid.SetColumn(ring, 2);
            content.Children.Add(ring);

            g.MouseEnter += delegate { Fade(_btHover, 1, 150); };
            g.MouseLeave += delegate { Fade(_btHover, 0, 180); };
            // 卡片自己吃掉按下/抬起，否则会冒泡到岛体被当成「点岛」而展开
            g.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; };
            g.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                OpenBluetoothSettings();
                HideBtCard();
            };

            return g;
        }
        private Grid BuildBubble()
        {
            var g = new Grid { Margin = new Thickness(3, 0, 4, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var dot = new Ellipse
            {
                Width = 7,
                Height = 7,
                Fill = new SolidColorBrush(Color.FromRgb(0x07, 0xC1, 0x60)),
                VerticalAlignment = VerticalAlignment.Center
            };
            var dotGlow = new DropShadowEffect { Color = Color.FromRgb(0x07, 0xC1, 0x60), BlurRadius = 8, ShadowDepth = 0, Opacity = 0.9 };
            dot.Effect = dotGlow;
            var dotWrap = new Grid { Width = 16, VerticalAlignment = VerticalAlignment.Center };
            dotWrap.Children.Add(dot);
            Grid.SetColumn(dotWrap, 0);
            g.Children.Add(dotWrap);

            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _bubSender = new TextBlock
            {
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xF2, 0xF6, 0xFF)),
                VerticalAlignment = VerticalAlignment.Center
            };
            _bubBody = new TextBlock
            {
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromArgb(190, 0xA8, 0xBA, 0xDC)),
                Margin = new Thickness(7, 1, 0, 0),
                MaxWidth = BubbleW - 92,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            sp.Children.Add(_bubSender);
            sp.Children.Add(_bubBody);
            Grid.SetColumn(sp, 1);
            g.Children.Add(sp);

            return g;
        }

        private static Ellipse MakeBlob(Color color, double size)
        {
            // 用径向渐变模拟柔光，代替 BlurEffect(64)：缩放手势时不再逐帧重算模糊，
            // 这是掉帧的最大来源。
            var brush = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.5, 0.5),
                Center = new Point(0.5, 0.5),
                RadiusX = 0.5,
                RadiusY = 0.5
            };
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(color.A, color.R, color.G, color.B), 0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(color.A * 0.55), color.R, color.G, color.B), 0.42));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(color.A * 0.16), color.R, color.G, color.B), 0.72));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1));

            return new Ellipse
            {
                Width = size,
                Height = size,
                Fill = brush,
                IsHitTestVisible = false
            };
        }
        private Grid BuildCollapsed()
        {
            // 结构：中间「时间在上、日期星期在下」，水平居中。
            // 左端原来那颗装饰蓝点已经去掉——它压在圆角边上，看着像脏点/漏光。
            var g = new Grid { Margin = new Thickness(14, 0, 14, 0) };

            var mid = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            _tClock = new TextBlock
            {
                Text = "16:37",
                FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"),
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(GlassStyle.Ink),
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                LineHeight = 20
            };
            _tDate = new TextBlock
            {
                Text = "周一 11月16日",
                FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
                FontSize = 9,
                Foreground = new SolidColorBrush(GlassStyle.InkDim),
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 0),
                LineHeight = 11
            };
            mid.Children.Add(_tClock);
            mid.Children.Add(_tDate);
            g.Children.Add(mid);

            _tBatteryPct = new TextBlock
            {
                Text = "100%",
                MinWidth = 32,
                TextAlignment = TextAlignment.Right,
                Margin = new Thickness(0, 0, 10, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                FontSize = 10,
                FontWeight = FontWeights.Medium,
                Foreground = new SolidColorBrush(Color.FromArgb(200, 0xCC, 0xDA, 0xF6)),
                VerticalAlignment = VerticalAlignment.Center
            };
            g.Children.Add(_tBatteryPct);

            return g;
        }

        private Canvas BuildExpanded()
        {
            var canvas = new Canvas { Width = ExpandedW, Height = ExpandedH, IsHitTestVisible = true };

            // ---- hero clock ------------------------------------------------
            _hClock = new TextBlock
            {
                Text = "10:24",
                FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"),
                FontSize = 56,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFF)),
                LineHeight = 60
            };
            _hClock.Effect = new DropShadowEffect
            {
                Color = Color.FromRgb(0x88, 0xAE, 0xFF),
                BlurRadius = 26, ShadowDepth = 0, Opacity = 0.42
            };
            Canvas.SetLeft(_hClock, 28);
            Canvas.SetTop(_hClock, 26);
            canvas.Children.Add(_hClock);

            _hDate = new TextBlock
            {
                Text = "周一，11 月 10 日",
                FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromArgb(216, 0xB4, 0xC6, 0xE6))
            };
            SoftShadow(_hDate, 0.45);
            Canvas.SetLeft(_hDate, 31);
            Canvas.SetTop(_hDate, 92);
            canvas.Children.Add(_hDate);

            // ---- 翻页标签（右上角，原来「运行正常」那一块） -------------------
            // 面板是 420×500 的一整块，四张统计卡 + 设备卡 + 媒体卡已经排满，
            // 没有空间再塞第五张卡，所以「宠物」做成第二页，用这块两段式标签切页。
            var chip = GlassStyle.Tile(16, 32);
            chip.Width = 116;
            chip.Height = 32;

            var tabGrid = new Grid();
            tabGrid.ColumnDefinitions.Add(new ColumnDefinition());
            tabGrid.ColumnDefinitions.Add(new ColumnDefinition());

            _tabTextA = new TextBlock
            {
                Text = "状态",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 0xEC, 0xF4, 0xFF)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.SemiBold
            };
            _tabTextB = new TextBlock
            {
                Text = "宠物",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(170, 0xC2, 0xD4, 0xEE)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            SoftShadow(_tabTextA, 0.4);
            SoftShadow(_tabTextB, 0.35);
            Grid.SetColumn(_tabTextA, 0);
            Grid.SetColumn(_tabTextB, 1);
            tabGrid.Children.Add(_tabTextA);
            tabGrid.Children.Add(_tabTextB);

            var tabOverlay = new Canvas { IsHitTestVisible = false };
            _tabPill = new Border
            {
                Width = 54,
                Height = 24,
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(Color.FromArgb(132, 0x74, 0xAC, 0xFF)),
                IsHitTestVisible = false,
                RenderTransform = new TranslateTransform(0, 0)
            };
            Canvas.SetLeft(_tabPill, 4);
            Canvas.SetTop(_tabPill, 4);
            tabOverlay.Children.Add(_tabPill);

            var tabHost = new Grid();
            tabHost.Children.Add(tabGrid);
            tabHost.Children.Add(tabOverlay);
            chip.Child = tabHost;
            chip.Cursor = Cursors.Hand;
            chip.Focusable = true;
            chip.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e)
            {
                double x = e.GetPosition(chip).X;
                SwitchPage(x < chip.Width / 2 ? 0 : 1);
                e.Handled = true;
            };
            chip.KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.Key == Key.Enter || e.Key == Key.Space)
                {
                    SwitchPage(_page == 0 ? 1 : 0);
                    e.Handled = true;
                }
                else if (e.Key == Key.Left)
                {
                    SwitchPage(0);
                    e.Handled = true;
                }
                else if (e.Key == Key.Right)
                {
                    SwitchPage(1);
                    e.Handled = true;
                }
            };
            chip.GotFocus += delegate
            {
                chip.BorderBrush = new SolidColorBrush(Color.FromArgb(180, 0x74, 0xA6, 0xFF));
            };
            chip.LostFocus += delegate
            {
                chip.BorderBrush = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255));
            };
            Canvas.SetLeft(chip, ExpandedW - 28 - 116);
            Canvas.SetTop(chip, 33);
            _tabChip = chip;   // 标签挂在两页之上，不然翻到宠物页就回不去了

            // ---- separator -------------------------------------------------
            var sep = new Border { Width = ExpandedW - 56, Height = 1, Background = GlassStyle.DividerBrush() };
            Canvas.SetLeft(sep, 28);
            Canvas.SetTop(sep, 122);
            canvas.Children.Add(sep);

            // ---- metric cards ---------------------------------------------
            double cardW = (ExpandedW - 56 - 12) / 2.0;
            double cardH = 77;
            double top1 = 135;

            _metricCards.Clear();
            canvas.Children.Add(BuildMetricCard(28, top1, cardW, cardH, "CPU 使用率", "CPU", out _cpuVal, out _cpuSub, out _cpuBar, Color.FromRgb(0x6E, 0xA8, 0xFF)));
            canvas.Children.Add(BuildMetricCard(28 + cardW + 12, top1, cardW, cardH, "内存占用", "MEM", out _memVal, out _memSub, out _memBar, Color.FromRgb(0x66, 0xE6, 0xC8)));
            canvas.Children.Add(BuildMetricCard(28, top1 + cardH + 10, cardW, cardH, "电池电量", "BAT", out _batVal, out _batSub, out _batBar, Color.FromRgb(0x9F, 0x8A, 0xFF)));
            canvas.Children.Add(BuildMetricCard(28 + cardW + 12, top1 + cardH + 10, cardW, cardH, "运行时长", "UP", out _upVal, out _upSub, out _upBar, Color.FromRgb(0xFF, 0xB4, 0x6E)));

            // ---- 设备电量中心（替代原来的关机/睡眠/退出） ----------------------
            canvas.Children.Add(BuildBatteryCenter(28, 302, ExpandedW - 56, 86));

            // ---- media card (fills the bottom gap) -------------------------
            _mediaWash = new Border
            {
                Width = ExpandedW - 56,
                Height = 102,
                CornerRadius = new CornerRadius(18),
                IsHitTestVisible = false,
                Opacity = 0,
                Background = new LinearGradientBrush(
                    new GradientStopCollection
                    {
                        new GradientStop(Color.FromArgb(0, 0x2B, 0xD9, 0x6B), 0.0),
                        new GradientStop(Color.FromArgb(90, 0x2B, 0xD9, 0x6B), 0.72),
                        new GradientStop(Color.FromArgb(130, 0x2B, 0xD9, 0x6B), 1.0)
                    }, new Point(0.5, 0.0), new Point(0.5, 1.0))
            };
            Canvas.SetLeft(_mediaWash, 28);
            Canvas.SetTop(_mediaWash, 398);
            canvas.Children.Add(_mediaWash);

            _mediaCard = BuildMediaCard(28, 398, ExpandedW - 56, 102);
            canvas.Children.Add(_mediaCard);

            // ---- 装成两页 ---------------------------------------------------
            _page1 = canvas;
            _page2 = BuildPetPage();
            _page2.Visibility = Visibility.Collapsed;

            var shell = new Canvas { Width = ExpandedW, Height = ExpandedH, IsHitTestVisible = true };
            shell.Children.Add(_page1);
            shell.Children.Add(_page2);
            if (_tabChip != null) shell.Children.Add(_tabChip);   // 最上层，两页都看得见、点得到
            return shell;
        }

        private Border BuildMediaCard(double x, double y, double w, double h)
        {
            var card = GlassStyle.Tile(18, 26);
            card.Width = w;
            card.Height = h;
            Canvas.SetLeft(card, x);
            Canvas.SetTop(card, y);

            var inner = new Grid { Margin = new Thickness(12, 6, 12, 6) };
            inner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
            inner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(14) });
            inner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // 标题块：第一行「歌名 + 歌手」，第二行是当前那句歌词
            var head = new Grid();
            head.RowDefinitions.Add(new RowDefinition { Height = new GridLength(15) });
            head.RowDefinitions.Add(new RowDefinition { Height = new GridLength(13) });

            var headTop = new Grid();
            headTop.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headTop.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _expTitle = GlassStyle.Label("媒体控制", 13.5, Color.FromArgb(240, 0xE8, 0xF4, 0xFF));
            _expTitle.TextTrimming = TextTrimming.CharacterEllipsis;
            _expTitle.FontWeight = FontWeights.SemiBold;
            SoftShadow(_expTitle, 0.45);
            Grid.SetColumn(_expTitle, 0);
            headTop.Children.Add(_expTitle);

            _expArtist = GlassStyle.Label("", 11, Color.FromArgb(210, 0xC8, 0xD8, 0xF0));
            _expArtist.Margin = new Thickness(8, 0, 0, 0);
            SoftShadow(_expArtist, 0.3);
            Grid.SetColumn(_expArtist, 1);
            headTop.Children.Add(_expArtist);

            Grid.SetRow(headTop, 0);
            head.Children.Add(headTop);

            _expLyric = GlassStyle.Label("", 10.5, Color.FromArgb(200, 0xC0, 0xD4, 0xEE));
            _expLyric.TextTrimming = TextTrimming.CharacterEllipsis;
            _expLyric.Margin = new Thickness(0, 0, 0, 0);
            SoftShadow(_expLyric, 0.3);
            Grid.SetRow(_expLyric, 1);
            head.Children.Add(_expLyric);

            Grid.SetRow(head, 0);
            inner.Children.Add(head);

            var eq = BuildEqualizer(w - 24, 12);
            Grid.SetRow(eq, 1);
            inner.Children.Add(eq);

            var row = new Grid();
            for (int i = 0; i < 3; i++)
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var prev = BuildMediaButton("◀◀", "上一首", 0xB1, 0);
            Grid.SetColumn(prev, 0);
            row.Children.Add(prev);

            var play = BuildMediaButton("▶", "播放", 0xB3, 12, out _playGlyph, out _playLabel);
            Grid.SetColumn(play, 1);
            row.Children.Add(play);

            var next = BuildMediaButton("▶▶", "下一首", 0xB0, 12);
            Grid.SetColumn(next, 2);
            row.Children.Add(next);

            Grid.SetRow(row, 2);
            inner.Children.Add(row);

            card.Child = inner;
            return card;
        }

        // ------------------------------------------------------------------
        //  第二页：桌面宠物
        // ------------------------------------------------------------------
        private Canvas BuildPetPage()
        {
            var page = new Canvas { Width = ExpandedW, Height = ExpandedH, IsHitTestVisible = true };

            var title = GlassStyle.Label("桌面宠物", 22, Color.FromArgb(250, 0xF2, 0xF8, 0xFF));
            title.FontWeight = FontWeights.SemiBold;
            SoftShadow(title, 0.5);
            Canvas.SetLeft(title, 28);
            Canvas.SetTop(title, 24);
            page.Children.Add(title);

            var sub = GlassStyle.Label("选一只，它就到桌面上；点一下会弹，可拖动", 11.5, Color.FromArgb(186, 0xB8, 0xCB, 0xE8));
            sub.Width = 240;
            sub.TextWrapping = TextWrapping.NoWrap;
            SoftShadow(sub, 0.35);
            Canvas.SetLeft(sub, 29);
            Canvas.SetTop(sub, 60);
            page.Children.Add(sub);

            double tileW = (ExpandedW - 56 - 12) / 2.0;
            double tileH = 140;
            for (int i = 0; i < Pet.Ids.Length; i++)
            {
                int col = i % 2, row = i / 2;
                var tile = BuildPetTile(i, tileW, tileH);
                Canvas.SetLeft(tile, 28 + col * (tileW + 12));
                Canvas.SetTop(tile, 88 + row * (tileH + 10));
                page.Children.Add(tile);
            }

            // 「选自己的图」——A 方案：只收带透明通道的 PNG
            var btn = GlassStyle.Tile(14, 20);
            btn.Width = ExpandedW - 56;
            btn.Height = 46;
            var btnGrid = new Grid();
            var btnText = GlassStyle.Label("选自己的图（透明 PNG）", 12.5, Color.FromArgb(238, 0xE6, 0xF1, 0xFF));
            btnText.HorizontalAlignment = HorizontalAlignment.Center;
            btnText.VerticalAlignment = VerticalAlignment.Center;
            btnText.FontWeight = FontWeights.SemiBold;
            SoftShadow(btnText, 0.45);
            btnGrid.Children.Add(btnText);
            btn.Child = btnGrid;
            btn.Cursor = Cursors.Hand;
            btn.Focusable = true;
            btn.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { OnPickPetImage(); e.Handled = true; };
            btn.KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.Key == Key.Enter || e.Key == Key.Space)
                {
                    e.Handled = true;
                    OnPickPetImage();
                }
            };
            btn.GotFocus += delegate
            {
                btn.BorderBrush = new SolidColorBrush(Color.FromArgb(180, 0x74, 0xA6, 0xFF));
            };
            btn.LostFocus += delegate
            {
                btn.BorderBrush = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255));
            };
            Canvas.SetLeft(btn, 28);
            Canvas.SetTop(btn, 390);
            page.Children.Add(btn);

            _petHint = new TextBlock
            {
                Text = "支持抠好的透明 PNG（四角是透明的）；带白底的图会被拒绝，因为它贴到桌面上就是一块方块。",
                FontSize = 10.5,
                LineHeight = 16,
                TextWrapping = TextWrapping.Wrap,
                Width = ExpandedW - 60,
                Foreground = new SolidColorBrush(Color.FromArgb(168, 0xA8, 0xBD, 0xDC))
            };
            SoftShadow(_petHint, 0.3);
            Canvas.SetLeft(_petHint, 30);
            Canvas.SetTop(_petHint, 444);
            page.Children.Add(_petHint);

            RefreshPetUi();
            return page;
        }

        private Border BuildPetTile(int index, double w, double h)
        {
            var tile = GlassStyle.Tile(16, 22);
            tile.Width = w;
            tile.Height = h;
            tile.Cursor = Cursors.Hand;

            var inner = new Grid();
            inner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            inner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(22) });

            // 缩略图底板：桌面的文字会透过玻璃卡片，直接在透明底上看图会很乱，垫一层薄暗底把宠物衬出来
            var plate = new Border
            {
                CornerRadius = new CornerRadius(12),
                Margin = new Thickness(5, 5, 5, 3),
                Background = new SolidColorBrush(Color.FromArgb(84, 10, 14, 24)),
                IsHitTestVisible = false
            };
            Grid.SetRowSpan(plate, 2);
            inner.Children.Add(plate);

            var img = new Image
            {
                Stretch = Stretch.Uniform,
                Height = 100,                                  // 写死高度，宽度按图自身比例算，绝不拉伸变形
                Margin = new Thickness(8, 7, 8, 1),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            string path = Pet.PresetPath(Pet.Ids[index]);
            if (path != null && File.Exists(path))
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelHeight = 240;
                bmp.UriSource = new Uri(path);
                bmp.EndInit();
                bmp.Freeze();
                img.Source = bmp;
            }
            else
            {
                var miss = GlassStyle.Label("缺图", 11, Color.FromArgb(150, 0xA8, 0xBD, 0xDC));
                miss.HorizontalAlignment = HorizontalAlignment.Center;
                miss.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetRow(miss, 0);
                inner.Children.Add(miss);
            }
            Grid.SetRow(img, 0);
            inner.Children.Add(img);

            var name = GlassStyle.Label(Pet.Names[index], 11.5, Color.FromArgb(226, 0xD8, 0xE8, 0xFF));
            name.HorizontalAlignment = HorizontalAlignment.Center;
            name.VerticalAlignment = VerticalAlignment.Center;
            SoftShadow(name, 0.35);
            Grid.SetRow(name, 1);
            inner.Children.Add(name);

            tile.Child = inner;

            var ring = new Border
            {
                CornerRadius = new CornerRadius(16),
                BorderThickness = new Thickness(1.5),
                BorderBrush = new SolidColorBrush(Color.FromArgb(220, 0x7C, 0xC4, 0xFF)),
                Opacity = 0,
                IsHitTestVisible = false
            };

            var host = new Grid();
            host.Children.Add(tile);
            host.Children.Add(ring);
            host.Height = h;
            host.Width = w;
            tile.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e)
            {
                Pet.Show(Pet.Ids[index]);
                if (_petHint != null)
                {
                    _petHint.Foreground = new SolidColorBrush(Color.FromArgb(186, 0xB8, 0xCB, 0xE8));
                    _petHint.Text = "已经让它站到桌面右下角了；右键它会自己收起来，托盘里也能再叫出来。";
                }
                RefreshPetUi();
                e.Handled = true;
            };

            _petTiles.Add(tile);
            _petTileRings.Add(ring);
            return new Border { Child = host, Width = w, Height = h };
        }

        private void RefreshPetUi()
        {
            string cur = Pet.CurrentId;
            for (int i = 0; i < _petTileRings.Count; i++)
                _petTileRings[i].Opacity = (cur == Pet.Ids[i]) ? 1 : 0;
        }

        private void SwitchPage(int page)
        {
            if (_page1 == null || _page2 == null) return;
            if (page == _page) return;
            _page = page;

            Canvas show = page == 0 ? _page1 : _page2;
            Canvas hide = page == 0 ? _page2 : _page1;

            show.Visibility = Visibility.Visible;
            show.IsHitTestVisible = true;
            show.BeginAnimation(OpacityProperty, null);
            show.Opacity = 0;
            var fadeIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(190))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            show.BeginAnimation(OpacityProperty, fadeIn);

            hide.IsHitTestVisible = false;
            var fadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(140))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            fadeOut.Completed += delegate
            {
                hide.Visibility = Visibility.Collapsed;
                hide.BeginAnimation(OpacityProperty, null);
                hide.Opacity = 1;
            };
            hide.BeginAnimation(OpacityProperty, fadeOut);

            if (_tabPill != null)
            {
                var tr = _tabPill.RenderTransform as TranslateTransform;
                if (tr != null)
                {
                    tr.BeginAnimation(TranslateTransform.XProperty,
                        new DoubleAnimation(page == 0 ? 0.0 : 58.0, TimeSpan.FromMilliseconds(220))
                        {
                            EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut }
                        });
                }
            }
            if (_tabTextA != null && _tabTextB != null)
            {
                _tabTextA.Foreground = new SolidColorBrush(page == 0
                    ? Color.FromArgb(255, 0xEC, 0xF4, 0xFF) : Color.FromArgb(170, 0xC2, 0xD4, 0xEE));
                _tabTextB.Foreground = new SolidColorBrush(page == 1
                    ? Color.FromArgb(255, 0xEC, 0xF4, 0xFF) : Color.FromArgb(170, 0xC2, 0xD4, 0xEE));
                _tabTextA.FontWeight = page == 0 ? FontWeights.SemiBold : FontWeights.Normal;
                _tabTextB.FontWeight = page == 1 ? FontWeights.SemiBold : FontWeights.Normal;
            }
        }

        private void OnPickPetImage()
        {
            var dlg = new OpenFileDialog
            {
                Title = "选一张透明背景的宠物图",
                Filter = "PNG 图片 (*.png)|*.png",
                CheckFileExists = true
            };
            if (dlg.ShowDialog(this) != true) return;

            string why;
            if (Pet.UseCustom(dlg.FileName, out why))
            {
                _petHint.Foreground = new SolidColorBrush(Color.FromArgb(200, 0x9C, 0xE8, 0xC4));
                _petHint.Text = "换成你自己的图了，已经站到桌面右下角。";
                RefreshPetUi();
            }
            else
            {
                _petHint.Foreground = new SolidColorBrush(Color.FromArgb(220, 0xFF, 0xC0, 0x88));
                _petHint.Text = why + "——贴到桌面上会带一块方块底，所以没有采用。请换成抠好背景的透明 PNG。";
            }
        }

        private const int EqBarCount = 28;

        /// <summary>标题栏右侧的实时频谱条（数据来自系统回环音频）。</summary>
        private Canvas BuildEqualizer(double width, double height)
        {
            var canvas = new Canvas
            {
                Width = width,
                Height = height,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            double barW = 5;
            double step = width / EqBarCount;
            _eqRects = new Rectangle[EqBarCount];

            for (int i = 0; i < EqBarCount; i++)
            {
                var brush = new LinearGradientBrush();
                brush.StartPoint = new Point(0, 1);
                brush.EndPoint = new Point(0, 0);
                brush.GradientStops.Add(new GradientStop(Color.FromArgb(238, 0x66, 0xE6, 0xC8), 0));
                brush.GradientStops.Add(new GradientStop(Color.FromArgb(238, 0x6E, 0xA8, 0xFF), 1));

                var rect = new Rectangle
                {
                    Width = barW,
                    Height = 2,
                    RadiusX = 2,
                    RadiusY = 2,
                    Fill = brush,
                    Opacity = 0.45
                };
                Canvas.SetLeft(rect, i * step + (step - barW) / 2);
                Canvas.SetBottom(rect, 0);
                canvas.Children.Add(rect);
                _eqRects[i] = rect;
            }
            return canvas;
        }

        /// <summary>氛围光球：径向渐变（中心亮、边缘化开），颜色每帧由音乐能量改写。</summary>
        private static double Lerp(double a, double b, double t)
        {
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            return a + (b - a) * t;
        }

        private static Color MixColor(Color a, Color b, double t)
        {
            return Color.FromRgb(
                (byte)Math.Round(Lerp(a.R, b.R, t)),
                (byte)Math.Round(Lerp(a.G, b.G, t)),
                (byte)Math.Round(Lerp(a.B, b.B, t)));
        }

        /// <summary>
        /// 音乐卡氛围：只作用在「媒体控制」这张卡内部。
        /// 自动跟随系统正在播放的声音（WASAPI 回环），颜色取频谱的重心，
        /// 亮度随音量呼吸，没声音就整层淡掉——不掺任何假数据。
        /// </summary>
        private void TickMusicAura()
        {
            if (_mediaWash == null) return;
            if (_mediaWashBg == null) _mediaWashBg = _mediaWash.Background as LinearGradientBrush;
            if (_mediaWashBg == null) return;

            bool active;
            double lvl;
            float[] bands;
            Color colA, colB;
            AuraCompute(out active, out lvl, out bands, out colA, out colB);

            double target = active ? 0.42 + lvl * 0.58 : 0.0;
            _auraT += (target - _auraT) * (active ? 0.26 : 0.16);
            if (_auraT < 0.006) _auraT = 0;

            if (_auraT <= 0)
            {
                _mediaWash.Opacity = 0;
                return;
            }


            var gs = _mediaWashBg.GradientStops;
            // 顶部几乎透明，越往下越亮；上段走主色、下段走副色，两个色自然过渡
            double bottomAlpha = 58 + lvl * 86;      // 跟着音量呼吸，别糊成一块实色
            gs[0].Color = Color.FromArgb(0, colA.R, colA.G, colA.B);
            gs[1].Color = Color.FromArgb((byte)(bottomAlpha * 0.55), colB.R, colB.G, colB.B);
            gs[2].Color = Color.FromArgb((byte)Math.Min(255, bottomAlpha), colA.R, colA.G, colA.B);

            _mediaWash.Opacity = _auraT * 0.92;
        }

        /// <summary>
        /// 配色 + 音量统一算法。
        /// 有专辑封面时用封面算出来的两个主色（这才叫「跟着这首歌」）；抓不到封面才退回频谱重心配色。
        /// 亮度始终跟着实时音量走。
        /// </summary>
        private static void AuraCompute(out bool active, out double lvl, out float[] bands, out Color colA, out Color colB)
        {
            active = AudioTap.Signal;
            lvl = Math.Min(1.0, Math.Max(0, AudioTap.Level * 2.4));
            bands = AudioTap.Bands;

            if (MediaSession.HasArt)
            {
                int a = MediaSession.ArtA, b = MediaSession.ArtB;
                colA = Color.FromRgb((byte)((a >> 16) & 0xFF), (byte)((a >> 8) & 0xFF), (byte)(a & 0xFF));
                colB = Color.FromRgb((byte)((b >> 16) & 0xFF), (byte)((b >> 8) & 0xFF), (byte)(b & 0xFF));
                return;
            }

            double bass = (bands[1] + bands[3] + bands[5] + bands[7]) / 4.0;
            double mid = (bands[9] + bands[11] + bands[13] + bands[15]) / 4.0;
            double treb = (bands[19] + bands[21] + bands[23] + bands[25] + bands[27]) / 5.0;
            double total = Math.Max(0.001, bass + mid + treb);
            double centroid = (bass * 0.22 + mid * 0.5 + treb * 0.85) / total;   // 0..0.85

            double hue = 150 + centroid * 115;          // 150°(绿青) → 265°(蓝紫)
            double sat = 0.42 + Math.Min(0.30, lvl * 0.35);
            colA = HsvToColor(hue, sat, 0.96);
            colB = HsvToColor((hue + 42) % 360, Math.Min(0.9, sat + 0.1), 0.92);
        }

        /// <summary>
        /// 缩回态的音乐胶囊：左边 4 根跳动的频谱条、中间「歌名 / 歌手」、整块一层跟着声音流动的彩色氛围光。
        /// 盖在时钟上面，有歌在放时淡入，停了淡出。
        /// </summary>
        private Grid BuildMusicPill()
        {
            var g = new Grid
            {
                // 不写死宽度：播放时胶囊会变宽，这一层跟着撑满，否则右边一截是硬边
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Height = CollapsedH,
                IsHitTestVisible = false,
                Opacity = 0,
                ClipToBounds = true
            };

            // 底：接近不透明的深色，用来盖住下面的时钟
            var fill = new Border
            {
                CornerRadius = new CornerRadius(CollapsedR),
                Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x10, 0x18))
            };
            g.Children.Add(fill);

            // 氛围光：两块柔光叠在胶囊里，颜色来自这张专辑封面（抓不到才退回频谱配色）
            // 不要在这里加 BlurEffect：整层模糊每帧都要重算，是播放时掉帧的大头，
            // 径向渐变本身边缘就已经是化开的。
            _musicWash = new Grid { Opacity = 0 };

            _musicWashA = new RadialGradientBrush
            {
                Center = new Point(0.45, 0.48),
                GradientOrigin = new Point(0.45, 0.48),
                RadiusX = 0.55,
                RadiusY = 0.62
            };
            _musicWashA.GradientStops.Add(new GradientStop(Color.FromArgb(120, 0x5C, 0xE0, 0xD0), 0.00));
            _musicWashA.GradientStops.Add(new GradientStop(Color.FromArgb(40, 0x5C, 0xE0, 0xD0), 0.55));
            _musicWashA.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0x5C, 0xE0, 0xD0), 1.00));

            _musicWashB = new RadialGradientBrush
            {
                Center = new Point(0.55, 0.48),
                GradientOrigin = new Point(0.55, 0.48),
                RadiusX = 0.60,
                RadiusY = 0.66
            };
            _musicWashB.GradientStops.Add(new GradientStop(Color.FromArgb(110, 0x74, 0xA6, 0xFF), 0.00));
            _musicWashB.GradientStops.Add(new GradientStop(Color.FromArgb(34, 0x74, 0xA6, 0xFF), 0.55));
            _musicWashB.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0x74, 0xA6, 0xFF), 1.00));

            var blobA = new Ellipse
            {
                Width = 190, Height = 96,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(-46, 10, 0, 0),
                Fill = _musicWashA,
                IsHitTestVisible = false
            };
            var blobB = new Ellipse
            {
                Width = 200, Height = 100,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 6, -52, 0),
                Fill = _musicWashB,
                IsHitTestVisible = false
            };
            _musicWash.Children.Add(blobA);
            _musicWash.Children.Add(blobB);
            g.Children.Add(_musicWash);

            var row = new Grid { Margin = new Thickness(13, 0, 13, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // 左：4 根频谱条（底对齐到 16px 的格子里）
            var bars = new Grid
            {
                Width = 17,
                Height = 16,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 9, 0)
            };
            for (int i = 0; i < 4; i++)
                bars.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _mpBars = new Rectangle[4];
            _mpBarBrush = new SolidColorBrush(Color.FromRgb(0x5C, 0xE0, 0xD0));
            for (int i = 0; i < 4; i++)
            {
                var bar = new Rectangle
                {
                    Width = 2.4,
                    Height = 3,
                    RadiusX = 1.2,
                    RadiusY = 1.2,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Fill = _mpBarBrush
                };
                Grid.SetColumn(bar, i);
                bars.Children.Add(bar);
                _mpBars[i] = bar;
            }
            Grid.SetColumn(bars, 0);
            row.Children.Add(bars);

            // 右：歌名 + 歌手
            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            _mpTitle = new TextBlock
            {
                Text = "正在播放",
                FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xF2, 0xF6, 0xFF)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                LineHeight = 15
            };
            _mpArtist = new TextBlock
            {
                Text = "系统媒体",
                FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
                FontSize = 9.5,
                Foreground = new SolidColorBrush(Color.FromArgb(210, 0x93, 0xA2, 0xBF)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 1, 0, 0),
                LineHeight = 12
            };
            // 歌名太长不省略，改成从右往左循环滚动（外面套一层裁切容器）
            _mpTitle.TextTrimming = TextTrimming.None;
            _mpTitleTx = new TranslateTransform(0, 0);
            _mpTitle.RenderTransform = _mpTitleTx;
            _mpTitleClip = new Grid
            {
                ClipToBounds = true,
                Height = 15,
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            _mpTitleClip.OpacityMask = EdgeFadeMask();
            _mpTitleClip.Children.Add(_mpTitle);
            texts.Children.Add(_mpTitleClip);

            // 收起态三行放不下，歌手不再显示（展开卡片里仍然有）
            Grid.SetColumn(texts, 1);
            row.Children.Add(texts);

            // 右侧：实时歌词（跟着播放进度走，33ms 刷新一次）
            // 右侧：实时歌词（单行，太长就横向滚动，绝不换行、不上下爬）
            _mpLyric = new TextBlock
            {
                Text = "",
                FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(242, 0xC6, 0xE2, 0xFF)),
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.None,
                LineHeight = 14,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                IsHitTestVisible = false
            };
            // 歌词放在歌名下面同一列里：一行歌名、一行歌词，宽度都比原来宽一倍多
            _mpLyricClip = new Grid
            {
                Height = 14,
                ClipToBounds = true,
                Margin = new Thickness(0, 2, 0, 0),
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            _mpLyricX = new TranslateTransform(0, 0);
            _mpLyric.RenderTransform = _mpLyricX;
            _mpLyricClip.OpacityMask = EdgeFadeMask();
            _mpLyricClip.Children.Add(_mpLyric);
            texts.Children.Add(_mpLyricClip);
            g.Children.Add(row);
            return g;
        }

        /// <summary>每 33ms 更新一次缩回态音乐胶囊（和频谱、氛围光同一个节拍）。</summary>
        private void TickMusicPill()
        {
            if (_musicLayer == null) return;

            bool active;
            double lvl;
            float[] bands;
            Color colA, colB;
            AuraCompute(out active, out lvl, out bands, out colA, out colB);

            // 调试用：ISLAND_LYRIC_DEMO=一句歌词 时假装在播歌，方便调版式和滚动速度
            string demo = Environment.GetEnvironmentVariable("ISLAND_LYRIC_DEMO");
            bool demoOn = !string.IsNullOrEmpty(demo) && !_expanded;
            double target = ((MediaSession.Playing || demoOn) && !_expanded) ? 1.0 : 0.0;
            _musicT += (target - _musicT) * (target > _musicT ? 0.32 : 0.18);
            if (_musicT < 0.004) _musicT = 0;
            if (_musicT > 0.996) _musicT = 1;
            _musicLayer.Opacity = _btCardOn ? 0 : _musicT;
            // 蓝牙卡在的时候，必须在每一帧都把下面几层真正关掉。
            // 只在 ShowBtCard 里关一次是不够的：这里附近好几处逻辑会按「播放状态」把时钟层
            // 的 Opacity 写回去，一写回去就会从卡片那块半透明底里渗出来，
            // 看着就是「时钟压着蓝牙卡」的两层皮。
            if (_btCardOn)
            {
                _collapsedLayer.Opacity = 0;
                if (_collapsedLayer.Visibility != Visibility.Collapsed) _collapsedLayer.Visibility = Visibility.Collapsed;
                if (_musicLayer != null) _musicLayer.Opacity = 0;
                if (_bubbleLayer != null) _bubbleLayer.Opacity = 0;
            }
            // 音乐胶囊和时钟胶囊必须交叉淡入淡出：两个都半透明同时画出来，
            // 就会看到歌名/歌手压在时间上，看着像“抽了一下还带鬼影”
            else
            {
                _collapsedLayer.Opacity = 1 - _musicT;
                _collapsedLayer.Visibility = _musicT > 0.5 ? Visibility.Collapsed : Visibility.Visible;
            }

            // 播歌时胶囊不撑宽：保持收起态尺寸，歌词就在这个宽度里滚


            if (_musicT <= 0)
            {
                if (_musicWash != null) _musicWash.Opacity = 0;
                return;
            }

            // 文案：系统给什么就显示什么，一个字都没有就老实说「正在播放」
            string title = MediaSession.Title;
            string artist = MediaSession.Artist;
            if (demoOn) { title = "测试歌曲"; artist = "测试歌手"; }
            var tdemo = Environment.GetEnvironmentVariable("ISLAND_TITLE_DEMO");
            if (tdemo != null) title = tdemo;
            if (title.Length == 0) title = "正在播放";
            if (artist.Length == 0) artist = "系统媒体";
            if (title != _mpTitleText)
            {
                _mpTitleText = title;
                if (_mpTitle != null) _mpTitle.Text = title;
                _mpTitleOff = 0; _mpTitlePhase = 0; _mpTitleHold = 0;
            }
            if (artist != _mpArtistText)
            {
                _mpArtistText = artist;
                if (_mpArtist != null) _mpArtist.Text = artist;
            }
            if (_mpTitleClip != null && _mpTitleClip.ActualWidth > 1)
                ScrollLine(_mpTitle, _mpTitleTx, _mpTitleClip, ref _mpTitleOff, ref _mpTitlePhase, ref _mpTitleHold);

            if (_mpLyric != null)
            {
                string line = MediaSession.CurrentLine();
                if (demoOn) line = demo;
                if (line != _mpLyricText)
                {
                    _mpLyricText = line;
                    _mpLyric.Text = line;
                    _mpLyricOff = 0; _mpLyricPhase = 0; _mpLyricHold = 0;
                }
                if (_mpLyricClip != null && _mpLyricClip.ActualWidth > 1)
                    ScrollLine(_mpLyric, _mpLyricX, _mpLyricClip, ref _mpLyricOff, ref _mpLyricPhase, ref _mpLyricHold);
            }
            // 频谱：4 根小条跳起来，安静时压成细线
            if (_mpBars != null)
            {
                int[] idx = { 1, 7, 15, 23 };
                for (int i = 0; i < _mpBars.Length; i++)
                {
                    double v = active ? bands[idx[i]] : 0.0;
                    if (v < 0) v = 0;
                    _mpBars[i].Height = 2.0 + Math.Min(1.0, v * 1.6) * 13.0;
                }
            }

            if (_musicWash != null && _musicWashA != null && _musicWashB != null)
            {
                // 两块径向柔光（带模糊）混出两个颜色，边缘是羽化过的，不会再有一条硬边
                double core = 66 + lvl * 92;
                var gsA = _musicWashA.GradientStops;
                gsA[0].Color = Color.FromArgb((byte)Math.Min(255, core), colA.R, colA.G, colA.B);
                gsA[1].Color = Color.FromArgb((byte)(core * 0.36), colA.R, colA.G, colA.B);
                gsA[2].Color = Color.FromArgb(0, colA.R, colA.G, colA.B);

                var gsB = _musicWashB.GradientStops;
                gsB[0].Color = Color.FromArgb((byte)Math.Min(255, core * 0.92), colB.R, colB.G, colB.B);
                gsB[1].Color = Color.FromArgb((byte)(core * 0.32), colB.R, colB.G, colB.B);
                gsB[2].Color = Color.FromArgb(0, colB.R, colB.G, colB.B);

                _musicWash.Opacity = 0.80 + 0.20 * lvl;
            }

            if (_mpBarBrush != null)
                _mpBarBrush.Color = colA;
        }
        /// <summary>按当前字体量一段文字的宽度（TextBlock 被父级限宽后量不准，直接算）。</summary>
        private static LinearGradientBrush EdgeFadeMask()
        {
            var fade = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0.5),
                EndPoint = new Point(1, 0.5)
            };
            fade.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 0.0));
            fade.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF), 0.08));
            fade.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF), 0.92));
            fade.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1.0));
            return fade;
        }

        /// <summary>按某个 TextBlock 自己的字体量宽度（被父级限宽后 TextBlock 会量不准，直接算）。</summary>
        private double TextWidthFor(TextBlock tb, string s)
        {
            if (tb == null || string.IsNullOrEmpty(s)) return 0;
            var ft = new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch),
                tb.FontSize, Brushes.White,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            return ft.WidthIncludingTrailingWhitespace;
        }

        /// <summary>
        /// 单行循环走马灯：先整行停住让人看清开头，再往左走，走到末尾停一下再从头来；
        /// 两端用渐变遮罩淡出，避免把字切出半截的硬边。
        /// </summary>
        private void ScrollLine(TextBlock tb, TranslateTransform tx, Grid clip, ref double off, ref int phase, ref int hold)
        {
            if (tb == null || tx == null || clip == null) return;
            double full = clip.ActualWidth;
            double boxW = full - 10;                 // 两侧各留 5px，别贴着边不算“放得下”
            if (boxW <= 1) return;
            double w = TextWidthFor(tb, tb.Text);

            if (w <= boxW)
            {
                // 放得下：居中静止，且关掉边缘淡出，整行清清楚楚
                if (tb.HorizontalAlignment != HorizontalAlignment.Center) tb.HorizontalAlignment = HorizontalAlignment.Center;
                if (clip.OpacityMask != null) clip.OpacityMask = null;
                off = 0; phase = 0; hold = 0;
                if (tx.X != 0) tx.X = 0;
                return;
            }

            // 放不下：左对齐走马灯，两端加淡出，避免硬切半个字
            if (tb.HorizontalAlignment != HorizontalAlignment.Left) tb.HorizontalAlignment = HorizontalAlignment.Left;
            if (clip.OpacityMask == null) clip.OpacityMask = EdgeFadeMask();

            double travel = w - full + 4;
            if (phase == 0)
            {
                off = 0;
                if (++hold >= 54) { phase = 1; hold = 0; }   // 开头停 1.8s
            }
            else if (phase == 1)
            {
                off -= 1.0;                                   // 33ms 一拍 ≈ 30px/s
                if (off <= -travel) { off = -travel; phase = 2; hold = 0; }
            }
            else
            {
                if (++hold >= 36) { phase = 0; hold = 0; off = 0; }  // 末尾停 1.2s
            }
            tx.X = off;
        }

        private double TextWidth(string s)
        {
            if (string.IsNullOrEmpty(s) || _mpLyric == null) return 0;
            var ft = new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(_mpLyric.FontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
                _mpLyric.FontSize, Brushes.White,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            return ft.WidthIncludingTrailingWhitespace;
        }

        /// <summary>把一整句歌词切成“每段都能完整显示”的若干片，优先在空格/标点处断开。</summary>
        private List<string> SplitLyricChunks(string line, double box)
        {
            var list = new List<string>();
            string s = (line ?? "").Trim();
            if (s.Length == 0) return list;
            if (TextWidth(s) <= box)
            {
                list.Add(s);
                return list;
            }

            const string breaks = " ，,、。.！!？?；;：:·-—";
            int i = 0;
            while (i < s.Length)
            {
                int len = 0, breakAt = 0;
                while (i + len < s.Length)
                {
                    if (TextWidth(s.Substring(i, len + 1)) > box && len > 0) break;
                    len++;
                    if (breaks.IndexOf(s[i + len - 1]) >= 0) breakAt = len;
                }
                if (i + len < s.Length && breakAt > 0 && breakAt < len) len = breakAt;
                if (len <= 0) len = 1;
                string part = s.Substring(i, len).Trim();
                if (part.Length > 0) list.Add(part);
                i += len;
                while (i < s.Length && s[i] == ' ') i++;
            }
            return list;
        }
        private static Color HsvToColor(double h, double s, double v)
        {
            h = (h % 360 + 360) % 360;
            double c = v * s;
            double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
            double m = v - c;
            double r, g, b;
            if (h < 60) { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }
            return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
        }
        private void TickEqualizer()
        {
            if (_eqRects == null) return;
            bool active = AudioTap.Signal;
            for (int i = 0; i < _eqRects.Length; i++)
            {
                var r = _eqRects[i];
                if (r == null) continue;
                double v = active ? AudioTap.Bands[i] : 0;
                // 低频段（左侧）视觉上略高一点，看起来更像音乐
                double weight = 1.0 - i * 0.010;
                double target = active ? 2 + Math.Pow(v, 0.62) * 15.5 * weight : 1;
                double h = r.Height + (target - r.Height) * 0.30;
                if (h < (active ? 2 : 1)) h = active ? 2 : 1;
                if (h > 17.5) h = 17.5;
                r.Height = h;
                // 没在放音乐时压到几乎看不见的一条细线，别看着像一排虚线
                r.Opacity = active ? 0.95 : 0.16;
            }
        }

        private Border BuildMediaButton(string glyph, string label, byte vk, double leftMargin)
        {
            TextBlock gb, lb;
            return BuildMediaButton(glyph, label, vk, leftMargin, out gb, out lb);
        }

        private Border BuildMediaButton(string glyph, string label, byte vk, double leftMargin, out TextBlock glyphBlock, out TextBlock labelBlock)
        {
            var tile = GlassStyle.Tile(11, 22);
            tile.Height = 32;
            tile.HorizontalAlignment = HorizontalAlignment.Stretch;
            tile.Margin = new Thickness(leftMargin, 0, 0, 0);
            tile.Cursor = Cursors.Hand;

            var stack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            glyphBlock = new TextBlock
            {
                Text = glyph,
                FontSize = 12.5,
                Foreground = new SolidColorBrush(Color.FromArgb(238, 0xE6, 0xEF, 0xFF)),
                VerticalAlignment = VerticalAlignment.Center
            };
            TextOptions.SetTextFormattingMode(glyphBlock, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(glyphBlock, TextRenderingMode.ClearType);
            stack.Children.Add(glyphBlock);

            labelBlock = new TextBlock
            {
                Text = label,
                FontSize = 10.5,
                Margin = new Thickness(5, 0, 0, 0),
                Foreground = new SolidColorBrush(Color.FromArgb(210, 0xC4, 0xD6, 0xEE)),
                VerticalAlignment = VerticalAlignment.Center
            };
            TextOptions.SetTextFormattingMode(labelBlock, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(labelBlock, TextRenderingMode.ClearType);
            stack.Children.Add(labelBlock);

            tile.Child = stack;
            tile.Focusable = true;

            tile.KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.Key == Key.Enter || e.Key == Key.Space)
                {
                    e.Handled = true;
                    SendMediaKey(vk);
                    FlashTile(tile);
                    if (vk == 0xB3) UpdatePlayPauseState(!MediaSession.Playing);
                }
            };
            tile.GotFocus += delegate
            {
                tile.BorderBrush = new SolidColorBrush(Color.FromArgb(180, 0x74, 0xA6, 0xFF));
            };
            tile.LostFocus += delegate
            {
                tile.BorderBrush = new SolidColorBrush(Color.FromArgb(26, 255, 255, 255));
            };

            tile.MouseEnter += delegate
            {
                tile.Background = new SolidColorBrush(Color.FromArgb(52, 255, 255, 255));
                tile.BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255));
            };
            tile.MouseLeave += delegate
            {
                tile.Background = GlassStyle.Tile(13, 22).Background;
                tile.BorderBrush = new SolidColorBrush(Color.FromArgb(26, 255, 255, 255));
            };
            tile.MouseLeftButtonDown += delegate (object s, MouseButtonEventArgs e) { e.Handled = true; };
            tile.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                SendMediaKey(vk);
                FlashTile(tile);
                if (vk == 0xB3) UpdatePlayPauseState(!MediaSession.Playing);
            };
            return tile;
        }

        private void UpdatePlayPauseState()
        {
            UpdatePlayPauseState(MediaSession.Playing);
        }

        private void UpdatePlayPauseState(bool isPlaying)
        {
            if (_playGlyph == null || _playLabel == null) return;
            string expectedGlyph = isPlaying ? "⏸" : "▶";
            string expectedLabel = isPlaying ? "暂停" : "播放";
            if (_playGlyph.Text != expectedGlyph) _playGlyph.Text = expectedGlyph;
            if (_playLabel.Text != expectedLabel) _playLabel.Text = expectedLabel;
        }

        /// <summary>按键反馈：短促亮一下。</summary>
        private void FlashTile(Border tile)
        {
            try
            {
                tile.Background = new SolidColorBrush(Color.FromArgb(90, 0x7A, 0xC8, 0xFF));
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
                timer.Tick += delegate
                {
                    timer.Stop();
                    tile.Background = GlassStyle.Tile(13, 22).Background;
                };
                timer.Start();
            }
            catch { }
        }

        private Border BuildWeChatCard(double x, double y, double w, double h)
        {
            var card = GlassStyle.Tile(18, 26);
            card.Width = w;
            card.Height = h;
            Canvas.SetLeft(card, x);
            Canvas.SetTop(card, y);
            card.Cursor = Cursors.Hand;
            card.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                WeChatFeed.OpenWeChat();
            };
            card.MouseEnter += delegate { card.BorderBrush = new SolidColorBrush(Color.FromArgb(64, 255, 255, 255)); };
            card.MouseLeave += delegate { card.BorderBrush = new SolidColorBrush(Color.FromArgb(26, 255, 255, 255)); };

            var inner = new Grid { Margin = new Thickness(14, 9, 14, 9) };
            inner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(17) });
            for (int i = 0; i < 3; i++)
                inner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var title = GlassStyle.Label("微信消息", 10.5, Color.FromArgb(170, 0xA4, 0xB6, 0xD8));
            Grid.SetColumn(title, 0);
            head.Children.Add(title);

            var hint = new TextBlock
            {
                Text = "点击打开微信",
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromArgb(120, 0x9E, 0xB0, 0xD2))
            };
            Grid.SetColumn(hint, 1);
            head.Children.Add(hint);
            Grid.SetRow(head, 0);
            inner.Children.Add(head);

            for (int i = 0; i < 3; i++)
            {
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                _wxSender[i] = new TextBlock
                {
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromArgb(235, 0xE8, 0xF1, 0xFF)),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(_wxSender[i], 0);
                row.Children.Add(_wxSender[i]);

                _wxBody[i] = new TextBlock
                {
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromArgb(175, 0xA8, 0xBA, 0xDC)),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(10, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(_wxBody[i], 1);
                row.Children.Add(_wxBody[i]);

                _wxTime[i] = new TextBlock
                {
                    FontSize = 9,
                    Foreground = new SolidColorBrush(Color.FromArgb(120, 0x9E, 0xB0, 0xD2)),
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(_wxTime[i], 2);
                row.Children.Add(_wxTime[i]);

                Grid.SetRow(row, i + 1);
                inner.Children.Add(row);
            }

            _wxEmpty = new TextBlock
            {
                Text = "暂无新消息",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(130, 0xA8, 0xBA, 0xDC)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed
            };
            Grid.SetRow(_wxEmpty, 1);
            Grid.SetRowSpan(_wxEmpty, 3);
            inner.Children.Add(_wxEmpty);

            card.Child = inner;
            return card;
        }

        /// <summary>
        /// 面板大字保留紧致投影防透光；小字（<=13px）禁用模糊与位图缓存，启用 ClearType 保持笔画锐利不重影。
        /// </summary>
        private static void SoftShadow(TextBlock tb, double opacity)
        {
            if (tb == null) return;
            if (tb.FontSize <= 13)
            {
                TextOptions.SetTextFormattingMode(tb, TextFormattingMode.Display);
                TextOptions.SetTextRenderingMode(tb, TextRenderingMode.ClearType);
                tb.Effect = null;
                tb.CacheMode = null;
                return;
            }
            tb.Effect = new DropShadowEffect
            {
                Color = Color.FromRgb(0x03, 0x06, 0x0C),
                BlurRadius = 3,
                ShadowDepth = 1.0,
                Direction = 270,
                Opacity = opacity,
                RenderingBias = RenderingBias.Performance
            };
            tb.CacheMode = new BitmapCache();
        }

        private Border BuildMetricCard(double x, double y, double w, double h,
            string caption, string tag, out TextBlock value, out TextBlock sub, out GlassBar bar, Color accent)
        {
            var card = GlassStyle.Tile(18, 26);
            card.Width = w;
            card.Height = h;
            Canvas.SetLeft(card, x);
            Canvas.SetTop(card, y);

            var inner = new Grid { Margin = new Thickness(14, 11, 14, 11) };
            inner.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            inner.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            inner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var cap = GlassStyle.Label(caption, 11.5, Color.FromArgb(196, 0xB6, 0xC8, 0xE8));
            SoftShadow(cap, 0.4);
            Grid.SetColumn(cap, 0);
            head.Children.Add(cap);

            var tagText = new TextBlock
            {
                Text = tag,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(200, accent.R, accent.G, accent.B))
            };
            SoftShadow(tagText, 0.3);
            Grid.SetColumn(tagText, 1);
            head.Children.Add(tagText);
            Grid.SetRow(head, 0);
            inner.Children.Add(head);

            var valueRow = new Grid { Margin = new Thickness(0, 3, 0, 0) };
            valueRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            valueRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            value = new TextBlock
            {
                Text = "--",
                FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"),
                FontSize = 23,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xF6, 0xF9, 0xFF))
            };
            SoftShadow(value, 0.55);
            Grid.SetColumn(value, 0);
            valueRow.Children.Add(value);

            sub = new TextBlock
            {
                Text = "",
                FontSize = 10.5,
                Foreground = new SolidColorBrush(Color.FromArgb(178, 0xAA, 0xBE, 0xDE)),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 2)
            };
            SoftShadow(sub, 0.3);
            Grid.SetColumn(sub, 1);
            valueRow.Children.Add(sub);
            Grid.SetRow(valueRow, 1);
            inner.Children.Add(valueRow);

            bar = new GlassBar(accent) { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 0) };
            Grid.SetRow(bar, 2);
            inner.Children.Add(bar);

            card.Child = inner;
            return card;
        }

        /// <summary>设备电量中心里的一个槽位。</summary>
        private sealed class BcSlot
        {
            public StackPanel Root;
            public Ellipse Empty;
            public Ellipse Base;
            public System.Windows.Shapes.Path Arc;
            public TextBlock Glyph;
            public Border Pc;
            public TextBlock Pct;
            public TextBlock NameText;
        }

        /// <summary>设备电量中心：标题行 + 四个设备槽（本机 / 已连接的蓝牙耳机），样式对齐手机桌面的电量卡片。</summary>
        private Border BuildBatteryCenter(double x, double y, double w, double h)
        {
            var card = GlassStyle.Tile(16, 26);
            card.Width = w;
            card.Height = h;
            Canvas.SetLeft(card, x);
            Canvas.SetTop(card, y);

            var inner = new Grid { Margin = new Thickness(12, 3, 12, 3) };
            inner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(12) });
            inner.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // 标题行：绿色小电池 + 「设备电量」 + 右侧「更多 ›」
            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var batIcon = new Grid { Width = 16, Height = 9, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            batIcon.Children.Add(new Border
            {
                Width = 12,
                Height = 8,
                CornerRadius = new CornerRadius(2),
                BorderThickness = new Thickness(1.2),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x2B, 0xD9, 0x6B)),
                HorizontalAlignment = HorizontalAlignment.Left
            });
            batIcon.Children.Add(new Border
            {
                Width = 2,
                Height = 3,
                CornerRadius = new CornerRadius(1),
                Background = new SolidColorBrush(Color.FromRgb(0x2B, 0xD9, 0x6B)),
                HorizontalAlignment = HorizontalAlignment.Right
            });
            head.Children.Add(batIcon);

            var title = new TextBlock
            {
                Text = "设备电量",
                FontSize = 11.5,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromArgb(238, 0xE8, 0xF1, 0xFF))
            };
            SoftShadow(title, 0.4);
            Grid.SetColumn(title, 1);
            head.Children.Add(title);

            _bcMore = new TextBlock
            {
                Text = "更多 ›",
                FontSize = 10.5,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromArgb(206, 0xB2, 0xC6, 0xE8)),
                Cursor = Cursors.Hand,
                Focusable = true
            };
            SoftShadow(_bcMore, 0.3);
            _bcMore.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                if (_btConnected != null && _btConnected.Count > 3)
                {
                    _bcPageOffset = (_bcPageOffset + 1) % _btConnected.Count;
                    RefreshBatteryCenter();
                }
                else
                {
                    OpenBluetoothSettings();
                }
            };
            _bcMore.MouseRightButtonUp += delegate (object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                OpenBluetoothSettings();
            };
            _bcMore.KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.Key == Key.Enter || e.Key == Key.Space)
                {
                    e.Handled = true;
                    if (_btConnected != null && _btConnected.Count > 3)
                    {
                        _bcPageOffset = (_bcPageOffset + 1) % _btConnected.Count;
                        RefreshBatteryCenter();
                    }
                    else
                    {
                        OpenBluetoothSettings();
                    }
                }
            };
            Grid.SetColumn(_bcMore, 3);
            head.Children.Add(_bcMore);

            // 「读取」：只走系统公开属性 / 标准 GATT，不打断音频、不调用会 Release HFP 的辅助工具。
            _bcRead = new TextBlock
            {
                Text = "读取",
                FontSize = 10.5,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
                Foreground = new SolidColorBrush(Color.FromArgb(220, 0x2B, 0xD9, 0x6B)),
                Cursor = Cursors.Hand,
                ToolTip = "从系统已有电量属性刷新（不会断开耳机或中断播放）",
                Focusable = true
            };
            SoftShadow(_bcRead, 0.3);
            _bcRead.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                RequestSystemBatteryRead(true);
            };
            _bcRead.KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.Key == Key.Enter || e.Key == Key.Space)
                {
                    e.Handled = true;
                    RequestSystemBatteryRead(true);
                }
            };
            Grid.SetColumn(_bcRead, 2);
            head.Children.Add(_bcRead);

            Grid.SetRow(head, 0);
            inner.Children.Add(head);

            // 四个槽位
            LoadBatteryCache();
            _bcSlots.Clear();
            var row = new Grid();
            for (int i = 0; i < 4; i++)
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            for (int i = 0; i < 4; i++)
            {
                var slot = new BcSlot();
                var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

                var holder = new Grid { Width = 32, Height = 32, HorizontalAlignment = HorizontalAlignment.Center };
                slot.Empty = new Ellipse
                {
                    Width = 28,
                    Height = 28,
                    Fill = new SolidColorBrush(Color.FromArgb(40, 0xDC, 0xE6, 0xF6)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                slot.Base = new Ellipse
                {
                    Width = 27,
                    Height = 27,
                    Stroke = new SolidColorBrush(Color.FromArgb(70, 0xEC, 0xF3, 0xFF)),
                    StrokeThickness = 2.4,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                slot.Arc = new System.Windows.Shapes.Path
                {
                    Stroke = new SolidColorBrush(Color.FromRgb(0x2B, 0xD9, 0x6B)),
                    StrokeThickness = 2.4,
                    StrokeLineJoin = PenLineJoin.Round,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    IsHitTestVisible = false
                };
                slot.Glyph = new TextBlock
                {
                    FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromArgb(235, 0xE6, 0xEF, 0xFF)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                // 本机用小手机形状画，不依赖字体字形，精确居中
                slot.Pc = new Border
                {
                    Width = 7.5,
                    Height = 12,
                    CornerRadius = new CornerRadius(2),
                    BorderThickness = new Thickness(1.1),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(235, 0xE6, 0xEF, 0xFF)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                holder.Children.Add(slot.Empty);
                holder.Children.Add(slot.Base);
                holder.Children.Add(slot.Arc);
                holder.Children.Add(slot.Glyph);
                holder.Children.Add(slot.Pc);
                stack.Children.Add(holder);

                slot.Pct = new TextBlock
                {
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xF2, 0xF7, 0xFF)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 1, 0, 0)
                };
                TextOptions.SetTextFormattingMode(slot.Pct, TextFormattingMode.Display);
                TextOptions.SetTextRenderingMode(slot.Pct, TextRenderingMode.ClearType);
                stack.Children.Add(slot.Pct);

                slot.NameText = new TextBlock
                {
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromArgb(215, 0xC4, 0xD8, 0xF0)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 0),
                    Width = 78,
                    TextAlignment = TextAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    LineHeight = 12
                };
                TextOptions.SetTextFormattingMode(slot.NameText, TextFormattingMode.Display);
                TextOptions.SetTextRenderingMode(slot.NameText, TextRenderingMode.ClearType);
                stack.Children.Add(slot.NameText);

                if (i >= 1)
                {
                    stack.Cursor = Cursors.Hand;
                    stack.MouseLeftButtonUp += delegate (object s, MouseButtonEventArgs e)
                    {
                        e.Handled = true;
                        RequestSystemBatteryRead(true);
                    };
                }
                slot.Root = stack;
                Grid.SetColumn(stack, i);
                row.Children.Add(stack);
                _bcSlots.Add(slot);
            }

            Grid.SetRow(row, 1);
            inner.Children.Add(row);

            card.Child = inner;
            return card;
        }

        /// <summary>填一个槽位：occupied=false 就是图里那种空的灰圆；电量未知时只留底环、写「未上报」。</summary>
        private void SetBcSlot(BcSlot s, bool isPc, double pct, bool known, bool occupied, string name, string glyph = "\uE7F6")
        {
            if (s == null) return;

            s.Empty.Visibility = occupied ? Visibility.Collapsed : Visibility.Visible;
            s.Base.Visibility = occupied ? Visibility.Visible : Visibility.Collapsed;
            s.Glyph.Visibility = (occupied && !isPc) ? Visibility.Visible : Visibility.Collapsed;
            s.Pc.Visibility = (occupied && isPc) ? Visibility.Visible : Visibility.Collapsed;

            if (s.NameText != null) s.NameText.Text = occupied ? (name ?? "") : "";

            if (!occupied)
            {
                s.Arc.Data = null;
                s.Pct.Text = "";
                return;
            }

            s.Glyph.Text = string.IsNullOrEmpty(glyph) ? "\uE7F6" : glyph;

            if (!known)
            {
                s.Arc.Data = null;
                s.Pct.Text = "未上报";
                return;
            }

            double p = pct < 0 ? 0 : (pct > 100 ? 100 : pct);
            var c = new Point(16, 16);
            const double r = 13.5;
            if (p >= 99.5)
            {
                s.Arc.Data = new EllipseGeometry(c, r, r);
            }
            else
            {
                double rad = Math.PI / 180;
                double a0 = -90, a1 = -90 + 360 * p / 100;
                var fig = new PathFigure
                {
                    StartPoint = new Point(c.X + r * Math.Cos(a0 * rad), c.Y + r * Math.Sin(a0 * rad))
                };
                fig.Segments.Add(new ArcSegment
                {
                    Point = new Point(c.X + r * Math.Cos(a1 * rad), c.Y + r * Math.Sin(a1 * rad)),
                    Size = new Size(r, r),
                    SweepDirection = SweepDirection.Clockwise,
                    IsLargeArc = p > 50
                });
                var geo = new PathGeometry();
                geo.Figures.Add(fig);
                s.Arc.Data = geo;
            }
            s.Pct.Text = ((int)Math.Round(p)) + "%";
        }

        /// <summary>数据目录：跟程序放在一起（exe 所在目录\data），不写 C 盘、不写注册表。</summary>
        private static string DataDir
        {
            get
            {
                try
                {
                    string dir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
                    System.IO.Directory.CreateDirectory(dir);
                    return dir;
                }
                catch
                {
                    // 只读介质等极端情况兜底：程序自己所在目录也写不进去时，退回内存态（不落盘）
                    return null;
                }
            }
        }

        private static string BatteryCachePath
        {
            get
            {
                string dir = DataDir;
                return dir == null ? null : System.IO.Path.Combine(dir, "btbattery.txt");
            }
        }

        private void LoadBatteryCache()
        {
            try
            {
                string f = BatteryCachePath;
                if (f == null || !System.IO.File.Exists(f)) return;
                foreach (string line in System.IO.File.ReadAllLines(f))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    ulong addr = Convert.ToUInt64(line.Substring(0, eq).Trim(), 16);
                    string[] parts = line.Substring(eq + 1).Trim().Split(';');
                    int v = int.Parse(parts[0]);
                    if (v >= 0 && v <= 100) _btBattKnown[addr] = v;
                    if (parts.Length >= 4)
                    {
                        var arr = new int[3];
                        for (int k = 0; k < 3; k++) int.TryParse(parts[k + 1], out arr[k]);
                        _btBattParts[addr] = arr;
                    }
                    if (parts.Length >= 5)
                    {
                        DateTime ts;
                        if (DateTime.TryParse(parts[4], out ts)) _btBattTimestamp[addr] = ts;
                    }
                }
            }
            catch { }
        }

        private void SaveBatteryCache()
        {
            try
            {
                string f = BatteryCachePath;
                if (f == null) return;
                var sb = new System.Text.StringBuilder();
                foreach (var kv in _btBattKnown)
                {
                    sb.Append(kv.Key.ToString("X12")).Append('=').Append(kv.Value);
                    int[] parts = null;
                    if (_btBattParts.TryGetValue(kv.Key, out parts) && parts != null)
                        sb.Append(';').Append(parts[0]).Append(';').Append(parts[1]).Append(';').Append(parts[2]);
                    else
                        sb.Append(";-1;-1;-1");
                    
                    DateTime ts;
                    if (_btBattTimestamp.TryGetValue(kv.Key, out ts))
                        sb.Append(';').Append(ts.ToString("s"));
                    sb.Append('\n');
                }
                System.IO.File.WriteAllText(f, sb.ToString());
            }
            catch { }
        }
        /// <summary>
        /// 手动或自动刷新电量：只查询系统公开属性与标准 GATT 电池服务，
        /// 不会启动 BtBattery.exe，也不会禁用/启用 HFP 或其他音频服务。
        /// </summary>
        private void RequestSystemBatteryRead(bool force)
        {
            if (force)
            {
                _btDeepProbed.Clear();
                _btBattRetryAt.Clear();
                _btLastActiveProbeAt.Clear();
                Bluetooth.ClearProbeCache(0);
            }
            var list = new List<BtDevice>();
            if (_btConnected != null)
            {
                foreach (var d in _btConnected)
                    if (d != null && d.IsListable) list.Add(d);
            }
            if (list.Count == 0 && _btAudio != null) list.Add(_btAudio);
            if (force && _bcRead != null) _bcRead.Text = "读取中";
            ScheduleBatteryQueries(list, force);
        }

        private static double BatteryBackoffSeconds(int failCount)
        {
            if (failCount <= 0) return 20;
            if (failCount == 1) return 45;
            if (failCount == 2) return 120;
            return 300;
        }

        private void BackfillBatteryCache(BtDevice d)
        {
            if (d == null) return;

            // 如果本次枚举（如 SetupAPI PnP 节点直读）已经拿到了新鲜电量，立即同步进缓存并持久化
            if (d.HasBattery && d.QueryStatus == BatteryQueryStatus.Fresh)
            {
                DateTime now = d.BatterySampledAt != DateTime.MinValue ? d.BatterySampledAt : DateTime.Now;
                d.BatterySampledAt = now;
                bool changed = false;
                int oldVal;
                if (!_btBattKnown.TryGetValue(d.Address, out oldVal) || oldVal != d.Battery)
                    changed = true;
                _btBattKnown[d.Address] = d.Battery;
                _btBattTimestamp[d.Address] = now;
                _btBattStatus[d.Address] = BatteryQueryStatus.Fresh;
                _btBattFailCount[d.Address] = 0;

                if (d.HasSplit)
                {
                    _btBattParts[d.Address] = new int[] { d.BatteryLeft, d.BatteryRight, d.BatteryCase };
                }
                else
                {
                    int[] cachedParts;
                    if (_btBattParts.TryGetValue(d.Address, out cachedParts) && cachedParts != null && cachedParts.Length >= 3)
                    {
                        d.BatteryLeft = cachedParts[0];
                        d.BatteryRight = cachedParts[1];
                        d.BatteryCase = cachedParts[2];
                    }
                }
                if (changed)
                {
                    try { SaveBatteryCache(); } catch { }
                }
                return;
            }

            int known;
            if (d.Battery < 0 && _btBattKnown.TryGetValue(d.Address, out known) && known >= 0 && known <= 100)
                d.Battery = known;
            int[] parts = null;
            if (_btBattParts.TryGetValue(d.Address, out parts) && parts != null && d.BatteryLeft < 0)
            {
                d.BatteryLeft = parts[0];
                d.BatteryRight = parts[1];
                d.BatteryCase = parts[2];
            }
            DateTime ts;
            if (_btBattTimestamp.TryGetValue(d.Address, out ts) && ts != DateTime.MinValue)
            {
                d.BatterySampledAt = ts;
                if (d.HasBattery)
                    d.QueryStatus = (DateTime.Now - ts).TotalMinutes < BatteryFreshMinutes
                        ? BatteryQueryStatus.Fresh
                        : BatteryQueryStatus.Stale;
                else if (d.QueryStatus == BatteryQueryStatus.None)
                    d.QueryStatus = BatteryQueryStatus.NoSystemValue;
            }
            else if (d.HasBattery)
            {
                d.QueryStatus = BatteryQueryStatus.Stale;
            }
            BatteryQueryStatus saved;
            if (_btBattStatus.TryGetValue(d.Address, out saved) && !d.HasBattery)
                d.QueryStatus = saved;
        }

        private static BtDevice CloneForQuery(BtDevice d)
        {
            var c = new BtDevice();
            c.Name = d.Name;
            c.Address = d.Address;
            c.Connected = d.Connected;
            c.ClassOfDevice = d.ClassOfDevice;
            c.IsBle = d.IsBle;
            c.IsClassic = d.IsClassic;
            c.ContainerId = d.ContainerId;
            c.AepId = d.AepId;
            c.CategoryHint = d.CategoryHint;
            c.Battery = d.Battery;
            c.BatteryLeft = d.BatteryLeft;
            c.BatteryRight = d.BatteryRight;
            c.BatteryCase = d.BatteryCase;
            c.BatterySampledAt = d.BatterySampledAt;
            c.QueryStatus = d.QueryStatus;
            c.QuerySource = d.QuerySource;
            return c;
        }

        private void ApplyQueryResult(BtDevice live, BtDevice result, DateTime done)
        {
            if (live == null || result == null) return;
            _btDeepProbed[live.Address] = true;
            _btLastActiveProbeAt[live.Address] = done;
            if (result.HasBattery && result.QueryStatus == BatteryQueryStatus.Fresh)
            {
                live.Battery = result.Battery;
                live.BatteryLeft = result.BatteryLeft;
                live.BatteryRight = result.BatteryRight;
                live.BatteryCase = result.BatteryCase;
                live.BatterySampledAt = result.BatterySampledAt != DateTime.MinValue ? result.BatterySampledAt : done;
                live.QueryStatus = BatteryQueryStatus.Fresh;
                live.QuerySource = result.QuerySource;
                if (!string.IsNullOrEmpty(result.CategoryHint) && string.IsNullOrEmpty(live.CategoryHint))
                    live.CategoryHint = result.CategoryHint;
                if (!string.IsNullOrEmpty(result.ContainerId) && string.IsNullOrEmpty(live.ContainerId))
                    live.ContainerId = result.ContainerId;
                _btBattKnown[live.Address] = live.Battery;
                _btBattTimestamp[live.Address] = live.BatterySampledAt;
                _btBattStatus[live.Address] = BatteryQueryStatus.Fresh;
                _btBattFailCount[live.Address] = 0;
                if (live.HasSplit)
                    _btBattParts[live.Address] = new int[] { live.BatteryLeft, live.BatteryRight, live.BatteryCase };
                try { SaveBatteryCache(); } catch { }
                return;
            }

            // 如果 live 自身已经带有枚举阶段拿到的新鲜电量，不要被后台兜底失败冲掉
            if (live.HasBattery && live.QueryStatus == BatteryQueryStatus.Fresh)
                return;

            int fails = 0;
            _btBattFailCount.TryGetValue(live.Address, out fails);
            _btBattFailCount[live.Address] = fails + 1;
            BatteryQueryStatus st = result.QueryStatus;
            if (st == BatteryQueryStatus.None || st == BatteryQueryStatus.Querying)
                st = BatteryQueryStatus.Failed;
            int known;
            if (!result.HasBattery && _btBattKnown.TryGetValue(live.Address, out known) && known >= 0)
            {
                live.Battery = known;
                st = BatteryQueryStatus.Stale;
                DateTime ts;
                if (_btBattTimestamp.TryGetValue(live.Address, out ts))
                    live.BatterySampledAt = ts;
            }
            else if (result.HasBattery)
            {
                live.Battery = result.Battery;
                live.BatterySampledAt = result.BatterySampledAt;
            }
            live.QueryStatus = st;
            live.QuerySource = result.QuerySource;
            _btBattStatus[live.Address] = st;
        }

        private BtDevice FindLiveDevice(ulong address)
        {
            if (_btConnected != null)
            {
                for (int i = 0; i < _btConnected.Count; i++)
                    if (_btConnected[i] != null && _btConnected[i].Address == address)
                        return _btConnected[i];
            }
            if (_btCardDevice != null && _btCardDevice.Address == address) return _btCardDevice;
            if (_btAudio != null && _btAudio.Address == address) return _btAudio;
            return null;
        }

        private void ScheduleBatteryQueries(List<BtDevice> devices, bool force)
        {
            if (devices == null || devices.Count == 0) return;
            var targets = new List<BtDevice>();
            DateTime now = DateTime.Now;
            for (int i = 0; i < devices.Count; i++)
            {
                BtDevice d = devices[i];
                if (d == null || !d.Connected) continue;
                if (!force)
                {
                    // 自动轮询阶段：凡是已经在本次连接中探测过一次（_btDeepProbed），或者已有 PnP 内核电量且正在播放音频（AudioTap.Signal / MediaSession.Playing），
                    // 绝不再自动发起 RFCOMM / GATT 无线连接，100% 依赖每 3 秒一次的 SetupAPI 纯内存 PnP 属性更新，彻底杜绝耳机断音与卡顿！
                    if (_btDeepProbed.ContainsKey(d.Address))
                        continue;
                    if (d.HasBattery && (AudioTap.Signal || MediaSession.Playing))
                        continue;
                    DateTime last;
                    if (_btBattRetryAt.TryGetValue(d.Address, out last))
                    {
                        int fails = 0;
                        _btBattFailCount.TryGetValue(d.Address, out fails);
                        if ((now - last).TotalSeconds < BatteryBackoffSeconds(fails))
                            continue;
                    }
                }
                if (!d.HasBattery)
                {
                    d.QueryStatus = BatteryQueryStatus.Querying;
                    _btBattStatus[d.Address] = BatteryQueryStatus.Querying;
                }
                _btBattRetryAt[d.Address] = now;
                targets.Add(CloneForQuery(d));
            }
            if (targets.Count == 0)
            {
                if (_btBattInFlight <= 0 && _bcRead != null) _bcRead.Text = "读取";
                RefreshBatteryCenter();
                return;
            }

            bool anyMissingBattery = false;
            for (int i = 0; i < targets.Count; i++)
            {
                if (!targets[i].HasBattery) { anyMissingBattery = true; break; }
            }
            if ((force || anyMissingBattery) && _bcRead != null)
                _bcRead.Text = "读取中";
            if (force || anyMissingBattery)
                RefreshBatteryCenter();

            System.Threading.Interlocked.Increment(ref _btBattInFlight);
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    Bluetooth.FillBatteries(targets, 8000, true);
                }
                catch { }
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    System.Threading.Interlocked.Decrement(ref _btBattInFlight);
                    DateTime done = DateTime.Now;
                    for (int i = 0; i < targets.Count; i++)
                    {
                        BtDevice result = targets[i];
                        if (result == null) continue;
                        BtDevice live = FindLiveDevice(result.Address);
                        if (live != null) ApplyQueryResult(live, result, done);
                        else ApplyQueryResult(result, result, done);
                    }
                    if (_bcRead != null) _bcRead.Text = "读取";
                    if (_btCardOn)
                    {
                        BtDevice cardTarget = _btCardDevice != null ? (FindLiveDevice(_btCardDevice.Address) ?? _btCardDevice) : _btAudio;
                        if (cardTarget != null) SetBtBatteryText(cardTarget);
                    }
                    RefreshBatteryCenter();
                }));
            });
        }

        /// <summary>刷新电量中心：槽1 本机，后面依次是已连接的可列出蓝牙设备（支持多副耳机、键鼠等；超过三台时点击右上角可轮换查看全部设备）。</summary>
        private void RefreshBatteryCenter()
        {
            if (_bcSlots == null || _bcSlots.Count < 4) return;

            bool ac, pcKnown;
            int pcPct = Native.BatteryPercentEx(out ac, out pcKnown);
            if (_hasBattery) SetBcSlot(_bcSlots[0], true, pcPct, pcKnown, true, pcKnown ? "本机" : "本机 · 读取中");
            else
            {
                SetBcSlot(_bcSlots[0], true, 0, false, true, "本机 · 外接电源");
            }

            int connectedCount = _btConnected == null ? 0 : _btConnected.Count;
            if (connectedCount <= 3) _bcPageOffset = 0;
            else if (_bcPageOffset >= connectedCount) _bcPageOffset = 0;

            for (int i = 1; i < 4; i++)
            {
                BtDevice d = null;
                if ((i - 1) < connectedCount)
                {
                    int idx = connectedCount > 3 ? ((_bcPageOffset + (i - 1)) % connectedCount) : (i - 1);
                    d = _btConnected[idx];
                }
                if (d == null) SetBcSlot(_bcSlots[i], false, 0, false, false, "");
                else ApplyDeviceBatterySlot(_bcSlots[i], d);
            }
            if (_bcMore != null)
            {
                if (connectedCount > 3)
                {
                    _bcMore.Text = "切换 (" + (_bcPageOffset + 1) + "/" + connectedCount + ") ›";
                    _bcMore.ToolTip = "左键切换查看其余已连接设备；右键打开系统蓝牙设置";
                }
                else
                {
                    _bcMore.Text = "更多 ›";
                    _bcMore.ToolTip = "打开 Windows 蓝牙与设备设置";
                }
            }
        }

        private static bool BatteryLooksStale(BtDevice d)
        {
            if (d == null || !d.HasBattery) return false;
            if (d.QueryStatus == BatteryQueryStatus.Stale) return true;
            if (d.QueryStatus == BatteryQueryStatus.Fresh) return false;
            if (d.BatterySampledAt == DateTime.MinValue) return true;
            return (DateTime.Now - d.BatterySampledAt).TotalMinutes >= BatteryFreshMinutes;
        }

        private void ApplyDeviceBatterySlot(BcSlot slot, BtDevice d)
        {
            string glyph = "\uE7F6";
            if (d.IsKeyboard) glyph = "\uE765";
            else if (d.IsMouse) glyph = "\uE962";
            else if (d.IsPeripheral) glyph = "\uE772";
            bool stale = BatteryLooksStale(d);
            SetBcSlot(slot, false, d.HasBattery ? d.Battery : 0, d.HasBattery && !stale, true, d.Name, glyph);
            if (slot.Pct == null) return;

            if (d.HasBattery && stale)
            {
                slot.Pct.Text = "上次 " + d.Battery + "%";
                slot.Pct.FontSize = 9.5;
                slot.Arc.Data = null;
            }
            else if (!d.HasBattery)
            {
                string st = Bluetooth.StatusText(d.QueryStatus);
                slot.Pct.Text = string.IsNullOrEmpty(st) ? "未上报" : st;
                slot.Pct.FontSize = st != null && st.Length >= 4 ? 8.5 : 10;
            }

            if (d.HasSplit && d.HasBattery && !stale)
            {
                slot.Pct.Text = d.BatteryLeft + " / " + d.BatteryRight;
                slot.Pct.FontSize = 9.5;
            }
            if (d.HasSplit && d.BatteryCase > 0 && slot.NameText != null)
            {
                string baseName = string.IsNullOrEmpty(d.Name) ? "" : (" · " + d.Name);
                slot.NameText.Text = "盒" + d.BatteryCase + "%" + baseName;
            }

            string tip = (d.Name ?? "蓝牙设备") + " · " + (d.HasBattery ? (d.Battery + "%") : Bluetooth.StatusText(d.QueryStatus));
            if (stale) tip = tip + " · 历史读数，非当前值";
            if (d.HasSplit)
            {
                tip = (d.Name ?? "蓝牙耳机") + " · 左耳 " + d.BatteryLeft + "%　右耳 " + d.BatteryRight + "%" +
                      (d.BatteryCase > 0 ? ("　充电盒 " + d.BatteryCase + "%") : "");
            }
            if (!string.IsNullOrEmpty(d.QuerySource)) tip = tip + " · " + d.QuerySource;
            slot.Pct.ToolTip = tip;
            if (slot.NameText != null) slot.NameText.ToolTip = tip;
            if (slot.Root != null) slot.Root.ToolTip = tip;
        }
        // ------------------------------------------------------------------
        //  Springs & geometry
        // ------------------------------------------------------------------
        private void BuildSprings()
        {
            _sw = new Spring(v => { _cw = v; }, 880, 58, 0.15);
            _sh = new Spring(v => { _ch = v; }, 880, 58, 0.15);
            _sr = new Spring(v => { _cr = v; }, 640, 48, 0.15);
            _shover = new Spring(v => { _hoverT = v; }, 380, 34, 0.05);
            _sglow = new Spring(v => { _glowT = v; }, 220, 26);
            // 蓝牙卡专用的一组长弹簧：比展开/收起更软（刚度 360/300），
            // 胶囊长成卡片是「伸展开」而不是「被弹开」，收回也是稳稳压回去，不会一顿一顿。
            _sbw = new Spring(v => { _cw = v; }, 360, 38, 0.15);
            _sbh = new Spring(v => { _ch = v; }, 300, 35, 0.15);
            _sbr = new Spring(v => { _cr = v; }, 360, 38, 0.15);
            _sw.Set(CollapsedW);
            _sh.Set(CollapsedH);
            _sr.Set(CollapsedR);
            _sbw.Set(CollapsedW);
            _sbh.Set(CollapsedH);
            _sbr.Set(CollapsedR);
            _shover.Set(0);
            _sglow.Set(0);
        }

        /// <summary>用渲染帧回调驱动弹簧，保证每帧最多合成一次，和 vsync 对齐。</summary>
        private bool _frameHooked;

        private void KickAnimation()
        {
            if (_frameHooked) return;
            _animClock.Reset();
            _animClock.Start();
            _frameHooked = true;
            CompositionTarget.Rendering += OnFrame;
        }

        private void OnFrame(object sender, EventArgs e)
        {
            double dt = _animClock.Elapsed.TotalSeconds;
            // 攒够一帧再推弹簧。阈值定在 3ms：
            // 60Hz（16.7ms）、120Hz（8.3ms）、144Hz（6.9ms）、240Hz（4.2ms）的显示器
            // 每帧都只走一步，刷新率越高动画越细，不用改代码。
            // 旧版写死 8ms，在 120Hz 上刚好卡在门槛上，会隔帧丢掉一步，看起来是「一顿一顿」。
            if (dt < 0.003) return;
            _animClock.Reset();
            _animClock.Start();
            if (dt > 0.033) dt = 0.033;

            _sw.Step(dt);
            _sh.Step(dt);
            _sr.Step(dt);
            _sbw.Step(dt);
            _sbh.Step(dt);
            _sbr.Step(dt);
            _shover.Step(dt);
            _sglow.Step(dt);

            ApplyGeometry();

            if (Environment.GetEnvironmentVariable("ISLAND_ANIMLOG") != null)
            {
                try
                {
                    System.IO.File.AppendAllText(
                        Environment.GetEnvironmentVariable("ISLAND_ANIMLOG"),
                        (dt * 1000.0).ToString("F1") + "\n");
                }
                catch { }
            }

            if (!_sw.IsActive && !_sh.IsActive && !_sr.IsActive
                && !_sbw.IsActive && !_sbh.IsActive && !_sbr.IsActive
                && !_shover.IsActive && !_sglow.IsActive)
            {
                CompositionTarget.Rendering -= OnFrame;
                _frameHooked = false;
                // 落位后硬清一次展开层：否则它的淡出动画 HoldEnd 会留下一点残影，
                // 看起来就是“缩回去时抽一下、还带着鬼影”
                if (!_expanded)
                {
                    _expandedLayer.BeginAnimation(OpacityProperty, null);
                    _expandedLayer.Opacity = 0;
                    if (_expScale != null) _expScale.ScaleX = _expScale.ScaleY = 1;
                    if (_expShift != null) _expShift.Y = 0;
                }
                if (_pendingShrink)
                {
                    _pendingShrink = false;
                    FitWindow(CollapsedW, CollapsedH);
                    ApplyGeometry();
                }
                else if (_btPendingShrink)
                {
                    _btPendingShrink = false;
                    FitWindow(CollapsedW, CollapsedH);
                    // 卡片收完，把通用弹簧就地同步到胶囊尺寸，
                    // 之后展开/收起直接接管，不会因为弹簧内部还停在 330 而再弹一下
                    _sw.Set(CollapsedW); _sh.Set(CollapsedH); _sr.Set(CollapsedR);
                    _sbw.Set(CollapsedW); _sbh.Set(CollapsedH); _sbr.Set(CollapsedR);
                    if (_btLayer != null) _btLayer.Opacity = 0;
                    ApplyGeometry();
                }
            }
        }

        /// <summary>
        /// 蓝牙卡内容的「生长进度」：0=还是胶囊，1=完全长成卡片。
        /// 内容透明度/轻微缩放/上移量全按这个值逐帧算，所以卡片是从胶囊里长出来的，
        /// 不是等壳体长完再「啪」一下贴上。收起时同一套逻辑反向走，不需要另一段动画。
        /// </summary>
        private void ApplyBtReveal()
        {
            if (_btLayer == null) return;

            double t = (_cw - CollapsedW) / (BtCardW - CollapsedW);
            if (t < 0) t = 0; else if (t > 1) t = 1;
            // 用 ease-out 让内容比壳体略微提前浮出来一点，观感更「顺」
            double e = 1 - (1 - t) * (1 - t);

            // 整张卡（含深蓝底）跟着壳体一起淡：壳体长到几成，卡片就实到几成。
            // 底板不单独做位移动画，否则会和亮边错开半个像素。
            // 收起时这一层挂了 Fade 动画（下行 HideBtCard），动画优先级高于这里的逐帧赋值，
            // 所以有动画时不要写 Opacity，否则会互相打架、内容卡在半透明上。
            if (_btContent != null && !_btContent.HasAnimatedProperties) _btContent.Opacity = e;

            // 文字/图标/电量环按壳体宽度等比缩：外框还窄的时候它只有原来的一半大，
            // 这样永远不会被壳体两边裁掉半截字（裁切那一下最显生硬）。
            if (_btInner != null)
            {
                var sc = _btInner.RenderTransform as ScaleTransform;
                if (sc == null)
                {
                    sc = new ScaleTransform(1, 1);
                    _btInner.RenderTransformOrigin = new Point(0.5, 0.5);
                    _btInner.RenderTransform = sc;
                }
                // 缩放到「刚好装得进当前壳体宽度」：留 12px 边距，
                // 版面宽固定是 BtCardW-46，除一下就是此刻该有的比例
                double s = (_cw - 12) / (BtCardW - 46);
                if (s > 1) s = 1;
                if (s < 0.4) s = 0.4;
                sc.ScaleX = s;
                sc.ScaleY = s;
            }
        }

        private void ApplyGeometry()
        {
            if (_body == null) return;
            _cw = Math.Max(1, _cw);
            _ch = Math.Max(1, _ch);

            _body.Width = _cw;
            _body.Height = _ch;
            _glass.CornerRadius = new CornerRadius(_cr);
            _rim.CornerRadius = new CornerRadius(_cr);
            _sheen.CornerRadius = new CornerRadius(_cr);


            var ambClip = (_ambient == null) ? null : (_ambient.Clip as RectangleGeometry);
            if (ambClip != null)
            {
                ambClip.Rect = new Rect(3, 3, Math.Max(1, _cw - 6), Math.Max(1, _ch - 6));
                ambClip.RadiusX = Math.Max(0, _cr - 3);
                ambClip.RadiusY = Math.Max(0, _cr - 3);
            }

            double bodyLeft = (Width - _cw) / 2.0;
            double bodyTop = 58 - _hoverT * 1.6;
            Canvas.SetLeft(_bodyHost, bodyLeft);
            Canvas.SetTop(_bodyHost, bodyTop);

            // 面板/胶囊的内容在屏幕坐标系里钉住：壳体从中间长出来，内容不跟着每帧重新居中。
            // 之前用 Center 对齐，尺寸每变一帧内容就做一次亚像素位移，文字看着在抖。
            if (_expandedLayer != null)
            {
                _expandedLayer.HorizontalAlignment = HorizontalAlignment.Left;
                _expandedLayer.Margin = new Thickness(Math.Round((_cw - ExpandedW) / 2.0), 0, 0, 0);

                // 兜底：壳体已经缩到不足面板 6 成（肯定进不了胶囊）就直接不画面板内容。
                // 光靠淡出压不住——动画一旦慢一拍，屏幕上就是半截大字压在胶囊上。
                if (!_expanded && _ch < ExpandedH * 0.62 && _expandedLayer.Visibility != Visibility.Collapsed)
                {
                    _expandedLayer.Visibility = Visibility.Collapsed;
                }
            }
            if (_collapsedLayer != null)
            {
                _collapsedLayer.HorizontalAlignment = HorizontalAlignment.Left;
                _collapsedLayer.Margin = new Thickness(Math.Round((_cw - CollapsedW) / 2.0), 0, 0, 0);
            }

            _specularLayer.Width = _cw;
            _specularLayer.Height = _ch;
            Canvas.SetLeft(_specular, -80);
            Canvas.SetTop(_specular, -40);


            if (_veil != null)
            {
                double grow = Math.Min(1.0, Math.Max(0.0, (_ch - CollapsedH) / 160.0));
                _veil.Opacity = (_solarEnabled ? 0.0 : 0.28) * grow;
                _veil.CornerRadius = new CornerRadius(_cr);
            }

            if (_liquid != null)
            {
                _liquid.CornerRadius = new CornerRadius(_cr);
                _liquidEdge.CornerRadius = new CornerRadius(_cr);
            }
            _rim.Opacity = 0.55 + _hoverT * 0.25;   // 玻璃轮廓：整圈细亮边

            // 蓝牙卡：变形过程中内容也跟着当前岛体轮廓裁。
            // 卡片是固定 330×76 的一层，岛体只有 198×42 时如果整层都画出来，
            // 就会看到「一小条卡片浮在胶囊上」——这就是之前那个图层分离。
            if (_btLayer != null)
            {
                // 卡片自己的圆角要跟着壳体实时走。固定 28 的话，壳体半径还在 21→28 之间长的时候，
                // 卡片四个角比壳体更「方」，边上就会露出一条缝 —— 又是那副两层皮的样子。
                if (_btScrim != null) _btScrim.CornerRadius = new CornerRadius(_cr);
                if (_btHover != null) _btHover.CornerRadius = new CornerRadius(_cr);
                if (_btCardOn || _btPendingShrink)
                {
                    var geo = _btLayer.Clip as RectangleGeometry;
                    if (geo == null)
                    {
                        geo = new RectangleGeometry();
                        _btLayer.Clip = geo;
                    }
                    // 卡片已经和外框同尺寸了，裁切只要跟住外框的圆角，
                    // 不能再用固定 330×76 去反推偏移（那样中途必然错位）。
                    geo.Rect = new Rect(0, 0, Math.Max(1, _cw), Math.Max(1, _ch));
                    geo.RadiusX = Math.Max(0, _cr);
                    geo.RadiusY = Math.Max(0, _cr);
                    ApplyBtReveal();
                }
                else if (_btLayer.Clip != null)
                {
                    _btLayer.Clip = null;
                }
            }

            // 窗口形状跟着「这一状态的目标几何」走，不再逐帧 SetWindowRgn。
            // 逐帧改窗口区域等于每帧让系统重新裁一次窗口：区域是整数像素、
            // 而画面是亚像素抗锯齿，边缘会一格一格跳，还伴随掉帧——看着就是展开时在抖。
            // 现在只在状态切换时设一次，动画本身就完全在 WPF 内部完成。
            UpdateBodyRegion(bodyTop);

            // 氛围光的 BlurEffect 会画到元素边界外面去，父级 ClipToBounds 挡不住；
            // 给音乐层套一个圆角 Clip，光晕就只能留在胶囊里，旁边不再有方形底框
            if (_musicLayer != null)
            {
                var clipGeo = new RectangleGeometry(new Rect(0, 0, _cw, CollapsedH), CollapsedR, CollapsedR);
                _musicLayer.Clip = clipGeo;
            }

            if (_geomLogPath != null)
            {
                try
                {
                    System.IO.File.AppendAllText(_geomLogPath,
                        string.Format("{0},{1:F2},{2:F2},{3:F2},{4:F2},{5:F2},{6:F2}\n",
                            Environment.TickCount, _cw, _ch, _cr, _hoverT, bodyLeft, bodyTop));
                }
                catch { }
            }
        }

        private readonly string _geomLogPath = Environment.GetEnvironmentVariable("ISLAND_GEOMLOG");
        private string _regionKey;

        /// <summary>
        /// 窗口可点区域：只按「当前状态的目标尺寸」设一次，动画期间不动。
        /// 收起=胶囊，展开=面板，蓝牙卡=卡片；过渡期间用目标尺寸（区域只大不小，
        /// 正在长的岛体永远落在区域内，所以不会被裁一半）。
        /// </summary>
        private void UpdateBodyRegion(double bodyTop)
        {
            if (_hwnd == IntPtr.Zero) return;

            double tw, th, trad;
            if (_expanded || _pendingShrink) { tw = ExpandedW; th = ExpandedH; trad = ExpandedR; }
            else if (_btCardOn || _btPendingShrink) { tw = BtCardW; th = BtCardH; trad = BtCardR; }
            else { tw = CollapsedW; th = CollapsedH; trad = CollapsedR; }

            double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
            int px = (int)Math.Round(((Width - tw) / 2.0) * dpi);
            int py = (int)Math.Round(bodyTop * dpi);
            int pw = (int)Math.Round(tw * dpi);
            int ph = (int)Math.Round(th * dpi);
            int pr = (int)Math.Round(trad * dpi);
            string key = px + "," + py + "," + pw + "," + ph + "," + pr;
            if (key == _regionKey) return;
            _regionKey = key;

            Native.SetBodyRegion(_hwnd, (Width - tw) / 2.0, bodyTop, tw, th, trad, dpi);
        }

        private bool _hiddenByTray;

        /// <summary>托盘双击：显示或隐藏灵动岛（隐藏后还能从托盘叫回来）。</summary>
        public void ToggleFromTray()
        {
            if (_hiddenByTray || !IsVisible)
            {
                Show();
                _hiddenByTray = false;
                TickClock();
            }
            else
            {
                Hide();
                _hiddenByTray = true;
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_tray != null) { _tray.Dispose(); _tray = null; }
            base.OnClosed(e);
        }
        /// <summary>
        /// 透明但可见的区域不吃鼠标：没命中真正的胶囊/卡片就返回 HTTRANSPARENT，
        /// 交还给下层窗口（浏览器标签栏、任务栏等），贴边时不会挡住别人的顶部栏。
        /// </summary>
        /// <summary>
        /// 光标不在胶囊/卡片实体上时把窗口设成点击穿透：这样贴边时下面的浏览器标签栏、
        /// 任务栏都还能正常点，鼠标一回到胶囊上立刻恢复可点。
        /// </summary>
        private void UpdateClickThrough()
        {
            if (_hwnd == IntPtr.Zero) return;
            Native.POINT pt;
            if (!Native.GetCursorPos(out pt)) return;
            bool inside = false;
            try
            {
                Point p0 = _body.PointToScreen(new Point(0, 0));
                Point p1 = _body.PointToScreen(new Point(_cw, _ch));
                inside = pt.X >= p0.X - 2 && pt.X <= p1.X + 2 && pt.Y >= p0.Y - 2 && pt.Y <= p1.Y + 2;

                if (!inside && _btCardOn && _btLayer != null)
                {
                    double bw = _btLayer.ActualWidth > 0 ? _btLayer.ActualWidth : BtCardW;
                    double bh = _btLayer.ActualHeight > 0 ? _btLayer.ActualHeight : BtCardH;
                    Point pb0 = _btLayer.PointToScreen(new Point(0, 0));
                    Point pb1 = _btLayer.PointToScreen(new Point(bw, bh));
                    inside = pt.X >= pb0.X && pt.X <= pb1.X && pt.Y >= pb0.Y && pt.Y <= pb1.Y;
                }
            }
            catch { }

            // 实时检测顶部状态栏显隐并平滑贴靠状态栏下沿（状态栏隐藏时自动贴回屏幕顶边）
            if (!_pressed && !_dragging)
            {
                UpdateTopBarDocking(smooth: true);
            }

            // 按住鼠标左键拖动期间不触发悬停展开或收起
            bool leftDown = _pressed || _dragging || ((Native.GetAsyncKeyState(0x01) & 0x8000) != 0);
            if (leftDown)
            {
                _hoverInAt = DateTime.MinValue;
                _hoverOutAt = DateTime.MinValue;
                return;
            }

            DateTime now = DateTime.Now;
            if (inside)
            {
                _hoverOutAt = DateTime.MinValue;
                if (_hoverInAt == DateTime.MinValue) _hoverInAt = now;
            }
            else
            {
                _hoverInAt = DateTime.MinValue;
                if (_hoverOutAt == DateTime.MinValue) _hoverOutAt = now;
            }

            if (!_expanded && inside && !_btCardOn)
            {
                // 悬停 380ms 展开，留出充足时间直接按住左键拖动胶囊
                if ((now - _hoverInAt).TotalMilliseconds > 380)
                {
                    _hoverInAt = DateTime.MinValue;
                    _hoverExpanded = true;
                    Expand();
                }
            }
            else if (_expanded && _hoverExpanded && !inside)
            {
                if ((now - _hoverOutAt).TotalMilliseconds > 240)
                {
                    _hoverOutAt = DateTime.MinValue;
                    _hoverExpanded = false;
                    Collapse();
                }
            }
        }

        private const double BodyTopOffset = 58.0;
        private const double TopScreenEdgeGap = 4.0;
        private const double TopBarBottomGap = 2.0;

        private double GetCurrentTopBarBottomDip()
        {
            double dpi = 1.0;
            try
            {
                dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
                if (dpi <= 0.1) dpi = 1.0;
            }
            catch { dpi = 1.0; }

            double bodyLeftDip = Left + (Width - _cw) / 2.0;
            int capLeftPx = (int)Math.Round(bodyLeftDip * dpi);
            int capRightPx = (int)Math.Round((bodyLeftDip + _cw) * dpi);
            int screenWidthPx = (int)Math.Round(SystemParameters.PrimaryScreenWidth * dpi);

            int barBottomPx = Native.GetTopBarBottomPhysicalPx(_hwnd, capLeftPx, capRightPx, screenWidthPx);
            double barBottomDip = barBottomPx > 0 ? (barBottomPx / dpi) : 0.0;
            double waTopDip = SystemParameters.WorkArea.Top;
            return Math.Max(waTopDip, barBottomDip);
        }

        private double ComputeTargetWindowTop(double barBottomDip)
        {
            // 当顶部状态栏显示（barBottomDip > 6）时紧贴状态栏最下边；当状态栏隐藏时自动贴回屏幕顶边
            double topDockedWindowY = (barBottomDip > 6.0)
                ? (barBottomDip + TopBarBottomGap - BodyTopOffset)
                : (TopScreenEdgeGap - BodyTopOffset);

            if (_dockedToTop || !_hasCustomPos)
                return topDockedWindowY;

            // 若用户将胶囊拖到了屏幕中下方的自定义位置，仍需保证顶部状态栏弹出时不遮挡胶囊
            return Math.Max(_customTop, topDockedWindowY);
        }

        private void UpdateTopBarDocking(bool smooth)
        {
            double barBottomDip = GetCurrentTopBarBottomDip();
            double targetTop = ComputeTargetWindowTop(barBottomDip);
            double diff = targetTop - Top;
            if (Math.Abs(diff) <= 0.4) return;
            if (!smooth || Math.Abs(diff) <= 1.2)
                Top = targetTop;
            else
                Top = Top + diff * 0.45;
        }

        private void PlaceCollapsed()
        {
            var wa = SystemParameters.WorkArea;
            if (_hasCustomPos)
            {
                ClampWindowLeft(ref _customLeft);
                Left = _customLeft;
            }
            else
            {
                Left = wa.Left + (wa.Width - Width) / 2.0;
            }
            UpdateTopBarDocking(smooth: false);
        }

        // ------------------------------------------------------------------
        //  蓝牙：连上耳机时在岛下方滑出一张小卡片（图标 + 名称 + 电量）
        // ------------------------------------------------------------------
        private void TickBluetooth()
        {
            if (_btPolling) return;
            _btPolling = true;
            // 枚举走 P/Invoke 很快，但仍然放后台，避免任何情况下拖慢动画
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                List<BtDevice> list;
                try { list = Bluetooth.Enumerate(); }
                catch { list = new List<BtDevice>(); }
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    _btPolling = false;
                    ApplyBluetooth(list);
                }));
            });
        }

        private static int CompareConnectedDevices(BtDevice a, BtDevice b)
        {
            if (a == null && b == null) return 0;
            if (a == null) return 1;
            if (b == null) return -1;
            int ra = a.IsAudio ? 0 : (a.IsKeyboard ? 1 : (a.IsMouse ? 2 : 3));
            int rb = b.IsAudio ? 0 : (b.IsKeyboard ? 1 : (b.IsMouse ? 2 : 3));
            if (ra != rb) return ra.CompareTo(rb);
            int nc = string.Compare(a.Name ?? "", b.Name ?? "", StringComparison.OrdinalIgnoreCase);
            if (nc != 0) return nc;
            return a.Address.CompareTo(b.Address);
        }

        private void ApplyBluetooth(List<BtDevice> list)
        {
            if (list == null) return;

            // 先把缓存读数回填或把新拿到的 PnP 读数落盘
            foreach (var d in list)
                BackfillBatteryCache(d);

            BtDevice audio = null;
            BtDevice newly = null;
            var newlyList = new List<BtDevice>();
            var connected = new List<BtDevice>();
            foreach (var d in list)
            {
                if (d.Connected && d.IsListable)
                {
                    connected.Add(d);
                    if (audio == null && d.IsAudio) audio = d;
                }

                bool popupEligible = d.IsAudio || d.IsPeripheral;
                bool was = false;
                if (_btLastConn.TryGetValue(d.Address, out was))
                {
                    if (d.Connected && !was && popupEligible)
                    {
                        newly = d;
                        newlyList.Add(d);
                    }
                    else if (!d.Connected && was)
                    {
                        _btDeepProbed.Remove(d.Address);
                        _btLastActiveProbeAt.Remove(d.Address);
                        Bluetooth.ClearProbeCache(d.Address);
                    }
                }
                else if (d.Connected && !_btFirstSweep && popupEligible)
                {
                    newly = d;
                    newlyList.Add(d);
                }
                _btLastConn[d.Address] = d.Connected;
            }

            connected.Sort(CompareConnectedDevices);

            _btFirstSweep = false;
            _btAudio = audio;
            _btConnected = connected;
            if (_btCardOn && _btCardDevice != null)
            {
                BtDevice liveCard = FindLiveDevice(_btCardDevice.Address);
                if (liveCard != null)
                {
                    _btCardDevice = liveCard;
                    SetBtBatteryText(liveCard);
                }
            }
            ScheduleBatteryQueries(connected, false);
            RefreshBatteryCenter();

            // 排查用：ISLAND_BTLOG 指向文件时，每次扫描把设备列表写一行
            string btLog = Environment.GetEnvironmentVariable("ISLAND_BTLOG");
            if (btLog != null)
            {
                try
                {
                    var sb = new System.Text.StringBuilder();
                    sb.Append(DateTime.Now.ToString("HH:mm:ss")).Append(" count=").Append(list.Count);
                    foreach (var d in list)
                        sb.Append(" | ").Append(d.Name).Append(" conn=").Append(d.Connected).Append(" cod=").Append(d.ClassOfDevice);
                    System.IO.File.AppendAllText(btLog, sb.ToString() + "\n");
                }
                catch { }
            }

            if (newly != null)
            {
                ShowBtCard(newly);
                ScheduleBatteryQueries(newlyList.Count > 0 ? newlyList : connected, true);
            }
        }

        /// <summary>设备细节交给系统：点卡片直接打开 Windows 蓝牙设置。</summary>
        private void OpenBluetoothSettings()
        {
            try
            {
                Process.Start(new ProcessStartInfo("ms-settings:bluetooth") { UseShellExecute = true });
                return;
            }
            catch { }
            try { Process.Start("control", "bthprops.cpl"); } catch { }
        }

        private void TryReadBattery(BtDevice d)
        {
            if (d == null) return;
            var one = new List<BtDevice>();
            one.Add(d);
            ScheduleBatteryQueries(one, false);
        }

        private void SetBtBatteryText(BtDevice d)
        {
            if (_btBatt == null || d == null) return;
            bool stale = BatteryLooksStale(d);
            SetBtRing(d.HasBattery && !stale ? d.Battery : 0, d.HasBattery && !stale);
            if (d.HasBattery && stale)
            {
                _btBatt.Text = "上次 " + d.Battery + "%";
                _btBatt.FontSize = 8;
                _btBatt.Foreground = new SolidColorBrush(Color.FromArgb(180, 0xAE, 0xC2, 0xD8));
                if (_btSub != null) _btSub.Text = "已连接 · 历史电量";
                return;
            }
            if (!d.HasBattery)
            {
                string st = Bluetooth.StatusText(d.QueryStatus);
                _btBatt.Text = string.IsNullOrEmpty(st) ? "未知" : st;
                _btBatt.FontSize = 8;
                _btBatt.Foreground = new SolidColorBrush(Color.FromArgb(150, 0xAE, 0xC2, 0xD8));
                if (_btSub != null) _btSub.Text = "已连接";
                return;
            }
            if (d.HasSplit && _btBatt != null)
            {
                // 双耳设备：环里直接显示「左 / 右」，副标题补充电盒，别把两个耳机的数揉成一个平均
                _btBatt.Text = d.BatteryLeft + "/" + d.BatteryRight;
                _btBatt.FontSize = 8.5;
                _btBatt.Foreground = new SolidColorBrush(Color.FromRgb(0x2B, 0xD9, 0x6B));
                if (_btSub != null)
                    _btSub.Text = d.BatteryCase >= 0 ? "已连接 · 盒 " + d.BatteryCase + "%" : "已连接";
            }
        }
        /// <summary>连接时电量环弹一下（0.72 → 1，带回弹），模拟系统耳机弹窗的入场。</summary>
        private void PopBtRing()
        {
            if (_btRing == null) return;
            var tr = _btRing.RenderTransform as ScaleTransform;
            if (tr == null) return;
            var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 };
            var sx = new DoubleAnimation(0.72, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = ease };
            var sy = new DoubleAnimation(0.72, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = ease };
            tr.BeginAnimation(ScaleTransform.ScaleXProperty, sx);
            tr.BeginAnimation(ScaleTransform.ScaleYProperty, sy);
        }

        /// <summary>画电量环：已知就画绿色圆弧（从 12 点方向顺时针），未知只留底环并写「未知」。</summary>
        private void SetBtRing(double percent, bool known)
        {
            if (_btRingArc == null || _btBatt == null) return;

            if (!known)
            {
                _btRingArc.Data = null;
                _btBatt.Text = "未知";
                _btBatt.FontSize = 8;
                _btBatt.Foreground = new SolidColorBrush(Color.FromArgb(150, 0xAE, 0xC2, 0xD8));
                return;
            }

            double p = percent < 0 ? 0 : (percent > 100 ? 100 : percent);
            var c = new Point(24, 24);
            const double r = 20.5;
            if (p >= 99.5)
            {
                _btRingArc.Data = new EllipseGeometry(c, r, r);
            }
            else
            {
                double rad = Math.PI / 180;
                double a0 = -90, a1 = -90 + 360 * p / 100;
                var fig = new PathFigure
                {
                    StartPoint = new Point(c.X + r * Math.Cos(a0 * rad), c.Y + r * Math.Sin(a0 * rad))
                };
                fig.Segments.Add(new ArcSegment
                {
                    Point = new Point(c.X + r * Math.Cos(a1 * rad), c.Y + r * Math.Sin(a1 * rad)),
                    Size = new Size(r, r),
                    SweepDirection = SweepDirection.Clockwise,
                    IsLargeArc = p > 50
                });
                var geo = new PathGeometry();
                geo.Figures.Add(fig);
                _btRingArc.Data = geo;
            }

            _btBatt.Text = ((int)Math.Round(p)) + "%";
            _btBatt.FontSize = 10.5;
            _btBatt.Foreground = new SolidColorBrush(Color.FromRgb(0x2B, 0xD9, 0x6B));
        }

        /// <summary>岛体临时长高，卡片从时钟下面滑出来。</summary>
        private void ShowBtCard(BtDevice d)
        {
            if (_btLayer == null || d == null) return;
            if (_expanded || _btCardOn) return;

            _btCardOn = true;
            _btCardDevice = d;
            if (_btIcon != null)
            {
                if (d.IsKeyboard) _btIcon.Text = "\uE765";
                else if (d.IsMouse) _btIcon.Text = "\uE962";
                else if (d.IsPeripheral && !d.IsAudio) _btIcon.Text = "\uE772";
                else _btIcon.Text = "\uE7F6";
            }
            _btName.Text = string.IsNullOrEmpty(d.Name) ? "蓝牙设备" : d.Name;
            if (_btSub != null) _btSub.Text = "已连接";
            SetBtBatteryText(d);
            _btLayer.Visibility = Visibility.Visible;
            _body.Children.Remove(_btLayer);
            _body.Children.Add(_btLayer);
            _btLayer.IsHitTestVisible = true;
            if (_btHover != null) { _btHover.BeginAnimation(OpacityProperty, null); _btHover.Opacity = 0; }

            FitWindow(BtCardW, BtCardH);
            // 切到卡片专用弹簧：先把通用弹簧就地停住（否则两组同时写 _cw 会打架），
            // 再用较软的一组把胶囊「伸」成卡片
            _sw.Set(_cw);
            _sh.Set(_ch);
            _sr.Set(_cr);
            _sbw.To(BtCardW);
            _sbh.To(BtCardH);
            _sbr.To(BtCardR);
            KickAnimation();

            // 卡片内容不再用「定格动画」，而是跟着壳体的生长进度逐帧推进：
            // 高度长到几成，内容就浮出来几成，同时从 0.94 轻微放大到 1.0、从 +4 归到 0。
            // 这样它是「从胶囊里长出来」的，而不是等一会儿突然贴上。
            _btLayer.BeginAnimation(OpacityProperty, null);
            _btLayer.Opacity = 1;
            _btContent.BeginAnimation(OpacityProperty, null);
            var tr = (TranslateTransform)_btLayer.RenderTransform;
            tr.BeginAnimation(TranslateTransform.YProperty, null);
            tr.Y = 0;
            ApplyBtReveal();
            _collapsedLayer.BeginAnimation(OpacityProperty, null);
            _collapsedLayer.Opacity = 0;
            _collapsedLayer.Visibility = Visibility.Collapsed;
            PopBtRing();
            // 音乐胶囊是独立一层，压在卡片上面，卡片出现时必须一起淡掉，否则歌名会糊在「已连接」上
            if (_musicLayer != null)
            {
                // 注意：这里不能用 Fade（动画会 HoldEnd 压住每帧写的 Opacity，卡片收走后音乐胶囊就再也不出来了）
                _musicLayer.BeginAnimation(OpacityProperty, null);
                _musicLayer.IsHitTestVisible = false;
                _musicLayer.Opacity = 0;
            }

            _btHideTimer.Stop();
            _btHideTimer.Start();
        }

        /// <summary>收回卡片：岛体缩回原来的高度，窗口等弹簧落位后再收缩。</summary>
        private void HideBtCard()
        {
            if (_btLayer == null || !_btCardOn) return;
            _btCardOn = false;
            _btHideTimer.Stop();
            _btLayer.IsHitTestVisible = false;

            // 收回：内容和时钟做一次 160ms 的交叉淡出（同一块深底上换内容），
            // 壳体由几何进度同步缩回去。之前是「卡片内容一直挂着、时钟半截透上来」，
            // 就是用户看到的「缩回去很脏」。
            var tr = (TranslateTransform)_btLayer.RenderTransform;
            tr.BeginAnimation(TranslateTransform.YProperty, null);
            tr.Y = 0;
            if (_btContent != null) Fade(_btContent, 0, 160);
            if (_musicLayer != null) _musicLayer.IsHitTestVisible = true;

            _sbw.To(CollapsedW);
            _sbh.To(CollapsedH);
            _sbr.To(CollapsedR);
            _collapsedLayer.BeginAnimation(OpacityProperty, null);
            _collapsedLayer.Visibility = Visibility.Visible;
            // 时钟晚 140ms 再进，等卡片内容先淡掉，避免「大字和时钟同时在卡里」
            FadeDelayed(_collapsedLayer, 1, 160, 140);
            _btPendingShrink = true;
            KickAnimation();
        }

        // ------------------------------------------------------------------
        //  Expand / collapse
        // ------------------------------------------------------------------
        private void Expand()
        {
            if (_expanded) return;
            _expanded = true;
            ExpandedNow = true;
            _pendingShrink = false;

            // 展开时把蓝牙卡片直接收掉，否则会被展开面板盖住、窗口尺寸也会打架
            _btHideTimer.Stop();
            _btCardOn = false;
            _btPendingShrink = false;
            if (_btLayer != null) { _btLayer.BeginAnimation(OpacityProperty, null); _btLayer.Opacity = 0; _btLayer.IsHitTestVisible = false; }
            _collapsedLayer.BeginAnimation(OpacityProperty, null);
            _collapsedLayer.Opacity = 1;

            // 音乐胶囊只属于收起态，展开时立刻清掉，免得在面板上留一层残影
            _musicT = 0;
            if (_musicLayer != null) { _musicLayer.BeginAnimation(OpacityProperty, null); _musicLayer.Opacity = 0; }

            _collapsedLayer.IsHitTestVisible = false;
            _expandedLayer.IsHitTestVisible = true;
            _expandedLayer.Visibility = Visibility.Visible;

            ResetBubble();
            FitWindow(ExpandedW, ExpandedH);
            _sw.To(ExpandedW);
            _sh.To(ExpandedH);
            _sr.To(ExpandedR);
            _sglow.To(1);
            KickAnimation();

            Fade(_collapsedLayer, 0, 110);
            RevealExpanded(true);
            SweepSpecular();

            // 展开面板时只刷新已由 PnP 内存读取到的电量，绝不触发无线电连接，防止操作暂停/播放时耳机断音
        }

        private void Collapse()
        {
            if (!_expanded) return;
            _expanded = false;
            ExpandedNow = false;
            _pendingShrink = true;

            _expandedLayer.IsHitTestVisible = false;
            _collapsedLayer.IsHitTestVisible = true;

            ResetBubble();
            // 窗口先保持大尺寸，等弹簧全部归位后再收缩，避免中途被裁一刀出现跳变。
            // 内容跟着壳体一起缩小（而不是被裁掉），缩到胶囊大小后再让胶囊淡入
            RevealExpanded(false);
            FadeDelayed(_collapsedLayer, 1, 120, 110);   // 面板内容淡掉之后马上让胶囊淡入，中间不留空档也不叠影
            _sw.To(CollapsedW);
            _sh.To(CollapsedH);
            _sr.To(CollapsedR);
            _sglow.To(0);
            KickAnimation();
        }

        private void FitWindow(double contentW, double contentH)
        {
            double w = Math.Max(contentW, ExpandedW) + StagePad;
            double h = Math.Max(contentH, ExpandedH) + StagePad;
            if (Math.Abs(Width - w) >= 0.5 || Math.Abs(Height - h) >= 0.5)
            {
                Width = w;
                Height = h;
            }
            ApplyGeometry();
        }

        private static void Fade(UIElement element, double target, double ms)
        {
            var anim = new DoubleAnimation(target, TimeSpan.FromMilliseconds(ms))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            element.BeginAnimation(OpacityProperty, anim);
        }

        /// <summary>延迟淡入：等壳体先缩到位，圆角胶囊内容再出现，避免两层叠影。</summary>
        private static void FadeDelayed(UIElement element, double target, double ms, double delayMs)
        {
            var anim = new DoubleAnimation(target, TimeSpan.FromMilliseconds(ms))
            {
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            element.BeginAnimation(OpacityProperty, anim);
        }

        private void RevealExpanded(bool show)
        {
            if (_expScale == null)
            {
                _expScale = new ScaleTransform(1, 1);
                _expShift = new TranslateTransform(0, 0);
                var tg = new TransformGroup();
                tg.Children.Add(_expScale);
                tg.Children.Add(_expShift);
                _expandedLayer.RenderTransformOrigin = new Point(0.5, 0.24);
                _expandedLayer.RenderTransform = tg;
            }

            // 清掉子元素旧的错峰动画，统一由整层做一次干净的运动
            foreach (UIElement child in _expandedLayer.Children)
            {
                var fe = child as FrameworkElement;
                if (fe == null) continue;
                var tr = fe.RenderTransform as TranslateTransform;
                if (tr != null) { tr.BeginAnimation(TranslateTransform.YProperty, null); tr.Y = 0; }
                fe.BeginAnimation(OpacityProperty, null);
                fe.Opacity = 1;
            }

            int ms = show ? 300 : 300;
            var ease = show
                ? (IEasingFunction)new QuinticEase { EasingMode = EasingMode.EaseOut }
                : (IEasingFunction)new CubicEase { EasingMode = EasingMode.EaseIn };

            if (show)
            {
                // 展开时不要再给内容做缩放：文字被逐帧重新栅格化，笔画会一直在细微抖动，
                // 看着就是“面板里的字在抖”。展开只做淡入，壳体由弹簧长出来。
                _expScale.ScaleX = 1.0; _expScale.ScaleY = 1.0;
                _expShift.Y = 0;
                _expandedLayer.BeginAnimation(OpacityProperty, null);
                _expandedLayer.Opacity = 0;
            }

            // 收起时整层等比缩小到胶囊的尺寸，和壳体收缩同步，所以不会再出现"内容被啃掉一角"
            double scaleTo = show ? 1.0 : 0.78;
            _expScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scaleTo, TimeSpan.FromMilliseconds(show ? ms : 200)) { EasingFunction = ease });
            _expScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scaleTo, TimeSpan.FromMilliseconds(show ? ms : 200)) { EasingFunction = ease });
            _expShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(show ? 0.0 : 8.0, TimeSpan.FromMilliseconds(show ? ms : 200)) { EasingFunction = ease });

            // 收起时内容要比壳体更早退场：面板 420×500、胶囊只有 198×42，
            // 按比例缩放永远缩不进胶囊里，只要还看得见就会被窗口边界切一刀。
            // 但也不能一刀切太急（60ms 直接灭，看着很硬）：用 130ms 的 EaseIn，
            // 前面慢、后面快，正好在壳体缩到「装不下面板」的时候退干净。
            // 剩下的交给 ApplyGeometry 里 _ch < ExpandedH*0.62 的兜底。
            var op = new DoubleAnimation(show ? 1.0 : 0.0, TimeSpan.FromMilliseconds(show ? 200 : 130))
            {
                BeginTime = TimeSpan.Zero,
                EasingFunction = new CubicEase { EasingMode = show ? EasingMode.EaseOut : EasingMode.EaseIn }
            };
            _expandedLayer.BeginAnimation(OpacityProperty, op);
        }

        // ------------------------------------------------------------------
        //  Input
        // ------------------------------------------------------------------
        private void ClampWindowLeft(ref double candidateLeft)
        {
            double screenW = SystemParameters.PrimaryScreenWidth;
            double bodyOffsetX = (Width - _cw) / 2.0;
            double minLeft = -bodyOffsetX + 4.0;
            double maxLeft = screenW - bodyOffsetX - _cw - 4.0;
            if (candidateLeft < minLeft) candidateLeft = minLeft;
            if (candidateLeft > maxLeft) candidateLeft = maxLeft;
        }

        private void OnDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            _pressed = true;
            _dragging = false;
            _press = e.GetPosition(this);
            Native.GetCursorPos(out _pressCursorPx);
            _dragStartLeft = Left;
            _dragStartTop = Top;
            try { _stage.CaptureMouse(); } catch { }
        }

        private void OnMove(object sender, MouseEventArgs e)
        {
            if (!_pressed) return;
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                _pressed = false;
                _dragging = false;
                try { if (_stage.IsMouseCaptured) _stage.ReleaseMouseCapture(); } catch { }
                return;
            }

            Native.POINT curPx;
            if (!Native.GetCursorPos(out curPx)) return;

            double dpi = 1.0;
            try
            {
                dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
                if (dpi <= 0.1) dpi = 1.0;
            }
            catch { dpi = 1.0; }

            double dx = (curPx.X - _pressCursorPx.X) / dpi;
            double dy = (curPx.Y - _pressCursorPx.Y) / dpi;

            if (!_dragging && (Math.Abs(dx) > 4.0 || Math.Abs(dy) > 4.0))
            {
                _dragging = true;
                Cursor = Cursors.SizeAll;
            }

            if (_dragging)
            {
                double newLeft = _dragStartLeft + dx;
                double newTop = _dragStartTop + dy;

                double screenW = SystemParameters.PrimaryScreenWidth;
                double screenH = SystemParameters.PrimaryScreenHeight;
                double centerLeft = (screenW - Width) / 2.0;
                // 靠近屏幕水平中线（±10px）时提供轻柔磁吸居中体验
                if (Math.Abs(newLeft - centerLeft) < 10.0)
                    newLeft = centerLeft;

                ClampWindowLeft(ref newLeft);

                double minTop = TopScreenEdgeGap - BodyTopOffset;
                double maxTop = screenH - BodyTopOffset - _ch - 8.0;
                if (newTop < minTop) newTop = minTop;
                if (newTop > maxTop) newTop = maxTop;

                Left = newLeft;
                Top = newTop;
            }
        }

        private void OnUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            if (!_pressed) return;
            _pressed = false;
            try { if (_stage.IsMouseCaptured) _stage.ReleaseMouseCapture(); } catch { }
            Cursor = Cursors.Arrow;

            if (_dragging)
            {
                _dragging = false;
                _hasCustomPos = true;
                _customLeft = Left;

                double barBottomDip = GetCurrentTopBarBottomDip();
                double capsuleScreenTop = Top + BodyTopOffset;
                // 若松手时胶囊靠近屏幕顶边或状态栏下沿（28px 内），自动开启贴顶/贴状态栏下沿跟随模式
                if (capsuleScreenTop <= barBottomDip + 28.0)
                {
                    _dockedToTop = true;
                    UpdateTopBarDocking(smooth: true);
                }
                else
                {
                    _dockedToTop = false;
                    _customTop = Top;
                }
                SavePositionConfig();
                return;
            }

            if (!_expanded) { _shover.To(0); KickAnimation(); }

            if (_expanded)
            {
                if (_hoverExpanded) return;
                Collapse();
            }
            else
            {
                Expand();
            }
        }

        private string PositionConfigPath
        {
            get
            {
                string dir = DataDir;
                return dir == null ? null : System.IO.Path.Combine(dir, "island_pos.txt");
            }
        }

        private void LoadPositionConfig()
        {
            try
            {
                string path = PositionConfigPath;
                if (path == null || !System.IO.File.Exists(path)) return;
                string[] parts = System.IO.File.ReadAllText(path).Trim().Split(';');
                if (parts.Length >= 3)
                {
                    double l, t;
                    bool docked;
                    if (double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out l) &&
                        double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out t) &&
                        bool.TryParse(parts[2], out docked))
                    {
                        _hasCustomPos = true;
                        _customLeft = l;
                        _customTop = t;
                        _dockedToTop = docked;
                    }
                }
            }
            catch { }
        }

        private void SavePositionConfig()
        {
            try
            {
                string path = PositionConfigPath;
                if (path == null) return;
                string line = _customLeft.ToString("F1", CultureInfo.InvariantCulture) + ";" +
                              _customTop.ToString("F1", CultureInfo.InvariantCulture) + ";" +
                              _dockedToTop.ToString();
                System.IO.File.WriteAllText(path, line);
            }
            catch { }
        }

        /// <summary>一键重置胶囊位置为屏幕顶部水平居中并自动贴靠顶部状态栏下沿。</summary>
        public void ResetPosition()
        {
            _hasCustomPos = false;
            _dockedToTop = true;
            var wa = SystemParameters.WorkArea;
            _customLeft = wa.Left + (wa.Width - Width) / 2.0;
            _customTop = TopScreenEdgeGap - BodyTopOffset;
            Left = _customLeft;
            UpdateTopBarDocking(smooth: true);
            try
            {
                string path = PositionConfigPath;
                if (path != null && System.IO.File.Exists(path))
                    System.IO.File.Delete(path);
            }
            catch { }
        }

        private void WatchOutsideClick()
        {
            if (!_expanded) return;
            bool down = (Native.GetAsyncKeyState(0x01) & 0x8000) != 0;
            if (down && !_cursorWasDown && !_pressed)
            {
                Native.POINT p;
                if (Native.GetCursorPos(out p))
                {
                    Point p0 = _body.PointToScreen(new Point(0, 0));
                    Point p1 = _body.PointToScreen(new Point(_cw, _ch));
                    bool inside = p.X >= p0.X - 8 && p.X <= p1.X + 8 &&
                                  p.Y >= p0.Y - 8 && p.Y <= p1.Y + 8;
                    if (!inside) Collapse();
                }
            }
            _cursorWasDown = down;
        }

        private void OnSpecularTick(object sender, EventArgs e) { SweepSpecular(); }

        private void RunShotSequence()
        {
            LogShot("RunShotSequence enter");
            var dir = System.IO.Path.GetDirectoryName(_shotPath);
            if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);

            Action captureBoth = delegate
            {
                SaveShot(_shotPath);
                LogShot("collapsed saved");

                Expand();
                _sw.Set(ExpandedW); _sh.Set(ExpandedH); _sr.Set(ExpandedR);
                _expandedLayer.Opacity = 1;
                _collapsedLayer.Opacity = 0;
                foreach (UIElement child in _expandedLayer.Children)
                {
                    var fe = child as FrameworkElement;
                    if (fe == null) continue;
                    fe.Opacity = 1;
                    var tr = fe.RenderTransform as TranslateTransform;
                    if (tr != null) tr.Y = 0;
                }
                Application.Current.Dispatcher.BeginInvoke(new Action(delegate
                {
                    string expandedPath = System.IO.Path.Combine(
                        string.IsNullOrEmpty(dir) ? "." : dir, "shot_expanded.png");
                    SaveShot(expandedPath);
                    LogShot("expanded saved");

                    // 第三张：蓝牙连接卡片的实际样子（离屏渲染，方便自查版式）
                    try
                    {
                        _expanded = false;
                        _expandedLayer.Opacity = 0;
                        _collapsedLayer.Opacity = 1;
                        _cw = BtCardW; _ch = BtCardH; _cr = BtCardR;
                        _btName.Text = "HONOR Earbuds X9";
                        SetBtRing(100, true);
                        _btLayer.Opacity = 1;
                        var tt = _btLayer.RenderTransform as TranslateTransform;
                        if (tt != null) tt.Y = 0;
                        _body.Width = BtCardW; _body.Height = BtCardH;
                        _bodyHost.Width = BtCardW + StagePad;
                        _bodyHost.Height = BtCardH + StagePad;
                        _stage.Measure(new Size(_stage.Width, _stage.Height));
                        _stage.Arrange(new Rect(0, 0, _stage.Width, _stage.Height));
                        _stage.UpdateLayout();
                        string btPath = System.IO.Path.Combine(
                            string.IsNullOrEmpty(dir) ? "." : dir, "shot_btcard.png");
                        SaveShot(btPath);
                        LogShot("btcard saved");
                    }
                    catch (Exception ex) { LogShot("btcard fail " + ex.Message); }

                    try { Close(); } catch { }
                    Application.Current.Shutdown();
                }), DispatcherPriority.Loaded);
            };
            // 截图模式演示：等频谱跑起来再截
            var delay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1800) };
            delay.Tick += delegate { delay.Stop(); captureBoth(); };
            delay.Start();
        }

        public void RenderPreview(string outDir)
        {
            // 预览按最大舞台尺寸摆放：收起/展开两种状态都以同一个 540×620 画布为中心，
            // 这样截出来的图才有对比意义（不然会像之前那样贴到画布左上角）。
            // 先把所有会改几何的定时器停掉：否则刚设成展开尺寸，下一拍又被收起的弹簧覆盖回去。
            CompositionTarget.Rendering -= OnFrame;
            _frameHooked = false;
            _clockTimer.Stop(); _statsTimer.Stop(); _specTimer.Stop(); _outTimer.Stop();
            _feedTimer.Stop(); _bubbleTimer.Stop(); _passTimer.Stop(); _eqTimer.Stop();
            _btTimer.Stop(); _btHideTimer.Stop();

            Width = ExpandedW + StagePad;
            Height = ExpandedH + StagePad;
            _bodyHost.Width = Width;
            _bodyHost.Height = Height;

            // Collapsed state -------------------------------------------------
            _cw = CollapsedW; _ch = CollapsedH; _cr = CollapsedR; _hoverT = 0;
            ApplyGeometry();
            TickClock();
            TickStats();
            _expandedLayer.Opacity = 0;
            _collapsedLayer.Opacity = 1;
            _bodyHost.Effect = null;
            Render(outDir, "island_collapsed.png");

            // Expanded state --------------------------------------------------
            _cw = ExpandedW; _ch = ExpandedH; _cr = ExpandedR;
            ApplyGeometry();
            _collapsedLayer.Opacity = 0;
            _expandedLayer.Opacity = 1;
            foreach (UIElement child in _expandedLayer.Children)
            {
                var fe = child as FrameworkElement;
                if (fe == null) continue;
                fe.Opacity = 1;
                var tr = fe.RenderTransform as TranslateTransform;
                if (tr != null) tr.Y = 0;
            }
            Render(outDir, "island_expanded.png");
        }

        private void Render(string outDir, string fileName)
        {
            // 先把整棵窗口的布局刷干净：只 arrange _stage 时，_body 改过的宽高不会被
            // 立刻应用，截出来还是上一帧的旧尺寸（之前展开图一直是那个小胶囊就是这个原因）。
            InvalidateMeasure();
            InvalidateArrange();
            _stage.Measure(new Size(_stage.Width, _stage.Height));
            _stage.Arrange(new Rect(0, 0, _stage.Width, _stage.Height));
            UpdateLayout();

            var rtb = new RenderTargetBitmap(
                (int)Math.Ceiling(_stage.Width), (int)Math.Ceiling(_stage.Height),
                96, 96, PixelFormats.Pbgra32);
            rtb.Render(_stage);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            string path = System.IO.Path.Combine(outDir, fileName);
            using (var fs = new FileStream(path, FileMode.Create))
            {
                encoder.Save(fs);
            }
            Console.WriteLine("wrote " + path);
        }

        private void LogShot(string message)
        {
            try { System.IO.File.AppendAllText(_shotPath + ".log", message + "\n"); } catch { }
        }

        private void SaveShot(string path)
        {
            try { SaveShotCore(path); }
            catch (Exception ex)
            {
                System.IO.File.WriteAllText(path + ".error.txt", ex.ToString());
                throw;
            }
        }

        private void SaveShotCore(string path)
        {
            _stage.Measure(new Size(_stage.Width, _stage.Height));
            _stage.Arrange(new Rect(0, 0, _stage.Width, _stage.Height));
            _stage.UpdateLayout();

            var rtb = new RenderTargetBitmap(
                (int)Math.Ceiling(_stage.Width), (int)Math.Ceiling(_stage.Height),
                96, 96, PixelFormats.Pbgra32);
            rtb.Render(_stage);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = new System.IO.FileStream(path, System.IO.FileMode.Create))
            {
                encoder.Save(fs);
            }
        }

        private void SweepSpecular()
        {
            if (_specular == null) return;
            var group = _specular.RenderTransform as TransformGroup;
            if (group == null || group.Children.Count < 2) return;
            var translate = group.Children[1] as TranslateTransform;
            if (translate == null) return;

            var xAnim = new DoubleAnimation(-170, _cw + 170, TimeSpan.FromMilliseconds(1500))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            translate.BeginAnimation(TranslateTransform.XProperty, xAnim);

            var opacity = new DoubleAnimationUsingKeyFrames();
            opacity.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromPercent(0.0)));
            opacity.KeyFrames.Add(new LinearDoubleKeyFrame(0.85, KeyTime.FromPercent(0.18)));
            opacity.KeyFrames.Add(new LinearDoubleKeyFrame(0.85, KeyTime.FromPercent(0.72)));
            opacity.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromPercent(1.0)));
            opacity.Duration = TimeSpan.FromMilliseconds(1500);
            _specular.BeginAnimation(OpacityProperty, opacity);
        }

        // ------------------------------------------------------------------
        //  Live data
        // ------------------------------------------------------------------
        /// <summary>
        /// 位置钉死：水平居中、贴着屏幕顶边。任何时刻只要位置或尺寸被外力改掉（改分辨率、
        /// 换缩放、被系统挪动），这里会把它拉回原位，用户无法拖动。
        /// </summary>
        private void EnforceCenter()
        {
            if (_pressed || _dragging) return;
            var wa = SystemParameters.WorkArea;
            if (!_hasCustomPos)
            {
                double targetLeft = wa.Left + (wa.Width - Width) / 2.0;
                if (Math.Abs(Left - targetLeft) > 0.5) Left = targetLeft;
            }
            else
            {
                double clampedLeft = Left;
                ClampWindowLeft(ref clampedLeft);
                if (Math.Abs(Left - clampedLeft) > 0.5) Left = clampedLeft;
            }
            UpdateTopBarDocking(smooth: true);
        }
        /// <summary>把系统媒体会话里的「歌名 / 歌手」同步到展开面板媒体卡的标题行。</summary>
        private void SyncExpandedSong()
        {
            if (_expTitle == null) return;
            // 暂停时仍显示当前曲目和歌词，否则拖进度后必须再按播放才能对上。
            string title = MediaSession.Title;
            string artist = MediaSession.Artist;
            if (title.Length == 0) title = "媒体控制";
            if (title != _expTitle.Text) _expTitle.Text = title;
            if (_expArtist != null && artist != _expArtist.Text) _expArtist.Text = artist;
            if (_expLyric != null)
            {
                string line = MediaSession.CurrentLine();
                if (line != _expLyric.Text) _expLyric.Text = line;
            }
            UpdatePlayPauseState();
        }
        private void TickClock()
        {
            var now = DateTime.Now;
            string time = now.ToString("HH:mm", CultureInfo.InvariantCulture);
            string shortDate = now.ToString("ddd M月d日", _zh);
            string longDate = now.ToString("dddd，M月d日", _zh);

            EnforceCenter();
            SyncExpandedSong();
            if (_tClock != null) _tClock.Text = time;
            if (_tDate != null) _tDate.Text = shortDate;
            if (_hClock != null) _hClock.Text = time;
            if (_hDate != null)
            {
                if (_solarEnabled)
                {
                    var sf = SolarRhythm.Compute(now);
                    _hDate.Text = longDate + " · " + sf.PhaseName;
                }
                else
                {
                    _hDate.Text = longDate;
                }
            }

            if (_solarEnabled && now.Minute != _lastSolarMinute)
            {
                _lastSolarMinute = now.Minute;
                ApplySolarRhythm(now, smooth: true);
            }
        }

        private string SolarConfigPath
        {
            get
            {
                string dir = DataDir;
                return dir == null ? null : System.IO.Path.Combine(dir, "solar_config.txt");
            }
        }

        private void LoadSolarConfig()
        {
            try
            {
                string path = SolarConfigPath;
                if (path != null && System.IO.File.Exists(path))
                {
                    string txt = System.IO.File.ReadAllText(path).Trim();
                    bool val;
                    if (bool.TryParse(txt, out val)) _solarEnabled = val;
                }
            }
            catch { }
        }

        public void ToggleSolarRhythm(bool enabled)
        {
            _solarEnabled = enabled;
            try
            {
                string path = SolarConfigPath;
                if (path != null)
                {
                    string dir = System.IO.Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                        System.IO.Directory.CreateDirectory(dir);
                    System.IO.File.WriteAllText(path, enabled.ToString());
                }
            }
            catch { }
            _lastSolarMinute = -1;
            ApplyGeometry();
            ApplySolarRhythm(DateTime.Now, smooth: true);
            TickClock();
        }

        private void ApplySolarRhythm(DateTime now, bool smooth)
        {
            if (_glassBg == null || _blobA == null || _blobB == null || _blobC == null) return;

            if (!_solarEnabled)
            {
                SetDefaultGlassBg(smooth);
                return;
            }

            var frame = SolarRhythm.Compute(now);

            // 1. 底色 4 级渐变更新
            var stops = _glassBg.GradientStops;
            if (stops.Count >= 4)
            {
                if (smooth)
                {
                    TimeSpan dur = TimeSpan.FromSeconds(1.2);
                    var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
                    stops[0].BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(frame.Bg0, dur) { EasingFunction = ease });
                    stops[1].BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(frame.Bg1, dur) { EasingFunction = ease });
                    stops[2].BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(frame.Bg2, dur) { EasingFunction = ease });
                    stops[3].BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(frame.Bg3, dur) { EasingFunction = ease });
                }
                else
                {
                    stops[0].BeginAnimation(GradientStop.ColorProperty, null);
                    stops[1].BeginAnimation(GradientStop.ColorProperty, null);
                    stops[2].BeginAnimation(GradientStop.ColorProperty, null);
                    stops[3].BeginAnimation(GradientStop.ColorProperty, null);
                    stops[0].Color = frame.Bg0;
                    stops[1].Color = frame.Bg1;
                    stops[2].Color = frame.Bg2;
                    stops[3].Color = frame.Bg3;
                }
            }

            // 2. 光斑位置与颜色平滑过渡
            UpdateBlob(_blobA, frame.PosA, frame.ColA, smooth);
            UpdateBlob(_blobB, frame.PosB, frame.ColB, smooth);
            UpdateBlob(_blobC, frame.PosC, frame.ColC, smooth);
        }

        private void UpdateBlob(Ellipse blob, Point targetPos, Color targetColor, bool smooth)
        {
            if (blob == null) return;
            var rBrush = blob.Fill as RadialGradientBrush;
            if (rBrush != null && rBrush.GradientStops.Count >= 4)
            {
                var gs = rBrush.GradientStops;
                Color c0 = targetColor;
                Color c1 = Color.FromArgb((byte)(targetColor.A * 0.55), targetColor.R, targetColor.G, targetColor.B);
                Color c2 = Color.FromArgb((byte)(targetColor.A * 0.16), targetColor.R, targetColor.G, targetColor.B);
                Color c3 = Color.FromArgb(0, targetColor.R, targetColor.G, targetColor.B);

                if (smooth)
                {
                    TimeSpan dur = TimeSpan.FromSeconds(1.2);
                    var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
                    gs[0].BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(c0, dur) { EasingFunction = ease });
                    gs[1].BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(c1, dur) { EasingFunction = ease });
                    gs[2].BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(c2, dur) { EasingFunction = ease });
                    gs[3].BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(c3, dur) { EasingFunction = ease });
                }
                else
                {
                    gs[0].BeginAnimation(GradientStop.ColorProperty, null);
                    gs[1].BeginAnimation(GradientStop.ColorProperty, null);
                    gs[2].BeginAnimation(GradientStop.ColorProperty, null);
                    gs[3].BeginAnimation(GradientStop.ColorProperty, null);
                    gs[0].Color = c0;
                    gs[1].Color = c1;
                    gs[2].Color = c2;
                    gs[3].Color = c3;
                }
            }

            if (smooth)
            {
                TimeSpan dur = TimeSpan.FromSeconds(1.2);
                var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
                var animX = new DoubleAnimation(targetPos.X, dur) { EasingFunction = ease };
                var animY = new DoubleAnimation(targetPos.Y, dur) { EasingFunction = ease };
                blob.BeginAnimation(Canvas.LeftProperty, animX);
                blob.BeginAnimation(Canvas.TopProperty, animY);
            }
            else
            {
                blob.BeginAnimation(Canvas.LeftProperty, null);
                blob.BeginAnimation(Canvas.TopProperty, null);
                Canvas.SetLeft(blob, targetPos.X);
                Canvas.SetTop(blob, targetPos.Y);
            }
        }

        private void SetDefaultGlassBg(bool smooth)
        {
            if (_glassBg == null || _glassBg.GradientStops.Count < 4) return;
            var stops = _glassBg.GradientStops;
            Color c0 = Color.FromArgb(232, 0x1C, 0x23, 0x36);
            Color c1 = Color.FromArgb(242, 0x12, 0x17, 0x26);
            Color c2 = Color.FromArgb(248, 0x0B, 0x0E, 0x1A);
            Color c3 = Color.FromArgb(252, 0x06, 0x08, 0x10);

            if (smooth)
            {
                TimeSpan dur = TimeSpan.FromSeconds(1.0);
                var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
                stops[0].BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(c0, dur) { EasingFunction = ease });
                stops[1].BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(c1, dur) { EasingFunction = ease });
                stops[2].BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(c2, dur) { EasingFunction = ease });
                stops[3].BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(c3, dur) { EasingFunction = ease });
            }
            else
            {
                stops[0].BeginAnimation(GradientStop.ColorProperty, null);
                stops[1].BeginAnimation(GradientStop.ColorProperty, null);
                stops[2].BeginAnimation(GradientStop.ColorProperty, null);
                stops[3].BeginAnimation(GradientStop.ColorProperty, null);
                stops[0].Color = c0;
                stops[1].Color = c1;
                stops[2].Color = c2;
                stops[3].Color = c3;
            }

            UpdateBlob(_blobA, new Point(-70, -86), Color.FromArgb(148, 0x54, 0x86, 0xFF), smooth);
            UpdateBlob(_blobB, new Point(168, 26), Color.FromArgb(120, 0x60, 0xEE, 0xD6), smooth);
            UpdateBlob(_blobC, new Point(-30, 176), Color.FromArgb(104, 0xB2, 0x78, 0xFF), smooth);
        }

        private void TickStats()
        {
            int cpu = (int)Math.Round(Native.SystemCpuPercent());
            if (cpu == 0 && _lastCpu >= 0) cpu = _lastCpu;
            _lastCpu = cpu;

            double usedGb, totalGb;
            Native.MemoryInfo(out usedGb, out totalGb);
            int memPercent = totalGb > 0 ? (int)Math.Round(usedGb / totalGb * 100) : Native.MemoryLoadPercent();

            bool onAc;
            int battery = Native.BatteryPercent(out onAc);
            double uptime = Math.Max(0, (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalHours);

            // 台式机没有电池：右上角时间胶囊不显示电量，指标卡老实写“台式机 · 无电池”，
            // 不再用“无电池数据”这种像出错一样的文案
            if (_tBatteryPct != null)
            {
                // 台式机：时间胶囊右侧干脆不显示电量（电量在电池卡和电量中心里看）；
                // 笔记本才在这里实时显示百分比
                _tBatteryPct.Text = _hasBattery ? (battery < 0 ? "—" : battery + "%") : "";
                _tBatteryPct.Visibility = _hasBattery ? Visibility.Visible : Visibility.Collapsed;
            }

            SetMetric(_cpuVal, _cpuSub, _cpuBar, cpu + "%", "系统", cpu / 100.0);
            SetMetric(_memVal, _memSub, _memBar, memPercent + "%",
                totalGb > 0 ? usedGb.ToString("0.0") + " / " + totalGb.ToString("0") + " GB" : "",
                memPercent / 100.0);
            if (_hasBattery)
            {
                SetMetric(_batVal, _batSub, _batBar, battery < 0 ? "—" : battery + "%",
                    battery < 0 ? "正在读取" : (onAc ? "已接通电源" : "使用电池"),
                    battery < 0 ? 0 : battery / 100.0);
            }
            else
            {
                // 台式机 / 无电池：如实展示外接电源，不伪造 100% 满电
                SetMetric(_batVal, _batSub, _batBar, "—", "外接电源运行", 0.0);
            }
            SetMetric(_upVal, _upSub, _upBar,
                uptime >= 24 ? (uptime / 24.0).ToString("0.#") + "天" : uptime.ToString("0.#") + "时",
                "本程序运行", Math.Min(1, uptime / 24.0));
            SetMetric(_upVal, _upSub, _upBar,
                uptime >= 24 ? (uptime / 24.0).ToString("0.#") + "天" : uptime.ToString("0.#") + "时",
                "本程序运行", Math.Min(1, uptime / 24.0));

            RefreshBatteryCenter();
        }

        // ------------------------------------------------------------------
        //  微信通道已取消（微信 4.x 不投 Windows 通知，截屏+OCR 也不稳定）
        // ------------------------------------------------------------------
        private void TickFeed()
        {
            if (_shotMode) return;
            // 不再抓取微信，保留空实现避免改动其它调用点
        }

        private void ShowBubble(WeChatFeed.Message m)
        {
            if (_bubbleLayer == null || m == null) return;

            if (_bubSender != null) _bubSender.Text = m.Sender.Length > 0 ? m.Sender : "微信";
            if (_bubBody != null) _bubBody.Text = m.Body;

            if (!_bubbleOn)
            {
                _bubbleOn = true;
                _bubbleLayer.IsHitTestVisible = true;
                _collapsedLayer.IsHitTestVisible = false;
                FitWindow(BubbleW, CollapsedH);
                _sw.To(BubbleW);
                Fade(_collapsedLayer, 0, 130);
                Fade(_bubbleLayer, 1, 190);
            }

            _bubbleTimer.Stop();
            _bubbleTimer.Start();
        }

        private void HideBubble()
        {
            if (!_bubbleOn) return;
            _bubbleOn = false;
            _bubbleLayer.IsHitTestVisible = false;
            _collapsedLayer.IsHitTestVisible = true;
            Fade(_bubbleLayer, 0, 150);
            _collapsedLayer.BeginAnimation(OpacityProperty, null);
            _collapsedLayer.Visibility = Visibility.Visible;
            _collapsedLayer.Opacity = 1;
            FitWindow(CollapsedW, CollapsedH);
            _sw.To(CollapsedW);
        }

        /// <summary>立刻复位气泡（展开/收起时用，避免残留压住面板）。</summary>
        private void ResetBubble()
        {
            if (!_bubbleOn) return;
            _bubbleOn = false;
            _bubbleTimer.Stop();
            _bubbleLayer.BeginAnimation(OpacityProperty, null);
            _bubbleLayer.Opacity = 0;
            _bubbleLayer.IsHitTestVisible = false;
            _collapsedLayer.IsHitTestVisible = true;
        }

        private void RefreshWxCard()
        {
            for (int i = 0; i < 3; i++)
            {
                bool has = i < _wxMessages.Count;
                if (_wxSender[i] != null) _wxSender[i].Visibility = has ? Visibility.Visible : Visibility.Collapsed;
                if (_wxBody[i] != null) _wxBody[i].Visibility = has ? Visibility.Visible : Visibility.Collapsed;
                if (_wxTime[i] != null) _wxTime[i].Visibility = has ? Visibility.Visible : Visibility.Collapsed;
                if (!has) continue;

                var m = _wxMessages[i];
                _wxSender[i].Text = m.Sender;
                _wxBody[i].Text = m.Body;
                _wxTime[i].Text = ShortTime(m.Time);
            }

            if (_wxEmpty != null)
            {
                if (_wxMessages.Count == 0)
                {
                    _wxEmpty.Text = "暂无新消息";
                    _wxEmpty.Visibility = Visibility.Visible;
                }
                else _wxEmpty.Visibility = Visibility.Collapsed;
            }
        }

        private static string ShortTime(DateTime t)
        {
            var now = DateTime.Now;
            if (t.Date == now.Date) return t.ToString("HH:mm", CultureInfo.InvariantCulture);
            if (t.Date == now.AddDays(-1).Date) return "昨天";
            return t.ToString("M/d", CultureInfo.InvariantCulture);
        }

        /// <summary>仅用于自检/截图：灌入假消息走一遍真实 UI 流程。</summary>
        public void DemoWeChat()
        {
            var fake = new List<WeChatFeed.Message>();
            fake.Add(new WeChatFeed.Message { Sender = "张三", Body = "晚上的会改到八点，你看行吗？", Order = 1003, Time = DateTime.Now });
            fake.Add(new WeChatFeed.Message { Sender = "产品-李雷", Body = "灵动岛那版视觉稿我更新了", Order = 1002, Time = DateTime.Now.AddMinutes(-26) });
            fake.Add(new WeChatFeed.Message { Sender = "设计群", Body = "王五：图标已切完，等联调", Order = 1001, Time = DateTime.Now.AddHours(-3) });

            _wxMessages.Clear();
            _wxMessages.AddRange(fake);
            RefreshWxCard();
            ShowBubble(fake[0]);
        }

        private void SetMetric(TextBlock value, TextBlock sub, GlassBar bar, string text, string subText, double ratio)
        {
            if (value != null) value.Text = text;
            if (sub != null) sub.Text = subText;
            if (bar != null) bar.Ratio = ratio;
        }

        // ------------------------------------------------------------------
        //  Helpers & lifecycle
        // ------------------------------------------------------------------
        [DllImport("user32.dll")]
        private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

        private static void SendMediaKey(byte vk)
        {
            keybd_event(vk, 0, 0, UIntPtr.Zero);
            keybd_event(vk, 0, 2, UIntPtr.Zero);
        }

        private static void Run(string file, string args)
        {
            try
            {
                Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = true });
            }
            catch { }
        }
    }
}

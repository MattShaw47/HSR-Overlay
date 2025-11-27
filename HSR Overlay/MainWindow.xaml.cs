using HSR_Overlay.Interop;
using HSR_Overlay.Services;
using HSR_Overlay.Util;
using HSR_Overlay.Services.Probing;
using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.IO;
using HSR_Overlay.Services.Capture;
using System;
using System.Drawing;
using HSR_Overlay.Services.Ocr;
using MBrushes = System.Windows.Media.Brushes;
using WPoint = System.Windows.Point;
using DRectangle = System.Drawing.Rectangle;
using HSR_Overlay.Services.Analysis;
using HSR_Overlay.Ui.Modules;
using HSR_Overlay.Services.State;

namespace HSR_Overlay;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private const string GameProcessName = "StarRail";
    private const string GameWindowTitleHint = "Honkai: Star Rail";

    private IntPtr _hwnd; // overlay hwnd
    private IntPtr _gameHwnd; // game hwnd

    private int HOTKEY_ID_MENU = 1;

    private readonly OverlayMenuViewModel _overlayMenu;
    private readonly Dictionary<ModuleCategory, CategoryPanelView> _categoryPanels = new();
    private bool _menuVisible;

    private bool _draggingMenu;
    private WPoint _menuDragStart;
    private Thickness _menuStartMargin;

    private readonly GameLocator _locator = new(GameProcessName, GameWindowTitleHint);
    private readonly ZOrderController _zOrder = new();
    private readonly OverlayTracker _tracker;
    private readonly ForegroundWatcher _fgWatcher;

    private readonly DispatcherTimer _trackTimer = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private readonly DispatcherTimer _captureTimer = new() { Interval = TimeSpan.FromMilliseconds(10) };

    private readonly DispatcherTimer _charOcrTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private bool _charScreenActive;
    private bool _charOcrInProgress;

    private RelicPopupController _popup;
    private readonly DispatcherTimer _relicOcrTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private bool _relicModalActive;
    private bool _relicOcrInProgress;


    private readonly CaptureService _capture = new();
    private bool _showProbeMarkers = true;
    private readonly List<SentinelDebugPoint> _sentinelDebugPoints = new();
    private readonly Dictionary<string, List<SentinelDebugPoint>> _sentinelDebugByProbe = new();
    private readonly List<DRectangle> _ocrDebugRects = new();
    private string? _currentRelicSlotProbeKey;
    private bool _currentRelicHasGoodReading;



    private readonly Dictionary<string, Action<bool>> _probeStateHandlers = new();
    private readonly Dictionary<string, Action<object?>> _probeDebugHandlers = new();
    private readonly Dictionary<string, Action<CaptureService.FrameInfo>> _probeActivatedHandlers = new();

    private Settings _settingsDraft = new();

    private IRelicTextParser _relicTextParser;
    private IRelicWeightsProvider _relicWeightsProvider;
    private RelicPieceDatabase _relicPieceDb;
    private RelicStatTables _relicStatTables;
    private IRelicInventory _relicInventory;
    private CharacterNameDatabase _characterNameDb;

    public MainWindow()
    {
        InitializeComponent();

        _popup = new RelicPopupController(RelicPopup, RelicText);

        _overlayMenu = new OverlayMenuViewModel(ApplySettingsToRuntime, ToggleCategoryPanel);
        OverlayMenuHost.DataContext = _overlayMenu;

        _tracker = new OverlayTracker(
            getDpi: () => Win32.GetDpiForWindow(_hwnd),
            pxToDip: DpiHelper.PxToDip,
            onApplyBounds: (l, t, w, h) =>
            {
                Left = l; Top = t; Width = w; Height = h;
                if (Visibility != Visibility.Visible) Visibility = Visibility.Visible;
            },
            onHide: () => Visibility = Visibility.Hidden
        );

        _fgWatcher = new ForegroundWatcher(
            belongsToGame: BelongsToGameProcess,
            onGameForeground: () =>
            {
                Log.Debug("FG", "Game foreground -> raise topmost");
                _zOrder.ForceRaiseTopmost(_hwnd);
            },
            onOtherForeground: () =>
            {
                Log.Debug("FG", "Other foreground -> drop topmost");
                _zOrder.SetOverlayTopmost(_hwnd, false);
                //_popup?.Hide();
            }
        );

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // 1) logging + window interop
        Log.Init(source: "overlay", min: Microsoft.Extensions.Logging.LogLevel.Debug);
        _hwnd = new WindowInteropHelper(this).Handle;
        var src = HwndSource.FromHwnd(_hwnd);
        src.CompositionTarget.BackgroundColor = Colors.Transparent;
        src.AddHook(WndProc);

        // 2) settings -> runtime
        Settings.Load();
        ApplySettingsToRuntime();
        _showProbeMarkers = Settings.Current.DebugVisualizationEnabled;

        _overlayMenu.BuildDefaultModules();

        // 3) initial game hwnd + start trackers
        _gameHwnd = _locator.FindGameWindow();
        _trackTimer.Tick += (_, __) => TrackGameWindow();
        _trackTimer.Start();

        // 4) register probes
        // farming relic probes
        var relicSentinels = new[]
        {
            // top left
            SentinelProbe.Sentinel.Normalized(0.21, 0.30, 0xFFD3D3D3, tol: 18),
            // top right
            SentinelProbe.Sentinel.Normalized(0.79,  0.30, 0xFFD3D3D3, tol: 18),
            // black bar next to 5th star
            SentinelProbe.Sentinel.Normalized(0.325, 0.59, 0xFF282828, tol: 18),
            // 5th star
            SentinelProbe.Sentinel.Normalized(0.3315,0.595,0xFFFFCF70, tol: 18),
        };

        _capture.RegisterProbe(new SentinelProbe(
            key: "relic-modal",
            sentinels: relicSentinels,
            requiredHitRatio: Settings.Current.RequiredHitRatio,
            hysteresis: 1
        ));

        _probeDebugHandlers["relic-modal"] = payload => { DrawSentinelPoints("relic-modal", payload); };

        // character screen probes
        var charRelicSentinels = new[]
        {
            // head icon (top left)
            SentinelProbe.Sentinel.Normalized(0.04, 0.05, 0xFFBCA679, tol: 20),
            // whitespace in sort by relic recommendation
            SentinelProbe.Sentinel.Normalized(0.16, 0.92, 0xFFE3E4E9, tol: 20),
            // center of orange relic dot next to lvl number (center of screen)
            SentinelProbe.Sentinel.Normalized(0.482, 0.699, 0xFFB78D61, tol:25),
            // on R in Remove button, to ensure on the equipped relic.
            SentinelProbe.Sentinel.Normalized(0.8017, 0.9187, 0xFF121212, tol:25)
        };

        _capture.RegisterProbe(new SentinelProbe(
            key: "char-relics",
            sentinels: charRelicSentinels,
            requiredHitRatio: Settings.Current.RequiredHitRatio,
            hysteresis: 1
            ));

        _probeDebugHandlers["char-relics"] = payload => { DrawSentinelPoints("char-relics", payload); };

        // probes to detect whether relic screen is swapped between.
        var slotHeadSentinels = new[]
        {
            SentinelProbe.Sentinel.Normalized(0.067, 0.125, 0xFFFFFFFF, tol: 18),
        };
        _capture.RegisterProbe(new SentinelProbe(
            key: "slot-head",
            sentinels: slotHeadSentinels,
            requiredHitRatio: 1.0,
            hysteresis: 1));


        var slotHandsSentinels = new[]
        {
            SentinelProbe.Sentinel.Normalized(0.102, 0.125, 0xFFFFFFFF, tol: 18),
        };
        _capture.RegisterProbe(new SentinelProbe(
            key: "slot-hands",
            sentinels: slotHandsSentinels,
            requiredHitRatio: 1.0,
            hysteresis: 1));

        var slotBodySentinels = new[]
        {
            SentinelProbe.Sentinel.Normalized(0.14, 0.137, 0xFFFFFFFF, tol: 18),
        };
        _capture.RegisterProbe(new SentinelProbe(
            key: "slot-body",
            sentinels: slotBodySentinels,
            requiredHitRatio: 1.0,
            hysteresis: 1));

        var slotFeetSentinels = new[]
        {
            SentinelProbe.Sentinel.Normalized(0.177, 0.145, 0xFFFFFFFF, tol: 18),
        };
        _capture.RegisterProbe(new SentinelProbe(
            key: "slot-feet",
            sentinels: slotFeetSentinels,
            requiredHitRatio: 1.0,
            hysteresis: 1));

        var slotOrbSentinels = new[]
        {
            SentinelProbe.Sentinel.Normalized(0.21, 0.147, 0xFFFFFFFF, tol: 18),
        };
        _capture.RegisterProbe(new SentinelProbe(
            key: "slot-orb",
            sentinels: slotOrbSentinels,
            requiredHitRatio: 1.0,
            hysteresis: 1));

        var slotRopeSentinels = new[]
        {
            SentinelProbe.Sentinel.Normalized(0.237, 0.141, 0xFFFFFFFF, tol: 18),
        };
        _capture.RegisterProbe(new SentinelProbe(
            key: "slot-rope",
            sentinels: slotRopeSentinels,
            requiredHitRatio: 1.0,
            hysteresis: 1));


        WireRuntimeEvents();

        // database / relic parser initialization
        string jsonPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");

        _relicPieceDb = RelicPieceDatabase.Load(System.IO.Path.Combine(jsonPath, "RelicNames.json"));
        _characterNameDb = CharacterNameDatabase.Load(System.IO.Path.Combine(jsonPath, "Characters.json"));
        _relicTextParser = new RelicTextParser(_relicPieceDb);
        var emptyProfiles = new Dictionary<string, CharacterRelicProfile>();
        _relicWeightsProvider = new RelicWeightsProvider(emptyProfiles);
        _relicStatTables = new RelicStatTables(RelicStatConfig.Load(System.IO.Path.Combine(jsonPath, "RelicStats.json")));
        _relicInventory = new RelicInventory(jsonPath);
        // load character profiles here as well
        

        // periodic ocr while relic modal is active
        _relicOcrTimer.Tick += (_, __) =>
        {
            if (!_relicModalActive) return;
            if (_relicOcrInProgress) return;
            if (!_capture.HasLastFrame) return;

            // Use the most recent frame snapshot
            StartRelicOcr(_capture.LastFrame);
        };

        _charOcrTimer.Tick += (_, __) =>
        {
            if (!_charScreenActive) return;
            if (_charOcrInProgress) return;
            if (!_capture.HasLastFrame) return;

            StartCharacterRelicOcr(_capture.LastFrame);
        };

        // 5) capture loop
        _captureTimer.Tick += (_, __) =>
        {
            if (_gameHwnd != IntPtr.Zero && Win32.IsWindow(_gameHwnd))
                _capture.TryCaptureOnce(_gameHwnd);
        };
        _captureTimer.Start();

        // 6) fg watcher
        _fgWatcher.Start();

        // 7) UI buttons
        GearButtonHost.MouseLeftButtonUp += (_, __) => ToggleOverlayMenu();
        BtnSave.Click += (_, __) =>
        {
            Settings.Current.CopyFrom(_settingsDraft);
            Settings.Save();
            ApplySettingsToRuntime();
            _showProbeMarkers = Settings.Current.DebugVisualizationEnabled;
            ConfigPanel.Visibility = Visibility.Collapsed;
        };
        BtnClose.Click += (_, __) => { ConfigPanel.Visibility = Visibility.Collapsed; };
    }

    private void UpdateRelicFoundIndicator(bool found)
    {
        if (!Settings.Current.ShowRelicFoundIndicator || !_charScreenActive)
        {
            RelicFoundIndicator.Visibility = Visibility.Collapsed;
            return;
        }

        RelicFoundIndicator.Visibility = Visibility.Visible;
        RelicFoundIndicator.Background = found ? MBrushes.DarkGreen : MBrushes.DarkRed;
        RelicFoundIndicatorText.Text = found ? "Relic found" : "Relic not found";
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        Settings.Save();
        _trackTimer.Stop();
        _captureTimer.Stop();
        _fgWatcher.Dispose();
        _capture.Dispose();
        base.OnClosed(e);
    }

    private void ApplySettingsToRuntime()
    {
        Log.Debug("Settings", "Attempting to apply settings.");

        // Capture frequency
        _captureTimer.Interval = TimeSpan.FromMilliseconds(Settings.Current.CaptureIntervalMs);

        // Probe visualization (overlay side only)
        ProbeLayer.Visibility = Settings.Current.DebugVisualizationEnabled ? Visibility.Visible : Visibility.Collapsed;
        if (!Settings.Current.DebugVisualizationEnabled) ProbeLayer.Children.Clear();

        // Hit ratio    
        _capture.RequiredHitRatio = Settings.Current.RequiredHitRatio;

        // Logger level
        Log.Init(min: Settings.Current.LogLevel);

        // relic found indicator visibility
        if (Settings.Current.ShowRelicFoundIndicator && _charScreenActive)
        {
            RelicFoundIndicator.Visibility = Visibility.Visible;
            RelicFoundIndicator.Background = MBrushes.DarkRed;
            RelicFoundIndicatorText.Text = "Relic not found, wait a second";
        }
        else
        {
            RelicFoundIndicator.Visibility = Visibility.Collapsed;
        }
    }

    private void OverlayMenuHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;

        _draggingMenu = true;
        _menuDragStart = e.GetPosition(this);
        _menuStartMargin = OverlayMenuHost.Margin;
        Mouse.Capture((IInputElement)sender);
    }

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_draggingMenu) return;

        var pos = e.GetPosition(this);
        var dx = pos.X - _menuDragStart.X;
        var dy = pos.Y - _menuDragStart.Y;

        OverlayMenuHost.Margin = new Thickness(
            _menuStartMargin.Left + dx,
            _menuStartMargin.Top + dy,
            0,
            0);
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_draggingMenu) return;
        _draggingMenu = false;
        Mouse.Capture(null);
    }

    private void TrackGameWindow()
    {
        if (_gameHwnd == IntPtr.Zero || !Win32.IsWindow(_gameHwnd))
            _gameHwnd = _locator.FindGameWindow();

        // hide if missing/minimized
        if (_gameHwnd == IntPtr.Zero || Win32.IsIconic(_gameHwnd))
        {
            Log.Debug("Track", "HideOverlay: missing or iconic");
            _tracker.HideOverlay();
            _popup?.Hide();
            return;
        }

        if (!Win32.GetClientRect(_gameHwnd, out var client))
        {
            Log.Debug("Track", "HideOverlay: GetClient failed");
            _tracker.HideOverlay();
            _popup?.Hide();
            return;
        }

        var topLeft = new Win32.POINT { X = 0, Y = 0 };
        Win32.ClientToScreen(_gameHwnd, ref topLeft);
        int wPx = Math.Max(1, client.Right - client.Left);
        int hPx = Math.Max(1, client.Bottom - client.Top);

        // px -> DIP using overlay window's DPI
        var dpi = Win32.GetDpiForWindow(_hwnd);
        double leftDip = DpiHelper.PxToDip(topLeft.X, dpi);
        double topDip = DpiHelper.PxToDip(topLeft.Y, dpi);
        double wDip = DpiHelper.PxToDip(wPx, dpi);
        double hDip = DpiHelper.PxToDip(hPx, dpi);

        _tracker.ApplyBounds(leftDip, topDip, wDip, hDip);
        Log.Trace("Track",
            $"ApplyBounds px=({client.Right - client.Left}x{client.Bottom - client.Top}) dip=({wDip:N1}x{hDip:N1}) at ({leftDip:N1},{topDip:N1})");
        _tracker.ApplyBounds(leftDip, topDip, wDip, hDip);

        // if game is foreground, keep freshly raised
        var fg = Win32.GetForegroundWindow();
        var fgRoot = fg != IntPtr.Zero ? Win32.GetAncestor(fg, Win32Constants.GA_ROOT) : IntPtr.Zero;
        if (fgRoot != IntPtr.Zero && BelongsToGameProcess(fgRoot))
        {
            _zOrder.ForceRaiseTopmost(_hwnd);
        }
    }

    private bool BelongsToGameProcess(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        Win32.GetWindowThreadProcessId(hwnd, out uint pid1);
        foreach (var p in System.Diagnostics.Process.GetProcessesByName(GameProcessName))
            if ((uint)p.Id == pid1) return true;
        return false;
    }
}
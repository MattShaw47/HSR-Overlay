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

    private readonly GameLocator _locator = new(GameProcessName, GameWindowTitleHint);
    private readonly ZOrderController _zOrder = new();
    private readonly OverlayTracker _tracker;
    private readonly ForegroundWatcher _fgWatcher;

    private readonly DispatcherTimer _trackTimer = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private readonly DispatcherTimer _captureTimer = new() { Interval = TimeSpan.FromMilliseconds(10) };

    private RelicPopupController _popup;
    private readonly DispatcherTimer _relicOcrTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private bool _relicModalActive;
    private bool _relicOcrInProgress;


    private readonly CaptureService _capture = new();
    private bool _showProbeMarkers = true;
    private IEnumerable<SentinelDebugPoint>? _lastSentinelDebug;
    private DRectangle _lastOcrRect;
    private bool _hasOcrRect;


    private readonly Dictionary<string, Action<bool>> _probeStateHandlers = new();
    private readonly Dictionary<string, Action<object?>> _probeDebugHandlers = new();
    private readonly Dictionary<string, Action<CaptureService.FrameInfo>> _probeActivatedHandlers = new();

    private Settings _settingsDraft = new();

    private IRelicTextParser _relicTextParser;
    private IRelicWeightsProvider _relicWeightsProvider;
    private RelicPieceDatabase _relicPieceDb;
    private RelicStatTables _relicStatTables;

    public MainWindow()
    {
        InitializeComponent();

        _popup = new RelicPopupController(RelicPopup, RelicText);

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

        // 3) initial game hwnd + start trackers
        _gameHwnd = _locator.FindGameWindow();
        _trackTimer.Tick += (_, __) => TrackGameWindow();
        _trackTimer.Start();

        // 4) register probes (relic modal)
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

        WireRuntimeEvents();

        // database / relic parser initialization
        string jsonPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");

        _relicPieceDb = RelicPieceDatabase.Load(System.IO.Path.Combine(jsonPath, "RelicNames.json"));
        _relicTextParser = new RelicTextParser(_relicPieceDb);
        var emptyProfiles = new Dictionary<string, CharacterRelicProfile>();
        _relicWeightsProvider = new RelicWeightsProvider(emptyProfiles);
        _relicStatTables = new RelicStatTables(RelicStatConfig.Load(System.IO.Path.Combine(jsonPath, "RelicStats.json")));
        // load character profiles here as well
        // load stored relic inventory

        // periodic ocr while relic modal is active
        _relicOcrTimer.Tick += (_, __) =>
        {
            if (!_relicModalActive) return;
            if (_relicOcrInProgress) return;
            if (!_capture.HasLastFrame) return;

            // Use the most recent frame snapshot
            StartRelicOcr(_capture.LastFrame);
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
        GearButtonHost.MouseLeftButtonUp += (_, __) => ToggleConfigPanel();
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

    private void OnClosed(object? sender, EventArgs e)
    {
        Settings.Save();
        _trackTimer.Stop();
        _captureTimer.Stop();
        _fgWatcher.Dispose();
        _capture.Dispose();
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
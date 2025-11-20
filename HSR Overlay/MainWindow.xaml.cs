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
        _relicPieceDb = RelicPieceDatabase.Load("/data/relicNames.json");
        _relicTextParser = new RelicTextParser(_relicPieceDb);
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

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_NCHITTEST = 0x0084;
        if (msg != WM_NCHITTEST) return IntPtr.Zero;

        int x = (short)((int)lParam & 0xFFFF);
        int y = (short)(((int)lParam >> 16) & 0xFFFF);
        var clientPt = PointFromScreen(new WPoint(x, y));

        // VisualTree hit test
        DependencyObject? hit = null;
        VisualTreeHelper.HitTest(this, null, r => { hit = r.VisualHit; return HitTestResultBehavior.Stop; }, new PointHitTestParameters(clientPt));

        // Walk up the tree to see if any ancestor is marked IsInteractive
        bool overInteractive = false;
        for (var d = hit; d != null; d = VisualTreeHelper.GetParent(d))
        {
            if (d is FrameworkElement fe && OverlayHitTest.GetIsInteractive(fe))
            {
                overInteractive = true;
                break;
            }
        }

        handled = true;
        return overInteractive ? (IntPtr)1 /*HTCLIENT*/ : (IntPtr)(-1) /*HTTRANSPARENT*/;
    }

    private void ToggleConfigPanel()
    {
        if (ConfigPanel.Visibility != Visibility.Visible)
        {
            // Open: clone current into draft and bind
            _settingsDraft = Settings.Current.DeepCopy();
            ConfigPanel.DataContext = _settingsDraft;
            ConfigPanel.Visibility = Visibility.Visible;
        }
        else
        {
            // Close without saving
            ConfigPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void WireRuntimeEvents()
    {
        _capture.ProbeStateChanged += OnProbeStateChanged;
        _capture.ProbeDebugTapped += OnProbeDebugTapped;
        _capture.ProbeActivated += OnProbeActivated;

        _probeStateHandlers["relic-modal"] = active =>
        {
            _relicModalActive = active;
            ShowRelicPopup(active);

            if (active)
            {
                if (!_relicOcrTimer.IsEnabled)
                    _relicOcrTimer.Start();
            }
            else
            {
                _relicOcrTimer.Stop();
            }
        };

        _probeDebugHandlers["relic-modal"] = payload => DrawSentinelPoints(payload);
        _probeActivatedHandlers["relic-modal"] = frame => StartRelicOcr(frame);
    }

    private void UnwireRuntimeEvents()
    {
        _capture.ProbeStateChanged -= OnProbeStateChanged;
        _capture.ProbeDebugTapped -= OnProbeDebugTapped;
    }

    private void OnProbeStateChanged(string key, bool active)
    {
        if (_probeStateHandlers.TryGetValue(key, out var handler))
            handler(active);
    }

    private void OnProbeDebugTapped(string key, object? payload)
    {
        if (_probeDebugHandlers.TryGetValue(key, out var handler))
            handler(payload);
    }

    private void OnProbeActivated(string key, CaptureService.FrameInfo frame)
    {
        if (_probeActivatedHandlers.TryGetValue(key, out var handler))
            handler(frame);
    }

    private void ShowRelicPopup(bool active)
    {
        Dispatcher.Invoke(() =>
        {
            if (!Settings.Current.EnableRelicPopup)
            {
                _popup.Hide();
                return;
            }

            if (active) _popup.Update("Relic detected — scanning...");
            else _popup.Hide();
        });
    }

    private void DrawSentinelPoints(object? payload)
    {
        if (!Settings.Current.DebugVisualizationEnabled) return;
        if (payload is not IEnumerable<SentinelDebugPoint> pts) return;

        // cache and redraw
        _lastSentinelDebug = pts.ToArray();
        RenderDebugOverlay();
    }

    private void DrawOcrRect(DRectangle roi)
    {
        if (!Settings.Current.DebugVisualizationEnabled) return;
        if (roi.Width <= 0 || roi.Height <= 0) return;

        _lastOcrRect = roi;
        _hasOcrRect = true;
        RenderDebugOverlay();
    }

    private void RenderDebugOverlay()
    {
        if (!Settings.Current.DebugVisualizationEnabled) return;

        Dispatcher.Invoke(() =>
        {
            ProbeLayer.Children.Clear();

            const double r = 4.0;

            // 1) Sentinel dots
            if (_lastSentinelDebug is not null)
            {
                foreach (var p in _lastSentinelDebug)
                {
                    var local = PointFromScreen(new WPoint(p.Cx, p.Cy));
                    var dot = new Ellipse
                    {
                        Width = r * 2,
                        Height = r * 2,
                        StrokeThickness = 2,
                        Stroke = p.Match ? MBrushes.Lime : MBrushes.Red,
                        Fill = MBrushes.Transparent,
                        IsHitTestVisible = false
                    };
                    Canvas.SetLeft(dot, local.X - r);
                    Canvas.SetTop(dot, local.Y - r);
                    ProbeLayer.Children.Add(dot);
                }
            }

            // 2) OCR ROI rectangle
            if (_hasOcrRect)
            {
                // screen → overlay
                var topLeft = PointFromScreen(new WPoint(_lastOcrRect.Left, _lastOcrRect.Top));
                var bottomRight = PointFromScreen(new WPoint(_lastOcrRect.Right, _lastOcrRect.Bottom));

                var w = Math.Max(0, bottomRight.X - topLeft.X);
                var h = Math.Max(0, bottomRight.Y - topLeft.Y);

                var rect = new System.Windows.Shapes.Rectangle
                {
                    Width = w,
                    Height = h,
                    StrokeThickness = 2,
                    Stroke = MBrushes.Yellow,
                    Fill = System.Windows.Media.Brushes.Transparent,
                    IsHitTestVisible = false
                };

                Canvas.SetLeft(rect, topLeft.X);
                Canvas.SetTop(rect, topLeft.Y);
                ProbeLayer.Children.Add(rect);
            }
        });
    }



    // Fired once per activation
    private async void StartRelicOcr(CaptureService.FrameInfo frame)
    {
        if (!_relicModalActive) return;
        if (!Settings.Current.EnableRelicPopup) return;
        if (!_capture.HasLastFrame) return;
        if (_relicOcrInProgress) return;

        _relicOcrInProgress = true;

        Log.Debug("ocr", "attempting to start ocr");

        try
        {
            // Define the screen-space OCR rect for relics.
            // Tune these normalized numbers to your UI; they’re just placeholders.
            DRectangle roi = GetRelicOcrScreenRect(frame);

            DrawOcrRect(roi);

            using var bmp = _capture.CaptureRegionToBitmap(roi);
            string text = await OcrReader.ReadTextAsync(bmp);

            ParsedRelic foundRelic = _relicTextParser.Parse(text, new RelicTextContext(RelicTextSource.Farming));

            // TODO REPLACE LAST ARG WITH THE ACTUAL COMPARISON RELIC
            List<RelicEvaluation> relicEvaluations = RelicAnalyzer.Analyze(foundRelic, _relicWeightsProvider, _relicPieceDb, new ParsedRelic());
            
            // once the list of relic evaluations is returned, process into string and display on popup.

            Dispatcher.Invoke(() =>
            {
                if (Settings.Current.EnableRelicPopup)
                    _popup.Update(string.IsNullOrWhiteSpace(text) ? "No text found." : text);
            });
        }
        finally
        {
            _relicOcrInProgress = false;
        }
        
    }

    // Example: compute OCR ROI from normalized rectangle in client coords
    private static DRectangle GetRelicOcrScreenRect(CaptureService.FrameInfo f)
    {
        // Example normalized box; replace with the actual area you want
        const double nx = 0.41, ny = 0.31, nw = 0.4, nh = 0.285;

        int x = f.OriginX + (int)(nx * f.ClientWidth);
        int y = f.OriginY + (int)(ny * f.ClientHeight);
        int w = (int)(nw * f.ClientWidth);
        int h = (int)(nh * f.ClientHeight);

        // Safety clamp
        if (w <= 0 || h <= 0) return DRectangle.Empty;
        return new DRectangle(x, y, w, h);
    }   
}
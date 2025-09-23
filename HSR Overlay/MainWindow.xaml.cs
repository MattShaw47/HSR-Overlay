using HSR_Overlay.Interop;
using HSR_Overlay.Services;
using HSR_Overlay.Util;
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
    private readonly DispatcherTimer _captureTimer = new() { Interval = TimeSpan.FromMilliseconds(333) };

    private RelicPopupController _popup;

    private readonly CaptureService _capture = new();
    private bool _showProbeMarkers = true;

    private Settings _settingsDraft = new();

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
            onOtherForeground: () => {
                Log.Debug("FG", "Other foreground -> drop topmost");
                _zOrder.SetOverlayTopmost(_hwnd, false);
                _popup?.Hide();
            }
        );

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Log.Init(source: "overlay", min: Microsoft.Extensions.Logging.LogLevel.Debug);
        _hwnd = new WindowInteropHelper(this).Handle;

        // transparent composition + message hook
        var src = HwndSource.FromHwnd(_hwnd);
        src.CompositionTarget.BackgroundColor = System.Windows.Media.Colors.Transparent;
        src.AddHook(WndProc);

        Settings.Load();
        ApplySettingsToRuntime();

        // initial find
        _gameHwnd = _locator.FindGameWindow();

        Log.Debug("overlay", "Starting timer");
        // tracking timer
        _trackTimer.Tick += (_, __) => TrackGameWindow();
        _trackTimer.Start();

        _capture.RelicPresenceChanged += present =>
        {
            Dispatcher.Invoke(() =>
            {
                if (!Settings.Current.EnableRelicPopup)
                {
                    RelicPopup.Visibility = Visibility.Collapsed;
                    RelicText.Text = string.Empty;
                    return;
                }
                if (present)
                {
                    RelicText.Text = "Relic detected — scanning…";
                    RelicPopup.Visibility = Visibility.Visible;
                }
                else
                {
                    RelicPopup.Visibility = Visibility.Collapsed;
                    RelicText.Text = string.Empty;
                }
            });
        };

        Log.Debug("overlay", "Loading relic vis");
        _capture.DebugVisualizationEnabled = Settings.Current.DebugVisualizationEnabled;
        _capture.requiredHitRatio = Settings.Current.RequiredHitRatio;

        _capture.DebugProbesTapped += points =>
        {
            if (!Settings.Current.DebugVisualizationEnabled) return;

            Dispatcher.Invoke(() =>
            {
                ProbeLayer.Children.Clear();
                const double r = 3.0;

                foreach (var p in points)
                {
                    var local = this.PointFromScreen(new Point(p.ScreenX, p.ScreenY));

                    var dot = new Ellipse
                    {
                        Width = r * 2,
                        Height = r * 2,
                        StrokeThickness = 1,
                        Stroke = p.Match ? Brushes.Lime : Brushes.Red,
                        Fill = p.Match ? Brushes.Lime : Brushes.Red,
                        Opacity = 0.9
                    };

                    Canvas.SetLeft(dot, local.X - r);
                    Canvas.SetTop(dot, local.Y - r);
                    ProbeLayer.Children.Add(dot);
                }
            });
        };

        HookProbeVisualization();

        // capture loop 
        _captureTimer.Tick += (_, __) =>
        {
            if (_gameHwnd != IntPtr.Zero && Win32.IsWindow(_gameHwnd))
            {
                _capture.TryCaptureOnce(_gameHwnd);
            }
        };
        _captureTimer.Start();


        // win-event hook for foreground change
        _fgWatcher.Start();

        GearButtonHost.MouseLeftButtonUp += (_, __) => ToggleConfigPanel();

        // Save button
        BtnSave.Click += (_, __) =>
        {
            Settings.Current.CopyFrom(_settingsDraft);
            Settings.Save();
            ApplySettingsToRuntime();
            ConfigPanel.Visibility = Visibility.Collapsed;
        };

        // Cancel button
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
        // Capture frequency
        _captureTimer.Interval = TimeSpan.FromMilliseconds(Settings.Current.CaptureIntervalMs);

        // Probe visualization
        _capture.DebugVisualizationEnabled = Settings.Current.DebugVisualizationEnabled;
        ProbeLayer.Visibility = Settings.Current.DebugVisualizationEnabled ? Visibility.Visible : Visibility.Collapsed;
        if (!Settings.Current.DebugVisualizationEnabled) ProbeLayer.Children.Clear();

        // Hit ratio
        _capture.requiredHitRatio = Settings.Current.RequiredHitRatio;

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
            Log.Debug("Track", "HideOverlay: misising or iconic");
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
        var clientPt = PointFromScreen(new System.Windows.Point(x, y));

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

    private void HookProbeVisualization()
    {
        _capture.DebugProbesTapped += pts =>
        {
            if (!_showProbeMarkers)
            {
                // Clear if someone disabled it mid-run
                if (ProbeLayer.Visibility == Visibility.Visible)
                    Dispatcher.Invoke(() => { ProbeLayer.Children.Clear(); ProbeLayer.Visibility = Visibility.Collapsed; });
                return;
            }

            Dispatcher.Invoke(() =>
            {
                // Ensure visible
                if (ProbeLayer.Visibility != Visibility.Visible) ProbeLayer.Visibility = Visibility.Visible;
                ProbeLayer.Children.Clear();

                double w = this.ActualWidth;
                double h = this.ActualHeight;
                const double rad = 5.0;

                foreach (var p in pts)
                {
                    double x = p.U * w;
                    double y = p.V * h;

                    var dot = new Ellipse
                    {
                        Width = rad * 2,
                        Height = rad * 2,
                        Stroke = p.Match ? Brushes.Lime : Brushes.Red,
                        Fill = Brushes.Transparent,
                        StrokeThickness = 2,
                        IsHitTestVisible = false
                    };
                    Canvas.SetLeft(dot, x - rad);
                    Canvas.SetTop(dot, y - rad);
                    ProbeLayer.Children.Add(dot);
                }
            });
        };
    }
}
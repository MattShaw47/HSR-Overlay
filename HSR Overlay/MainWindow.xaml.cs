using HSR_Overlay.Interop;
using HSR_Overlay.Services;
using HSR_Overlay.Util;
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

    private readonly CaptureService _capture = new(); // stubbed for now

    public MainWindow()
    {
        InitializeComponent();

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
                // strong raise into topmost band
                _zOrder.ForceRaiseTopmost(_hwnd);
            },
            onOtherForeground: () => _zOrder.SetOverlayTopmost(_hwnd, false)
        );

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;

        // transparent composition + message hook
        var src = HwndSource.FromHwnd(_hwnd);
        src.CompositionTarget.BackgroundColor = System.Windows.Media.Colors.Transparent;
        src.AddHook(WndProc);

        // initial find
        _gameHwnd = _locator.FindGameWindow();

        // tracking timer
        _trackTimer.Tick += (_, __) => TrackGameWindow();
        _trackTimer.Start();

        // capture loop 
        _captureTimer.Tick += (_, __) =>
        {
            if (_gameHwnd != IntPtr.Zero && Win32.IsWindow(_gameHwnd))
            {
                // for image capture, stubbed for now
                _capture.TryCaptureOnce(_gameHwnd);
            }
        };
        _captureTimer.Start();

        // win-event hook for foreground change
        _fgWatcher.Start();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _trackTimer.Stop();
        _captureTimer.Stop();
        _fgWatcher.Dispose();
        _capture.Dispose();
    }

    // tracking & alignment 
    private void TrackGameWindow()
    {
        if (_gameHwnd == IntPtr.Zero || !Win32.IsWindow(_gameHwnd))
            _gameHwnd = _locator.FindGameWindow();

        // hide if missing/minimized
        if (_gameHwnd == IntPtr.Zero || Win32.IsIconic(_gameHwnd))
        {
            _tracker.HideOverlay();
            return;
        }

        if (!Win32.GetClientRect(_gameHwnd, out var client))
        {
            _tracker.HideOverlay();
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

    // click-through except “Dropdown”
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_NCHITTEST = 0x0084;
        if (msg != WM_NCHITTEST) return IntPtr.Zero;

        int x = (short)((int)lParam & 0xFFFF);
        int y = (short)(((int)lParam >> 16) & 0xFFFF);
        var clientPt = PointFromScreen(new System.Windows.Point(x, y));

        // VisualTree hit test
        DependencyObject? hit = null;
        VisualTreeHelper.HitTest(this,
            /* filter */ null,
            /* result */ r => { hit = r.VisualHit; return HitTestResultBehavior.Stop; },
            /* params */ new PointHitTestParameters(clientPt));

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
}
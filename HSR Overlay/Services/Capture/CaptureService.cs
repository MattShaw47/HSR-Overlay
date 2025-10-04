using HSR_Overlay.Interop;
using HSR_Overlay.Services.Probing;
using HSR_Overlay.Util;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Media;

namespace HSR_Overlay.Services.Capture;

internal sealed class CaptureService : IDisposable
{
    // Frame snapshot
    public readonly struct FrameInfo
    {
        public FrameInfo(nint hwnd, int ox, int oy, int w, int h)
        { GameHwnd = hwnd; OriginX = ox; OriginY = oy; ClientWidth = w; ClientHeight = h; }

        public nint GameHwnd { get; }
        public int OriginX { get; }
        public int OriginY { get; }
        public int ClientWidth { get; }
        public int ClientHeight { get; }
    }

    public FrameInfo LastFrame { get; private set; }
    public bool HasLastFrame => LastFrame.ClientWidth > 0 && LastFrame.ClientHeight > 0;

    //  Events & config
    private readonly Dictionary<string, bool> _last = new();
    public event Action<string, bool>? ProbeStateChanged; // (key, active) — fires on transitions
    public event Action<string, object?>? ProbeDebugTapped; // (key, debug payload)
    public event Action<string, FrameInfo>? ProbeActivated; // (key, frame) — fires once on rising edge

    public double RequiredHitRatio { get; set; } = 1.0; // kept for callers that mirror this into probes

    // Probe registry
    private readonly List<IProbe> _probes = new();
    private readonly Dictionary<string, DateTime> _lastEvalAt = new();

    public void RegisterProbe(IProbe probe)
    {
        if (probe is null) return;
        _probes.Add(probe);
        _last[probe.Key] = false;
    }

    // Capture API
    public void TryCaptureOnce(nint gameHwnd)
    {
        if (gameHwnd == nint.Zero) return;
        if (!Win32.GetClientRect(gameHwnd, out var rc)) return;

        int cw = rc.Right - rc.Left, ch = rc.Bottom - rc.Top;
        if (cw <= 0 || ch <= 0) return;

        var origin = new Win32.POINT { X = 0, Y = 0 };
        Win32.ClientToScreen(gameHwnd, ref origin);

        // Update frame snapshot so consumers can compute ROIs
        LastFrame = new FrameInfo(gameHwnd, origin.X, origin.Y, cw, ch);

        nint hdc = GetDC(nint.Zero);
        try
        {
            uint GetPixelArgb(int sx, int sy)
            {
                uint colorref = GetPixel(hdc, sx, sy); // 0x00BBGGRR
                byte r = (byte)(colorref & 0xFF);
                byte g = (byte)(colorref >> 8 & 0xFF);
                byte b = (byte)(colorref >> 16 & 0xFF);
                return 0xFFu << 24 | (uint)r << 16 | (uint)g << 8 | b;
            }

            var ctx = new ProbeContext(gameHwnd, cw, ch, origin.X, origin.Y, GetPixelArgb);

            var now = DateTime.UtcNow;
            foreach (var p in _probes)
            {
                // Optional per-probe throttling
                if (p.MinInterval is TimeSpan t)
                {
                    if (_lastEvalAt.TryGetValue(p.Key, out var lastAt) && now - lastAt < t)
                        continue;
                    _lastEvalAt[p.Key] = now;
                }

                var res = p.Evaluate(ctx);

                bool prev = _last[p.Key];
                bool cur = res.IsActive;

                if (cur != prev)
                {
                    _last[p.Key] = cur;
                    ProbeStateChanged?.Invoke(p.Key, cur);
                    if (cur)
                        ProbeActivated?.Invoke(p.Key, LastFrame); // rising edge only
                }

                if (res.Debug is not null)
                    ProbeDebugTapped?.Invoke(p.Key, res.Debug);
            }
        }
        finally { ReleaseDC(nint.Zero, hdc); }
    }

    // Grab any screen-space rectangle (BGRA32) for OCR, thumbnails, etc.
    public Bitmap CaptureRegionToBitmap(Rectangle screenRect)
    {
        var bmp = new Bitmap(screenRect.Width, screenRect.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(screenRect.Location, Point.Empty, screenRect.Size, CopyPixelOperation.SourceCopy);
        return bmp;
    }

    public void Dispose()
    {
        ProbeStateChanged = null;
        ProbeDebugTapped = null;
        ProbeActivated = null;
        _probes.Clear();
        _last.Clear();
        _lastEvalAt.Clear();
    }

    [DllImport("user32.dll")] private static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint hwnd, nint hdc);
    [DllImport("gdi32.dll")] private static extern uint GetPixel(nint hdc, int x, int y);
}

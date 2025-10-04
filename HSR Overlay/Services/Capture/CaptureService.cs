using HSR_Overlay.Interop;
using HSR_Overlay.Services.Probing;
using HSR_Overlay.Util;
using System.Runtime.InteropServices;


namespace HSR_Overlay.Services.Capture;

internal sealed class CaptureService : IDisposable
{
    // Per-probe active state + events
    private readonly Dictionary<string, bool> _last = new();
    public event Action<string, bool>? ProbeStateChanged; // (key, active)
    public event Action<string, object?>? ProbeDebugTapped; // (key, debug payload)
    public double RequiredHitRatio { get; set; } = 1.0;


    private readonly List<IProbe> _probes = new();
    private readonly Dictionary<string, DateTime> _lastEvalAt = new();

    public void RegisterProbe(IProbe probe)
    {
        if (probe is null) return;
        _probes.Add(probe);
        _last[probe.Key] = false;
    }

    public void TryCaptureOnce(nint gameHwnd)
    {
        if (gameHwnd == nint.Zero) return;
        if (!Win32.GetClientRect(gameHwnd, out var rc)) return;

        int cw = rc.Right - rc.Left, ch = rc.Bottom - rc.Top;
        if (cw <= 0 || ch <= 0) return;

        var origin = new Win32.POINT { X = 0, Y = 0 };
        Win32.ClientToScreen(gameHwnd, ref origin);

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
                if (res.IsActive != prev)
                {
                    _last[p.Key] = res.IsActive;
                    ProbeStateChanged?.Invoke(p.Key, res.IsActive);
                }

                if (res.Debug is not null)
                    ProbeDebugTapped?.Invoke(p.Key, res.Debug);
            }
        }
        finally { ReleaseDC(nint.Zero, hdc); }
    }

    public void Dispose()
    {
        ProbeStateChanged = null;
        ProbeDebugTapped = null;
        _probes.Clear();
    }

    [DllImport("user32.dll")] private static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint hwnd, nint hdc);
    [DllImport("gdi32.dll")] private static extern uint GetPixel(nint hdc, int x, int y);
}

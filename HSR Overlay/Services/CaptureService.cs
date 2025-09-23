using HSR_Overlay.Interop;
using HSR_Overlay.Util;
using System.Runtime.InteropServices;


namespace HSR_Overlay.Services;

internal sealed class CaptureService : IDisposable
{
    public event Action<bool>? RelicPresenceChanged;

    private bool _lastRelicPresence;

    public bool DebugVisualizationEnabled { get; set; } = false;

    public readonly struct ProbeVizPoint
    {
        public ProbeVizPoint(double u, double v, int screenX, int screenY, uint sampled, uint expected, bool match)
        { U = u; V = v; ScreenX = screenX; ScreenY = screenY; SampledArgb = sampled; ExpectedArgb = expected; Match = match; }
        public double U { get; }
        public double V { get; }
        public int ScreenX { get; }
        public int ScreenY { get; }
        public uint SampledArgb { get; }
        public uint ExpectedArgb { get; }
        public bool Match { get; }
    }

    // Raised on every TryCaptureOnce tick *only if* DebugVisualizationEnabled == true.
    public event Action<ProbeVizPoint[]>? DebugProbesTapped;

    // A sentinel can be absolute pixels or normalized (u,v). Expected ARGB is required.
    private readonly Sentinel[] _sentinels =
    {
            // Top left white space
            Sentinel.Normalized(0.225, 0.30, 0xFFD3D3D3, tol: 18),
            // Top right white space
            Sentinel.Normalized(0.75, 0.30, 0xFFD3D3D3, tol: 18),
            // black space next to stars
            Sentinel.Normalized(0.325, 0.59, 0xFF282828, tol: 18),
            // 5th star check
            Sentinel.Normalized(0.3315, 0.595, 0xFFFFCF70, tol: 18),
        };

    public double requiredHitRatio { get; set; } = 1.0;
    //private readonly double requiredHitRatio = 1.0;

    public void TryCaptureOnce(IntPtr gameHwnd)
    {
        if (gameHwnd == IntPtr.Zero) return;
        if (!Win32.GetClientRect(gameHwnd, out var rc)) return;

        int cw = rc.Right - rc.Left, ch = rc.Bottom - rc.Top;
        if (cw <= 0 || ch <= 0) return;

        var origin = new Win32.POINT { X = 0, Y = 0 };
        Win32.ClientToScreen(gameHwnd, ref origin);

        var taps = DebugVisualizationEnabled ? new List<ProbeVizPoint>(_sentinels.Length) : null;

        int hits = 0, total = 0;
        IntPtr hdc = GetDC(IntPtr.Zero);
        try
        {
            foreach (var s in _sentinels)
            {
                total++;
                int cx, cy;
                if (s.IsNormalized)
                {
                    cx = origin.X + Math.Clamp((int)Math.Round(s.U * cw), 0, cw - 1);
                    cy = origin.Y + Math.Clamp((int)Math.Round(s.V * ch), 0, ch - 1);
                }
                else
                {
                    cx = origin.X + s.X;
                    cy = origin.Y + s.Y;
                }

                uint color = GetPixelArgb(hdc, cx, cy);

                if (taps != null)
                {
                    double u = s.IsNormalized ? s.U : (cw <= 0 ? 0 : (double)s.X / cw);
                    double v = s.IsNormalized ? s.V : (ch <= 0 ? 0 : (double)s.Y / ch);
                    bool match = CloseEnough(color, s.ExpectedArgb, s.Tolerance);
                    taps.Add(new ProbeVizPoint(u, v, cx, cy, color, s.ExpectedArgb, match));
                }

                if (CloseEnough(color, s.ExpectedArgb, s.Tolerance)) hits++;
            }
        }
        finally { ReleaseDC(IntPtr.Zero, hdc); }

        int requiredHits = Math.Max(1, (int)Math.Ceiling(requiredHitRatio * _sentinels.Length));
        bool present = hits >= requiredHits;

        if (present != _lastRelicPresence)
        {
            _lastRelicPresence = present;
            RelicPresenceChanged?.Invoke(present);
        }

        if (taps != null) DebugProbesTapped?.Invoke(taps.ToArray());
    }

    private static bool CloseEnough(uint c1, uint c2, int tol)
    {
        tol = Math.Clamp(tol, 0, 255);
        int r1 = (int)((c1 >> 16) & 0xFF), g1 = (int)((c1 >> 8) & 0xFF), b1 = (int)(c1 & 0xFF);
        int r2 = (int)((c2 >> 16) & 0xFF), g2 = (int)((c2 >> 8) & 0xFF), b2 = (int)(c2 & 0xFF);
        return Math.Abs(r1 - r2) <= tol && Math.Abs(g1 - g2) <= tol && Math.Abs(b1 - b2) <= tol;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern uint GetPixel(IntPtr hdc, int x, int y);

    private static uint GetPixelArgb(IntPtr hdc, int screenX, int screenY)
    {
        // 0x00BBGGRR
        uint colorref = GetPixel(hdc, screenX, screenY);
        byte r = (byte)(colorref & 0xFF);
        byte g = (byte)((colorref >> 8) & 0xFF);
        byte b = (byte)((colorref >> 16) & 0xFF);
        return (0xFFu << 24) | ((uint)r << 16) | ((uint)g << 8) | b;
    }

    public void Dispose() 
    {
        RelicPresenceChanged = null;
        DebugProbesTapped = null;
    }

    private readonly struct Sentinel
    {
        // Absolute
        public Sentinel(int x, int y, uint expectedArgb, int tolerance)
        {
            X = x; Y = y; ExpectedArgb = expectedArgb; Tolerance = tolerance;
            U = V = 0; IsNormalized = false;
        }
        // Normalized
        private Sentinel(double u, double v, uint expectedArgb, int tolerance, bool _)
        {
            U = u; V = v; ExpectedArgb = expectedArgb; Tolerance = tolerance;
            X = Y = 0; IsNormalized = true;
        }

        public int X { get; }
        public int Y { get; }
        public double U { get; }
        public double V { get; }
        public bool IsNormalized { get; }
        public uint ExpectedArgb { get; }
        public int Tolerance { get; }

        public static Sentinel Normalized(double u, double v, uint argb, int tol) => new(u, v, argb, tol, true);
        public static Sentinel Absolute(int x, int y, uint argb, int tol) => new(x, y, argb, tol);
    }
}

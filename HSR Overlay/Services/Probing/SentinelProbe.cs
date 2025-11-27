using HSR_Overlay.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HSR_Overlay.Services.Probing;

public readonly record struct SentinelDebugPoint(
    bool IsNormalized,
    double U, double V,
    int X, int Y,
    int Cx, int Cy,
    uint Sampled, uint Expected,
    bool Match);

public sealed class SentinelProbe : IProbe
{
    public string Key { get; }
    public TimeSpan? MinInterval { get; }

    private readonly Sentinel[] _sentinels;
    private readonly double _requiredHitRatio;
    private readonly int _hysteresis; // consecutive ticks needed to flip
    private int _stableCount;
    private bool _lastDecision;

    public SentinelProbe(string key, Sentinel[] sentinels, double requiredHitRatio = 1.0, int hysteresis = 0, TimeSpan? minInterval = null)
    {
        Key = key;
        _sentinels = sentinels;
        _requiredHitRatio = Math.Clamp(requiredHitRatio, 0.0, 1.0);
        _hysteresis = Math.Max(0, hysteresis);
        MinInterval = minInterval;
    }

    public ProbeResult Evaluate(in ProbeContext ctx)
    {
        int cw = ctx.ClientWidth, ch = ctx.ClientHeight;
        if (cw <= 0 || ch <= 0) return new ProbeResult(false);

        int hits = 0;
        var debugList = new List<SentinelDebugPoint>(_sentinels.Length);

        for (int i = 0; i < _sentinels.Length; i++)
        {
            var s = _sentinels[i];

            int cx = s.IsNormalized
                ? ctx.OriginX + Math.Clamp((int)Math.Round(s.U * cw), 0, cw - 1)
                : ctx.OriginX + Math.Clamp(s.X, 0, cw - 1);
            int cy = s.IsNormalized
                ? ctx.OriginY + Math.Clamp((int)Math.Round(s.V * ch), 0, ch - 1)
                : ctx.OriginY + Math.Clamp(s.Y, 0, ch - 1);

            uint sampled = ctx.GetPixel(cx, cy);
            bool match = CloseEnough(sampled, s.ExpectedArgb, s.Tolerance);

            debugList.Add(new SentinelDebugPoint(
                s.IsNormalized, s.U, s.V, s.X, s.Y,
                cx, cy, sampled, s.ExpectedArgb, match
            ));


            //if (!match)
            //{
            //    Log.Debug("sentinels",
            //        $"{Key}[{i}] miss " +
            //        $"pos=({cx},{cy}) norm={(s.IsNormalized ? $"{s.U:F3},{s.V:F3}" : "abs")} " +
            //        $"sampled=0x{sampled:X8} expected=0x{s.ExpectedArgb:X8} tol={s.Tolerance}");
            //}

            if (match) hits++;
        }

        int need = Math.Max(1, (int)Math.Ceiling(_requiredHitRatio * _sentinels.Length));
        bool decision = hits >= need;

        if (_hysteresis > 0 && decision != _lastDecision)
        {
            _stableCount++;
            if (_stableCount < _hysteresis) return new ProbeResult(_lastDecision, debugList);
            _stableCount = 0;
            _lastDecision = decision;
            return new ProbeResult(decision, debugList);
        }

        _stableCount = 0;
        _lastDecision = decision;
        return new ProbeResult(decision, debugList);
    }

    private static bool CloseEnough(uint c1, uint c2, int tol)
    {
        tol = Math.Clamp(tol, 0, 255);
        int r1 = (int)((c1 >> 16) & 0xFF), g1 = (int)((c1 >> 8) & 0xFF), b1 = (int)(c1 & 0xFF);
        int r2 = (int)((c2 >> 16) & 0xFF), g2 = (int)((c2 >> 8) & 0xFF), b2 = (int)(c2 & 0xFF);
        return Math.Abs(r1 - r2) <= tol && Math.Abs(g1 - g2) <= tol && Math.Abs(b1 - b2) <= tol;
    }

    public readonly struct Sentinel
    {
        public readonly int X, Y, Tolerance;
        public readonly double U, V;
        public readonly bool IsNormalized;
        public readonly uint ExpectedArgb;

        public static Sentinel Normalized(double u, double v, uint argb, int tol) => new(0, 0, u, v, argb, tol, true);
        public static Sentinel Absolute(int x, int y, uint argb, int tol) => new(x, y, 0, 0, argb, tol, false);

        private Sentinel(int x, int y, double u, double v, uint argb, int tol, bool normalized)
        { X = x; Y = y; U = u; V = v; ExpectedArgb = argb; Tolerance = tol; IsNormalized = normalized; }
    }
}

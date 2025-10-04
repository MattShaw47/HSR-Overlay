using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HSR_Overlay.Services.Probing;

/// One decision per tick. Stateless or stateful is up to the probe.
public interface IProbe
{
    string Key { get; } // e.g., "relic-modal"
    TimeSpan? MinInterval { get; } // optional throttling per probe
    ProbeResult Evaluate(in ProbeContext ctx); // returns Active/Inactive (+ optional debug)
}

public readonly struct ProbeContext
{
    public ProbeContext(IntPtr gameHwnd, int clientW, int clientH, int originX, int originY, Func<int, int, uint> getPixel)
    { GameHwnd = gameHwnd; ClientWidth = clientW; ClientHeight = clientH; OriginX = originX; OriginY = originY; GetPixel = getPixel; }

    public IntPtr GameHwnd { get; }
    public int ClientWidth { get; }
    public int ClientHeight { get; }
    public int OriginX { get; } // client->screen offset X
    public int OriginY { get; } // client->screen offset Y
    public Func<int, int, uint> GetPixel { get; } // screen coords → ARGB
}

public readonly struct ProbeResult
{
    public ProbeResult(bool isActive, object? debug = null) { IsActive = isActive; Debug = debug; }
    public bool IsActive { get; }
    public object? Debug { get; } // optional: points/colors, etc.
}

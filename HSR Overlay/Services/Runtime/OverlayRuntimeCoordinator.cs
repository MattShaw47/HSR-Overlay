using HSR_Overlay.Services.Capture;
using HSR_Overlay.Services.Probing;

namespace HSR_Overlay.Services.Runtime;

internal sealed class OverlayRuntimeCoordinator
{
    private readonly CaptureService _capture;
    private readonly Dictionary<string, Action<bool>> _probeStateHandlers = new();
    private readonly Dictionary<string, Action<object?>> _probeDebugHandlers = new();
    private readonly Dictionary<string, Action<CaptureService.FrameInfo>> _probeActivatedHandlers = new();

    public OverlayRuntimeCoordinator(CaptureService capture)
    {
        _capture = capture;
    }

    public void RegisterProbes(ProbeProfileSet profiles)
    {
        foreach (var kvp in profiles.Probes)
        {
            var key = kvp.Key;
            var profile = kvp.Value;

            var sentinels = profile.Sentinels
                .Select(s => SentinelProbe.Sentinel.Normalized(s.U, s.V, s.ExpectedArgb, s.Tolerance))
                .ToArray();

            _capture.RegisterProbe(new SentinelProbe(
                key,
                sentinels,
                requiredHitRatio: profile.RequiredHitRatio ?? profiles.DefaultRequiredHitRatio,
                hysteresis: profile.Hysteresis ?? profiles.DefaultHysteresis,
                minInterval: profile.MinIntervalMs.HasValue ? TimeSpan.FromMilliseconds(profile.MinIntervalMs.Value) : null));
        }
    }

    public void SetProbeHandlers(string key, Action<bool>? onStateChanged = null, Action<object?>? onDebug = null, Action<CaptureService.FrameInfo>? onActivated = null)
    {
        if (onStateChanged is not null) _probeStateHandlers[key] = onStateChanged;
        if (onDebug is not null) _probeDebugHandlers[key] = onDebug;
        if (onActivated is not null) _probeActivatedHandlers[key] = onActivated;
    }

    public void Wire()
    {
        _capture.ProbeStateChanged += OnProbeStateChanged;
        _capture.ProbeDebugTapped += OnProbeDebugTapped;
        _capture.ProbeActivated += OnProbeActivated;
    }

    public void Unwire()
    {
        _capture.ProbeStateChanged -= OnProbeStateChanged;
        _capture.ProbeDebugTapped -= OnProbeDebugTapped;
        _capture.ProbeActivated -= OnProbeActivated;
    }

    private void OnProbeStateChanged(string key, bool active)
    {
        if (_probeStateHandlers.TryGetValue(key, out var handler)) handler(active);
    }

    private void OnProbeDebugTapped(string key, object? payload)
    {
        if (_probeDebugHandlers.TryGetValue(key, out var handler)) handler(payload);
    }

    private void OnProbeActivated(string key, CaptureService.FrameInfo frame)
    {
        if (_probeActivatedHandlers.TryGetValue(key, out var handler)) handler(frame);
    }
}

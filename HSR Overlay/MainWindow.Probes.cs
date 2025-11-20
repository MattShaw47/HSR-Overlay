using HSR_Overlay.Services.Capture;
using HSR_Overlay.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HSR_Overlay;

public partial class MainWindow
{
    // (optionally move these fields here)
    // private readonly Dictionary<string, Action<bool>> _probeStateHandlers = new();
    // private readonly Dictionary<string, Action<object?>> _probeDebugHandlers = new();
    // private readonly Dictionary<string, Action<CaptureService.FrameInfo>> _probeActivatedHandlers = new();
    // private bool _relicModalActive;

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
        _capture.ProbeActivated -= OnProbeActivated;
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
}

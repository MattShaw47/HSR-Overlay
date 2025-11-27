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

        _probeDebugHandlers["relic-modal"] = payload => DrawSentinelPoints("relic-modal", payload);
        _probeActivatedHandlers["relic-modal"] = frame => StartRelicOcr(frame);

        _probeStateHandlers["char-relics"] = active =>
        {
            _charScreenActive = active;

            if (active)
            {
                _currentRelicSlotProbeKey = null;
                _currentRelicHasGoodReading = false;
                Dispatcher.Invoke(() => UpdateRelicFoundIndicator(false));
                if (!_charOcrTimer.IsEnabled)
                    _charOcrTimer.Start();
            }
            else
            {
                _charOcrTimer.Stop();
                _currentRelicSlotProbeKey = null;
                _currentRelicHasGoodReading = false;
                Dispatcher.Invoke(() => RelicFoundIndicator.Visibility = System.Windows.Visibility.Collapsed);
            }
        };

        _probeDebugHandlers["char-relics"] = payload => DrawSentinelPoints("char-relics", payload);
        _probeActivatedHandlers["char-relics"] = frame => StartCharacterRelicOcr(frame);

        _probeStateHandlers["slot-head"] = active =>
        {
            if (active) OnRelicSlotActivated("slot-head");
        };

        _probeStateHandlers["slot-hands"] = active =>
        {
            if (active) OnRelicSlotActivated("slot-hands");
        };

        _probeStateHandlers["slot-body"] = active =>
        {
            if (active) OnRelicSlotActivated("slot-body");
        };

        _probeStateHandlers["slot-feet"] = active =>
        {
            if (active) OnRelicSlotActivated("slot-feet");
        };

        _probeStateHandlers["slot-orb"] = active =>
        {
            if (active) OnRelicSlotActivated("slot-orb");
        };

        _probeStateHandlers["slot-rope"] = active =>
        {
            if (active) OnRelicSlotActivated("slot-rope");
        };

        _probeDebugHandlers["slot-head"] = payload => DrawSentinelPoints("slot-head", payload);
        _probeDebugHandlers["slot-hands"] = payload => DrawSentinelPoints("slot-hands", payload);
        _probeDebugHandlers["slot-body"] = payload => DrawSentinelPoints("slot-body", payload);
        _probeDebugHandlers["slot-feet"] = payload => DrawSentinelPoints("slot-feet", payload);
        _probeDebugHandlers["slot-orb"] = payload => DrawSentinelPoints("slot-orb", payload);
        _probeDebugHandlers["slot-rope"] = payload => DrawSentinelPoints("slot-rope", payload);
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

    private void OnRelicSlotActivated(string probeKey)
    {
        if (!_charScreenActive)
            return;

        if (_currentRelicSlotProbeKey == probeKey)
            return;

        _currentRelicSlotProbeKey = probeKey;
        _currentRelicHasGoodReading = false;

        Dispatcher.Invoke(() => UpdateRelicFoundIndicator(false));
    }
}

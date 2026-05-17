using HSR_Overlay.Services.Capture;
using HSR_Overlay.Util;

namespace HSR_Overlay;

public partial class MainWindow
{
    private void ConfigureRuntimeHandlers()
    {
        _runtime.SetProbeHandlers("relic-modal",
            onStateChanged: active =>
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
            },
            onDebug: payload => DrawSentinelPoints("relic-modal", payload),
            onActivated: frame => StartRelicOcr(frame));

        _runtime.SetProbeHandlers("char-relics",
            onStateChanged: active =>
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
            },
            onDebug: payload => DrawSentinelPoints("char-relics", payload),
            onActivated: frame => StartCharacterRelicOcr(frame));

        foreach (var slotKey in new[] { "slot-head", "slot-hands", "slot-body", "slot-feet", "slot-orb", "slot-rope" })
        {
            _runtime.SetProbeHandlers(slotKey,
                onStateChanged: active => { if (active) OnRelicSlotActivated(slotKey); },
                onDebug: payload => DrawSentinelPoints(slotKey, payload));
        }
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

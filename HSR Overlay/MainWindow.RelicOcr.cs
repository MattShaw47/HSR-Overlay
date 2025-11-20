using HSR_Overlay.Services.Analysis;
using HSR_Overlay.Services.Capture;
using HSR_Overlay.Services.Ocr;
using HSR_Overlay.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Threading;
using MBrushes = System.Windows.Media.Brushes;
using WPoint = System.Windows.Point;
using DRectangle = System.Drawing.Rectangle;

namespace HSR_Overlay;

public partial class MainWindow
{
    // fields: _relicOcrTimer, _relicOcrInProgress, _relicTextParser, _relicWeightsProvider, _relicStatTables

    private async void StartRelicOcr(CaptureService.FrameInfo frame)
{
    if (!_relicModalActive) return;
    if (!Settings.Current.EnableRelicPopup) return;
    if (!_capture.HasLastFrame) return;
    if (_relicOcrInProgress) return;

    _relicOcrInProgress = true;

    try
    {
        var roi = GetRelicOcrScreenRect(frame);
        DrawOcrRect(roi);

        using var bmp = _capture.CaptureRegionToBitmap(roi);
        string text = await OcrReader.ReadTextAsync(bmp);

        var parsed = _relicTextParser.Parse(text, new RelicTextContext(RelicTextSource.Farming));
        var eval = RelicAnalyzer.Analyze(parsed, _relicWeightsProvider, _relicStatTables);

        Dispatcher.Invoke(() =>
        {
            if (Settings.Current.EnableRelicPopup)
                _popup.Update(string.IsNullOrWhiteSpace(text) ? "No text found." : text);
        });
    }
    finally
    {
        _relicOcrInProgress = false;
    }
}

private static DRectangle GetRelicOcrScreenRect(CaptureService.FrameInfo f)
{
    const double nx = 0.41, ny = 0.31, nw = 0.4, nh = 0.285;

    int x = f.OriginX + (int)(nx * f.ClientWidth);
    int y = f.OriginY + (int)(ny * f.ClientHeight);
    int w = (int)(nw * f.ClientWidth);
    int h = (int)(nh * f.ClientHeight);

    if (w <= 0 || h <= 0) return DRectangle.Empty;
    return new DRectangle(x, y, w, h);
}
}

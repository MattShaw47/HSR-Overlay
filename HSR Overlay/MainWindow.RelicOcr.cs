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

        Log.Debug("ocr", "attempting to start ocr");

        try
        {
            // 1) Compute both OCR regions
            DRectangle textRoi = GetRelicTextOcrScreenRect(frame);
            //DRectangle numbersRoi = GetRelicNumbersOcrScreenRect(frame);

            // 2) Draw them for debugging
            DrawOcrRects(new DRectangle[] {textRoi});

            // 3) Capture and OCR both regions
            string textBlock;
            //string numbersBlock;

            using (var bmpText = _capture.CaptureRegionToBitmap(textRoi))
            {
                textBlock = await OcrReader.ReadTextAsync(bmpText);
            }

            if (!_relicModalActive)
            {
                return;
            }

            ParsedRelic foundRelic = _relicTextParser.Parse(
                textBlock,
                new RelicTextContext(RelicTextSource.Farming));

            Dispatcher.Invoke(() =>
            {
                if (!Settings.Current.EnableRelicPopup) return;

                var display = new StringBuilder();
                if (!string.IsNullOrWhiteSpace(textBlock))
                    display.AppendLine(textBlock.Trim());


                _popup.Update(display.Length == 0 ? "No text found." : display.ToString());
            });
        }
        finally
        {
            _relicOcrInProgress = false;
        }
    }

    private static DRectangle GetRelicOcrScreenRect(CaptureService.FrameInfo f)
    {
        const double nx = 0.41, ny = 0.31, nw = 0.4, nh = 0.275;

        int x = f.OriginX + (int)(nx * f.ClientWidth);
        int y = f.OriginY + (int)(ny * f.ClientHeight);
        int w = (int)(nw * f.ClientWidth);
        int h = (int)(nh * f.ClientHeight);

        if (w <= 0 || h <= 0) return DRectangle.Empty;
        return new DRectangle(x, y, w, h);
    }

    private static DRectangle GetRelicTextOcrScreenRect(CaptureService.FrameInfo f)
    {
        // Example normalized values – adjust to match your popup layout
        const double nx = 0.41, ny = 0.31, nw = 0.4, nh = 0.285;

        int x = f.OriginX + (int)(nx * f.ClientWidth);
        int y = f.OriginY + (int)(ny * f.ClientHeight);
        int w = (int)(nw * f.ClientWidth);
        int h = (int)(nh * f.ClientHeight);

        return w <= 0 || h <= 0 ? DRectangle.Empty : new DRectangle(x, y, w, h);
    }

    // Right side: numbers / percentages only
    private static DRectangle GetRelicNumbersOcrScreenRect(CaptureService.FrameInfo f)
    {
        // Shift X right, narrower width
        const double nx = 0.725, ny = 0.35, nw = 0.10, nh = 0.20;

        int x = f.OriginX + (int)(nx * f.ClientWidth);
        int y = f.OriginY + (int)(ny * f.ClientHeight);
        int w = (int)(nw * f.ClientWidth);
        int h = (int)(nh * f.ClientHeight);

        return w <= 0 || h <= 0 ? DRectangle.Empty : new DRectangle(x, y, w, h);
    }
}

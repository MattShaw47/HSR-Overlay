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
using Tesseract;

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

        //Log.Debug("ocr", "attempting to start ocr");

        try
        {

            var textRois = new[]
            {
                // Relic name
                GetRect(frame, 0.41, 0.30, 0.35, 0.05),
                // stats
                GetRect(frame, 0.428, 0.377, 0.25, 0.03),
                GetRect(frame, 0.428, 0.415, 0.25, 0.03),
                GetRect(frame, 0.428, 0.453, 0.25, 0.03),
                GetRect(frame, 0.428, 0.482, 0.25, 0.03),
                GetRect(frame, 0.428, 0.517, 0.25, 0.03),
                // values
                GetRect(frame, 0.73, 0.377, 0.05, 0.03),
                GetRect(frame, 0.73, 0.415, 0.05, 0.03),
                GetRect(frame, 0.73, 0.451, 0.05, 0.03),
                GetRect(frame, 0.73, 0.482, 0.05, 0.03),
                GetRect(frame, 0.73, 0.517, 0.05, 0.03),


                // set

                //GetCharRelicStatsOcrRect(frame)
                // stat names

                // stat vals
                
            };

            // 1) Compute both OCR regions

            // 2) Draw them for debugging
            DrawOcrRects(textRois);

            // 3) Capture and OCR both regions

            var relicName = await OcrReader.ReadDetailAsync(
                _capture.CaptureRegionToBitmap(textRois[0]),
                profile: OcrProfile.General,
                segMode: PageSegMode.Auto,
                ct: CancellationToken.None);

            var relicMainStat = await OcrReader.ReadDetailAsync(
                _capture.CaptureRegionToBitmap(textRois[1]),
                profile: OcrProfile.General,
                segMode: PageSegMode.Auto,
                ct: CancellationToken.None);

            var relicSubOne = await OcrReader.ReadDetailAsync(
                _capture.CaptureRegionToBitmap(textRois[2]),
                profile: OcrProfile.General,
                segMode: PageSegMode.Auto,
                ct: CancellationToken.None);

            var relicSubTwo = await OcrReader.ReadDetailAsync(
                _capture.CaptureRegionToBitmap(textRois[3]),
                profile: OcrProfile.General,
                segMode: PageSegMode.Auto,
                ct: CancellationToken.None);

            var relicSubThree = await OcrReader.ReadDetailAsync(
                _capture.CaptureRegionToBitmap(textRois[4]),
                profile: OcrProfile.General,
                segMode: PageSegMode.Auto,
                ct: CancellationToken.None);

            var relicSubFour = await OcrReader.ReadDetailAsync(
                _capture.CaptureRegionToBitmap(textRois[5]),
                profile: OcrProfile.General,
                segMode: PageSegMode.Auto,
                ct: CancellationToken.None);



            var relicMainVal = await OcrReader.ReadDetailAsync(
                _capture.CaptureRegionToBitmap(textRois[6]),
                profile: OcrProfile.Numeric,
                segMode: PageSegMode.SingleLine,
                ct: CancellationToken.None);

            var relicSubValOne = await OcrReader.ReadDetailAsync(
                _capture.CaptureRegionToBitmap(textRois[7]),
                profile: OcrProfile.Numeric,
                segMode: PageSegMode.SingleLine,
                ct: CancellationToken.None);

            var relicSubValTwo = await OcrReader.ReadDetailAsync(
                _capture.CaptureRegionToBitmap(textRois[8]),
                profile: OcrProfile.Numeric,
                segMode: PageSegMode.SingleLine,
                ct: CancellationToken.None);

            var relicSubValThree = await OcrReader.ReadDetailAsync(
                _capture.CaptureRegionToBitmap(textRois[9]),
                profile: OcrProfile.Numeric,
                segMode: PageSegMode.SingleLine,
                ct: CancellationToken.None);

            var relicSubValFour = await OcrReader.ReadDetailAsync(
                _capture.CaptureRegionToBitmap(textRois[10]),
                profile: OcrProfile.Numeric,
                segMode: PageSegMode.SingleLine,
                ct: CancellationToken.None);



            String ocrText = relicName.Text.Trim() + "\n"
                + relicMainStat.Text.Trim() + "\n"
                + relicSubOne.Text.Trim() + "\n"
                + relicSubTwo.Text.Trim() + "\n"
                + relicSubThree.Text.Trim() + "\n"
                + relicSubFour.Text.Trim() + "\n"
                + relicMainVal.Text.Trim() + "\n"
                + relicSubValOne.Text.Trim() + "\n"
                + relicSubValTwo.Text.Trim() + "\n"
                + relicSubValThree.Text.Trim() + "\n"
                + relicSubValFour.Text.Trim() + "\n"
                ;

            Log.Debug("ocr", ocrText);


            if (!_relicModalActive)
            {
                return;
            }

            ParsedRelic foundRelic = _relicTextParser.Parse(
                ocrText,
                new RelicTextContext(RelicTextSource.Farming));

            Dispatcher.Invoke(() =>
            {
                if (!Settings.Current.EnableRelicPopup) return;

                var display = new StringBuilder();
                if (!string.IsNullOrWhiteSpace(ocrText))
                    display.AppendLine(ocrText.Trim());


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


    private async void StartCharacterRelicOcr(CaptureService.FrameInfo frame)
    {
        if (!_charScreenActive) return;
        if (!_capture.HasLastFrame) return;
        if (_charOcrInProgress) return;

        _charOcrInProgress = true;

        try
        {
            var textRois = new[]
            {
                //nx , ny  , nw , nh ;
                //GetCharRelicOcrRect(frame),
                GetCharNameOcrRect(frame),
                GetCharRelicNameRect(frame),
                //GetCharRelicStatsOcrRect(frame)
                // stat names
                GetRect(frame, 0.795, 0.27, 0.12, 0.035),
                GetRect(frame, 0.795, 0.305, 0.12, 0.035),
                GetRect(frame, 0.795, 0.34, 0.12, 0.035),
                GetRect(frame, 0.795, 0.375, 0.12, 0.035),
                GetRect(frame, 0.795, 0.41, 0.12, 0.036),
                // stat vals
                GetRect(frame, 0.93, 0.270, 0.06, 0.035),
                GetRect(frame, 0.935, 0.305, 0.06, 0.035),
                GetRect(frame, 0.935, 0.34, 0.06, 0.035),
                GetRect(frame, 0.935, 0.375, 0.06, 0.035),
                GetRect(frame, 0.935, 0.41, 0.06, 0.037),
                // slot
                //GetRect(frame, 0.77, 0.20, 0.06, 0.033)
            };
            //nx = 0.795, ny = 0.25, nw = 0.10, nh = 0.22;

            // Draw all OCR rects in debug overlay
            DrawOcrRects(textRois);

            StringBuilder ocrText = new StringBuilder();

            var statNameMain = await OcrReader.ReadDetailAsync(
                _capture.CaptureRegionToBitmap(textRois[2]),
                profile: OcrProfile.General,
                segMode: PageSegMode.Auto,
                ct: CancellationToken.None
                );

            var statNameOne = await OcrReader.ReadDetailAsync(
                OcrReader.PrepareStatNameCrop(_capture.CaptureRegionToBitmap(textRois[3])),
                profile: OcrProfile.General,
                segMode: PageSegMode.Auto,
                ct: CancellationToken.None
                );

            var statNameTwo = await OcrReader.ReadDetailAsync(
                OcrReader.PrepareStatNameCrop(_capture.CaptureRegionToBitmap(textRois[4])),
                profile: OcrProfile.General,
                segMode: PageSegMode.Auto,
                ct: CancellationToken.None
                );

            var statNameThree = await OcrReader.ReadDetailAsync(
                OcrReader.PrepareStatNameCrop(_capture.CaptureRegionToBitmap(textRois[5])),
                profile: OcrProfile.General,
                segMode: PageSegMode.Auto,
                ct: CancellationToken.None
                );

            var statNameFour = await OcrReader.ReadDetailAsync(
                OcrReader.PrepareStatNameCrop(_capture.CaptureRegionToBitmap(textRois[6])),
                profile: OcrProfile.General,
                segMode: PageSegMode.Auto,
                ct: CancellationToken.None
                );


            // RELIC NAME
            var relicName = await OcrReader.ReadDetailAsync(
                _capture.CaptureRegionToBitmap(textRois[1]),
                profile: OcrProfile.Title,
                segMode: PageSegMode.Auto,
                ct: CancellationToken.None);

            // STAT VALS
            var mainStat = await OcrReader.ReadNumericAsync(
                _capture.CaptureRegionToBitmap(textRois[7]),
                isMainStat: true,
                ct: CancellationToken.None);

            var subOne = await OcrReader.ReadNumericAsync(
                _capture.CaptureRegionToBitmap(textRois[8]),
                isMainStat: false,
                ct: CancellationToken.None);

            var subTwo = await OcrReader.ReadNumericAsync(
                _capture.CaptureRegionToBitmap(textRois[9]),
                isMainStat: false,
                ct: CancellationToken.None);

            var subThree = await OcrReader.ReadNumericAsync(
                _capture.CaptureRegionToBitmap(textRois[10]),
                isMainStat: false,
                ct: CancellationToken.None);

            var subFour = await OcrReader.ReadNumericAsync(
                _capture.CaptureRegionToBitmap(textRois[11]),
                isMainStat: false,
                ct: CancellationToken.None);

            string ocrResult =
                statNameMain.Text.Trim() + "\n"
                + statNameOne.Text.Trim() + "\n"
                + statNameTwo.Text.Trim() + "\n"
                + statNameThree.Text.Trim() + "\n"
                + statNameFour.Text.Trim() + "\n"
                + mainStat.Text.Trim() + "\n"
                + subOne.Text.Trim() + "\n"
                + subTwo.Text.Trim() + "\n"
                + subThree.Text.Trim() + "\n"
                + subFour.Text.Trim() + "\n"
                + ocrText.Append(await OcrReader.ReadTextAsync(_capture.CaptureRegionToBitmap(textRois[0]))) + "\n"
                + relicName.Text.Trim() + "\n";

            Log.Debug("ocr", ocrResult);

            var main = _capture.CaptureRegionToBitmap(textRois[7]);
            main.Save("main_raw.png");
            OcrReader.PrepareMainStatCrop(main).Save("main_prepped.png");

            var sub1 = _capture.CaptureRegionToBitmap(textRois[8]);
            sub1.Save("sub1_raw.png");
            OcrReader.PrepareStatNameCrop(sub1).Save("sub1_prepped.png");

            var sub2 = _capture.CaptureRegionToBitmap(textRois[9]);
            sub2.Save("sub2_raw.png");
            OcrReader.PrepareStatNameCrop(sub2).Save("sub2_prepped.png");

            var subName3 = _capture.CaptureRegionToBitmap(textRois[5]);
            subName3.Save("subName3_raw.png");
            OcrReader.PrepareStatNameCrop(subName3).Save("subName3_prepped.png");

            EquippedRelic equipped = _relicTextParser.ParseEquippedFromCharacterScreen(ocrResult, _characterNameDb);

            equipped.Relic = RelicStatSanitizer.Sanitize(equipped.Relic, _relicStatTables);

            if (equipped.Relic.Set == "" || equipped.Relic.Substats.Count != 4 || equipped.Relic.MainStat == Stat.None)
            {
                Log.Debug("ocr", "ocr failed to get good relic reading");

                if (!_currentRelicHasGoodReading)
                {
                    Dispatcher.Invoke(() => UpdateRelicFoundIndicator(false));
                }

                return;
            }

            _currentRelicHasGoodReading = true;
            Dispatcher.Invoke(() => UpdateRelicFoundIndicator(true));
            Log.Debug("ocr", "acceptable relic found");

            var evaluation = RelicAnalyzer.Analyze(
                equipped.Relic,
                equipped.CharacterKey,
                _relicWeightsProvider,
                _relicStatTables,
                _relicInventory);
            if (evaluation.ImprovementChances.TryGetValue(equipped.CharacterKey, out var chance))
            {
                Log.Debug("analysis", $"{equipped.CharacterKey} improvement chance: {chance:P1}");
            }

            _relicInventory.setEquipped(equipped.CharacterKey, equipped.Relic.Slot, equipped.Relic);
            _relicInventory.Save();

        }
        finally
        {
            _charOcrInProgress = false;
        }
    }

    // stat names
    private static DRectangle GetCharRelicOcrRect(CaptureService.FrameInfo f)
    {
        const double nx = 0.795, ny = 0.25, nw = 0.10, nh = 0.22;

        int x = f.OriginX + (int)(nx * f.ClientWidth);
        int y = f.OriginY + (int)(ny * f.ClientHeight);
        int w = (int)(nw * f.ClientWidth);
        int h = (int)(nh * f.ClientHeight);

        return w <= 0 || h <= 0 ? DRectangle.Empty : new DRectangle(x, y, w, h);
    }

    // stat vals
    private static DRectangle GetRect(CaptureService.FrameInfo f, double nx, double ny, double nw, double nh)
    {
        //const double nx = 0.93, ny = 0.27, nw = 0.05, nh = 0.04;
        //const double nx = 0.93, ny = 0.25, nw = 0.05, nh = 0.20;

        int x = f.OriginX + (int)(nx * f.ClientWidth);
        int y = f.OriginY + (int)(ny * f.ClientHeight);
        int w = (int)(nw * f.ClientWidth);
        int h = (int)(nh * f.ClientHeight);

        return w <= 0 || h <= 0 ? DRectangle.Empty : new DRectangle(x, y, w, h);
    }

    // char name
    private static DRectangle GetCharNameOcrRect(CaptureService.FrameInfo f)
    {
        const double nx = 0.065, ny = 0.06, nw = 0.20, nh = 0.05;

        int x = f.OriginX + (int)(nx * f.ClientWidth);
        int y = f.OriginY + (int)(ny * f.ClientHeight);
        int w = (int)(nw * f.ClientWidth);
        int h = (int)(nh * f.ClientHeight);

        return w <= 0 || h <= 0 ? DRectangle.Empty : new DRectangle(x, y, w, h);
    }

    // relic name
    private static DRectangle GetCharRelicNameRect(CaptureService.FrameInfo f)
    {
        const double nx = 0.77, ny = 0.13, nw = 0.19, nh = 0.072;

        int x = f.OriginX + (int)(nx * f.ClientWidth);
        int y = f.OriginY + (int)(ny * f.ClientHeight);
        int w = (int)(nw * f.ClientWidth);
        int h = (int)(nh * f.ClientHeight);

        return w <= 0 || h <= 0 ? DRectangle.Empty : new DRectangle(x, y, w, h);
    }

}

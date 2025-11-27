using HSR_Overlay.Util;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Tesseract;

namespace HSR_Overlay.Services.Ocr;

public enum OcrProfile
{
    General,
    Title,
    Numeric
}

/// <summary>
/// Stateless OCR helper: feed a Bitmap, get text or a structured result (lines/words with bounds).
/// </summary>
public static class OcrReader
{
    /// <summary>Ensure OCR engine is ready. Call at app start or let it lazy-init on first use.</summary>
    public static void Init(string? tessdataDir = null, string lang = "eng")
    {
        EnsureEngine(tessdataDir, lang);
    }

    /// <summary>Read plain text from a bitmap asynchronously.</summary>
    public static async Task<string> ReadTextAsync(Bitmap bmp, string lang = "eng", CancellationToken ct = default)
    {
        var res = await ReadDetailAsync(bmp, lang, OcrProfile.General, PageSegMode.Auto, ct).ConfigureAwait(false);
        //Log.Debug("ocr", $"{res.Text?.Trim() ?? string.Empty}");
        return res.Text?.Trim() ?? string.Empty;
    }

    /// <summary>
    /// Read detailed OCR results (blocks/lines/words with bounding boxes and confidences).
    /// </summary>
    public static Task<OcrResult> ReadDetailAsync(
    Bitmap bmp,
    string lang = "eng",
    OcrProfile profile = OcrProfile.General,
    PageSegMode segMode = PageSegMode.Auto,
    CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            EnsureEngine(null, lang);

            // TesseractEngine isn’t thread-safe, so serialize access.
            lock (_gate)
            {
                ApplyProfile(profile);

                using var pix = PixConverter.ToPix(bmp);
                using var page = _engine!.Process(pix, segMode);

                var result = new OcrResult
                {
                    Text = page.GetText() ?? string.Empty,
                    Confidence = page.GetMeanConfidence()
                };

                using var iterator = page.GetIterator();
                if (iterator == null) return result;

                iterator.Begin();
                var lines = new List<OcrLine>();
                var curLine = default(OcrLine?);

                do
                {
                    if (iterator.IsAtBeginningOf(PageIteratorLevel.TextLine))
                    {
                        if (curLine is OcrLine finished)
                            lines.Add(finished);

                        curLine = new OcrLine
                        {
                            Bounds = iterator.TryGetRect(PageIteratorLevel.TextLine),
                            Words = new List<OcrWord>()
                        };
                    }

                    if (iterator.TryGetText(PageIteratorLevel.Word, out var wtext))
                    {
                        var w = new OcrWord
                        {
                            Text = wtext,
                            Bounds = iterator.TryGetRect(PageIteratorLevel.Word),
                            Confidence = iterator.TryGetConfidence(PageIteratorLevel.Word)
                        };
                        curLine?.Words.Add(w);
                    }
                }
                while (iterator.Next(PageIteratorLevel.Word));

                if (curLine is OcrLine last) lines.Add(last);
                result.Lines = lines;
                return result;
            }
        }, ct);
    }

    private static readonly object _gate = new();
    private static TesseractEngine? _engine;
    private static string? _loadedLang;

    private static void EnsureEngine(string? tessdataDir, string lang)
    {
        lock (_gate)
        {
            // Reuse engine if already loaded for this language
            if (_engine != null && string.Equals(_loadedLang, lang, StringComparison.OrdinalIgnoreCase))
                return;

            _engine?.Dispose();
            _engine = null;

            // Default tessdata dir: put a 'tessdata' folder next to your exe with 'eng.traineddata' inside
            tessdataDir ??= Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");
            if (!Directory.Exists(tessdataDir))
                throw new DirectoryNotFoundException($"tessdata not found: {tessdataDir}");

            _engine = new TesseractEngine(tessdataDir, lang, EngineMode.Default);

            // Helpful defaults for UI screenshots
            _engine.SetVariable("user_defined_dpi", "96");
            _engine.SetVariable("debug_file", "");
            // _engine.SetVariable("tessedit_char_whitelist", "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789:-+/()., "); // example whitelist

            _loadedLang = lang;
        }
    }

    private static void ApplyProfile(OcrProfile profile)
    {
        switch (profile)
        {
            case OcrProfile.Numeric:
                // Only numbers, decimal point, percent sign
                _engine!.SetVariable("tessedit_char_whitelist", "0123456789.%");
                _engine.SetVariable("tessedit_char_blacklist", "");

                // Don’t try to form dictionary words from digits
                _engine.SetVariable("load_system_dawg", "0");
                _engine.SetVariable("load_freq_dawg", "0");

                _engine.DefaultPageSegMode = PageSegMode.SingleLine;
                break;

            case OcrProfile.Title:
                // Relic name: letters + apostrophe + space
                _engine!.SetVariable("tessedit_char_whitelist",
                    "-ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz' ");
                _engine.SetVariable("tessedit_char_blacklist", "0123456789");

                _engine.SetVariable("load_system_dawg", "0");
                _engine.SetVariable("load_freq_dawg", "0");

                _engine.DefaultPageSegMode = PageSegMode.SingleLine;
                break;

            default: // General
                     // Reset to more default-like behavior
                _engine!.SetVariable("tessedit_char_whitelist", "");
                _engine.SetVariable("tessedit_char_blacklist", "");
                _engine.SetVariable("load_system_dawg", "1");
                _engine.SetVariable("load_freq_dawg", "1");

                _engine.DefaultPageSegMode = PageSegMode.Auto;
                break;
        }
    }

    // white on non-white text
    public static Bitmap PrepareNumericCrop(Bitmap src)
    {
        const int scale = 3;

        // 1) Scale up
        var scaled = new Bitmap(src.Width * scale, src.Height * scale);
        using (var g = Graphics.FromImage(scaled))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(src, 0, 0, scaled.Width, scaled.Height);
        }

        const int threshold = 200;

        int width = scaled.Width;
        int height = scaled.Height;

        // 2) Grayscale + threshold: bright digits → black, rest → white
        //    Also build per-column foreground counts.
        var colCounts = new int[width];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var c = scaled.GetPixel(x, y);
                int gray = (int)(0.299 * c.R + 0.587 * c.G + 0.114 * c.B);

                bool isDigit = gray >= threshold; // digits are bright
                if (isDigit)
                {
                    scaled.SetPixel(x, y, Color.Black);
                    colCounts[x]++;
                }
                else
                {
                    scaled.SetPixel(x, y, Color.White);
                }
            }
        }

        int totalForeground = colCounts.Sum();
        if (totalForeground == 0)
        {
            // nothing detected, just return the scaled image
            scaled.SetResolution(300, 300);
            return scaled;
        }

        // 3) Find the main digit column (max foreground pixels)
        int mainCol = 0;
        int maxCount = colCounts[0];
        for (int x = 1; x < width; x++)
        {
            if (colCounts[x] > maxCount)
            {
                maxCount = colCounts[x];
                mainCol = x;
            }
        }

        // 4) From left to right, find first column that looks like it is in the digit cluster.
        //    We stop as soon as we hit a column with some foreground and not too far from mainCol.
        int minX = 0;
        int bandHalfWidth = Math.Max(width / 4, 10); // how far from mainCol we consider "part of the cluster"
        int clusterMin = Math.Max(0, mainCol - bandHalfWidth);

        minX = 0;
        for (int x = 0; x < width; x++)
        {
            if (colCounts[x] > 0 && x >= clusterMin)
            {
                minX = x;
                break;
            }
        }

        // 5) Crop only on the left: [minX .. width-1], full height
        const int padLeft = 4;
        minX = Math.Max(0, minX - padLeft);

        int cropW = width - minX;
        int cropH = height;

        var tight = new Bitmap(cropW, cropH);
        using (var g2 = Graphics.FromImage(tight))
        {
            g2.DrawImage(
                scaled,
                new Rectangle(0, 0, cropW, cropH),
                new Rectangle(minX, 0, cropW, cropH),
                GraphicsUnit.Pixel);
        }

        tight.SetResolution(300, 300);
        scaled.Dispose();
        return tight;
    }

    // for orange text
    public static Bitmap PrepareMainStatCrop(Bitmap src)
    {
        const int scale = 3;

        // Scale up first so our color sampling and mask are a bit smoother.
        var scaled = new Bitmap(src.Width * scale, src.Height * scale, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(scaled))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(src, 0, 0, scaled.Width, scaled.Height);
        }

        int width = scaled.Width;
        int height = scaled.Height;

        // --- 1) Find candidate orange pixels and compute their average color ---

        double sumR = 0, sumG = 0, sumB = 0;
        int count = 0;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var c = scaled.GetPixel(x, y);
                int r = c.R;
                int g = c.G;
                int b = c.B;

                // Heuristic: bright, R-dominant, G above B (orange-ish)
                if (r > 170 && r > g + 20 && g > b + 10)
                {
                    sumR += r;
                    sumG += g;
                    sumB += b;
                    count++;
                }
            }
        }

        // If we didn't find any obvious "orange" pixels, fall back to generic numeric prep.
        if (count == 0)
        {
            // You can either call your numeric prep on the scaled image,
            // or just return scaled here and let Tesseract handle it.
            return PrepareNumericCrop(src);
        }

        double targetR = sumR / count;
        double targetG = sumG / count;
        double targetB = sumB / count;

        // How tight to be around the average orange.
        // 35–45 works well; you can tweak this after a bit of testing.
        const double maxDistSq = 40 * 40;

        // --- 2) Build a color mask: keep only pixels close to the target orange ---

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var c = scaled.GetPixel(x, y);
                double r = c.R;
                double g = c.G;
                double b = c.B;

                double dr = r - targetR;
                double dg = g - targetG;
                double db = b - targetB;

                double distSq = dr * dr + dg * dg + db * db;

                bool isOrangeLike =
                    distSq <= maxDistSq &&
                    r >= g && g >= b; // avoid bluish / grey background

                scaled.SetPixel(x, y, isOrangeLike ? Color.White : Color.Black);
            }
        }

        // Optional: if you want slightly thicker digits, you can reuse the small
        // 3x3 dilation we wrote earlier here. Usually not necessary once the
        // color mask is good.

        scaled.SetResolution(300, 300);
        return scaled;
    }

    public static Bitmap PrepareStatNameCrop(Bitmap src)
    {
        const int scale = 3;

        // 1) Scale up
        var scaled = new Bitmap(src.Width * scale, src.Height * scale);
        using (var g = Graphics.FromImage(scaled))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(src, 0, 0, scaled.Width, scaled.Height);
        }

        const int threshold = 200;

        int width = scaled.Width;
        int height = scaled.Height;

        // 2) Grayscale + threshold: white text -> black, rest -> white
        //    Also track per-column foreground counts.
        var colCounts = new int[width];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var c = scaled.GetPixel(x, y);
                int gray = (int)(0.299 * c.R + 0.587 * c.G + 0.114 * c.B);

                bool isLetter = gray >= threshold; // bright glyphs
                if (isLetter)
                {
                    scaled.SetPixel(x, y, Color.Black);
                    colCounts[x]++;
                }
                else
                {
                    scaled.SetPixel(x, y, Color.White);
                }
            }
        }

        int totalForeground = 0;
        int maxCount = 0;
        for (int x = 0; x < width; x++)
        {
            totalForeground += colCounts[x];
            if (colCounts[x] > maxCount)
                maxCount = colCounts[x];
        }

        if (totalForeground == 0)
        {
            // nothing detected; just return the scaled image
            scaled.SetResolution(300, 300);
            return scaled;
        }

        // 3) Decide what "real letter" columns look like.
        //    Letters have many black pixels; specks only a couple.
        int minLetterCount = Math.Max(2, maxCount / 8); // tune if needed

        // 4) From right to left, find the last column that looks like part of a letter
        int maxX = -1;
        for (int x = width - 1; x >= 0; x--)
        {
            if (colCounts[x] >= minLetterCount)
            {
                maxX = x;
                break;
            }
        }

        if (maxX == -1)
        {
            scaled.SetResolution(300, 300);
            return scaled;
        }

        // 5) Crop only the RIGHT side, keep full height and x=0 on the left
        const int padRight = 4;
        maxX = Math.Min(width - 1, maxX + padRight);

        int cropW = maxX + 1; // 0..maxX inclusive
        int cropH = height;

        var tight = new Bitmap(cropW, cropH);
        using (var g2 = Graphics.FromImage(tight))
        {
            g2.DrawImage(
                scaled,
                new Rectangle(0, 0, cropW, cropH),
                new Rectangle(0, 0, cropW, cropH),
                GraphicsUnit.Pixel);
        }

        tight.SetResolution(300, 300);
        scaled.Dispose();
        return tight;
    }

    public static async Task<OcrResult> ReadNumericAsync(
    Bitmap src,
    bool isMainStat,
    CancellationToken ct = default)
    {
        // Preprocess according to type
        Bitmap prepped = isMainStat
            ? PrepareMainStatCrop(src)
            : PrepareNumericCrop(src);

        // 1) Try SingleLine first (fast path)
        var result = await ReadDetailAsync(
            prepped,
            lang: "eng",
            profile: OcrProfile.Numeric,
            segMode: PageSegMode.SingleLine,
            ct: ct).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(result.Text))
            return result;

        // 2) Fallback to SingleWord
        result = await ReadDetailAsync(
            prepped,
            lang: "eng",
            profile: OcrProfile.Numeric,
            segMode: PageSegMode.SingleWord,
            ct: ct).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(result.Text))
            return result;

        // 3) As a last resort, try SingleChar (good for lone "4", "8", etc.)
        result = await ReadDetailAsync(
            prepped,
            lang: "eng",
            profile: OcrProfile.Numeric,
            segMode: PageSegMode.SingleChar,
            ct: ct).ConfigureAwait(false);

        return result;
    }

    // DTOs 

    public sealed class OcrResult
    {
        public string Text { get; set; } = "";
        public double Confidence { get; set; }
        public List<OcrLine> Lines { get; set; } = new();
    }

    public sealed class OcrLine
    {
        public Rectangle Bounds { get; set; }
        public List<OcrWord> Words { get; set; } = new();
        public string CombinedText
            => string.Join(" ", Words.ConvertAll(w => w.Text)).Trim();
    }

    public sealed class OcrWord
    {
        public string Text { get; set; } = "";
        public Rectangle Bounds { get; set; }
        public double Confidence { get; set; }
    }
}

internal static class TessIteratorExtensions
{
    public static bool TryGetText(this ResultIterator it, PageIteratorLevel level, out string text)
    {
        try { text = it.GetText(level) ?? string.Empty; return !string.IsNullOrEmpty(text); }
        catch { text = string.Empty; return false; }
    }

    public static Rectangle TryGetRect(this ResultIterator it, PageIteratorLevel level)
    {
        try
        {
            if (it.TryGetBoundingBox(level, out var rect))
                return new Rectangle(rect.X1, rect.Y1, rect.Width, rect.Height);
        }
        catch { /* ignore */ }
        return Rectangle.Empty;
    }

    public static double TryGetConfidence(this ResultIterator it, PageIteratorLevel level)
    {
        try { return it.GetConfidence(level); } catch { return 0.0; }
    }
}

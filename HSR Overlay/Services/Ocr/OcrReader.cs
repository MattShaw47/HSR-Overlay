using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Tesseract;

namespace HSR_Overlay.Services.Ocr;

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
        var res = await ReadDetailAsync(bmp, lang, ct).ConfigureAwait(false);
        return res.Text?.Trim() ?? string.Empty;
    }

    /// <summary>
    /// Read detailed OCR results (blocks/lines/words with bounding boxes and confidences).
    /// </summary>
    public static Task<OcrResult> ReadDetailAsync(Bitmap bmp, string lang = "eng", CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            EnsureEngine(null, lang);

            // Convert to Pix
            using var pix = PixConverter.ToPix(bmp);

            // You can tune PageSegMode to your content. Auto works well for mixed UI text.
            using var page = _engine!.Process(pix, PageSegMode.Auto);

            var result = new OcrResult
            {
                Text = page.GetText() ?? string.Empty,
                Confidence = page.GetMeanConfidence()
            };

            // Walk the result hierarchy for structure.
            using var iterator = page.GetIterator();
            if (iterator == null) return result;

            iterator.Begin();
            var lines = new List<OcrLine>();
            var words = new List<OcrWord>();
            var curLine = default(OcrLine?);

            do
            {
                // Start a new line when at line begin
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

                // Words within the current line
                if (iterator.TryGetText(PageIteratorLevel.Word, out var wtext))
                {
                    var w = new OcrWord
                    {
                        Text = wtext,
                        Bounds = iterator.TryGetRect(PageIteratorLevel.Word),
                        Confidence = iterator.TryGetConfidence(PageIteratorLevel.Word)
                    };
                    (curLine?.Words ?? words).Add(w);
                }
            }
            while (iterator.Next(PageIteratorLevel.Word));

            // Flush last line
            if (curLine is OcrLine last) lines.Add(last);

            result.Lines = lines;
            return result;
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

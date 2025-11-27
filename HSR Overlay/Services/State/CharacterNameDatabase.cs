using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace HSR_Overlay.Services.State;

public sealed class CharacterNameDatabase
{
    // canonicalKey -> list of display/synonym names
    // e.g.  "jingliu": [ "Jingliu" ]
    //       "imbibitor_lunae": [ "Dan Heng Imbibitor Lunae", "Dan Heng • Imbibitor Lunae" ]
    public Dictionary<string, List<string>> Characters { get; private set; }
        = new(StringComparer.OrdinalIgnoreCase);

    public static CharacterNameDatabase Load(string jsonPath)
    {
        if (!File.Exists(jsonPath))
            throw new FileNotFoundException("Character name database JSON not found", jsonPath);

        var json = File.ReadAllText(jsonPath);

        var dict = JsonSerializer.Deserialize<Dictionary<string, List<string>>?>(json);
        if (dict == null)
            throw new Exception("Failed to deserialize character name database.");

        return new CharacterNameDatabase
        {
            Characters = new Dictionary<string, List<string>>(dict, StringComparer.OrdinalIgnoreCase)
        };
    }

    /// <summary>
    /// Tries to map a noisy OCR name into a canonical character key.
    /// Returns null if no match passes the similarity threshold.
    /// </summary>
    public string? ResolveCharacter(string rawName, double minSimilarity = 0.5)
    {
        var cleaned = NormalizeCharacterName(rawName);
        if (string.IsNullOrWhiteSpace(cleaned))
            return null;

        string? bestKey = null;
        double bestScore = 0.0;

        foreach (var kvp in Characters)
        {
            var canonical = kvp.Key;
            var names = kvp.Value;

            foreach (var displayName in names)
            {
                var normDisplay = NormalizeCharacterName(displayName);
                if (string.IsNullOrEmpty(normDisplay))
                    continue;

                // fast path: substring / superstring / exact match
                if (cleaned.Equals(normDisplay, StringComparison.OrdinalIgnoreCase) ||
                    cleaned.Contains(normDisplay, StringComparison.OrdinalIgnoreCase) ||
                    normDisplay.Contains(cleaned, StringComparison.OrdinalIgnoreCase))
                {
                    return canonical;
                }

                // fuzzy token similarity (Jaccard over word set)
                var score = TokenSimilarity(cleaned, normDisplay);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestKey = canonical;
                }
            }
        }

        return bestScore >= minSimilarity ? bestKey : null;
    }

    // Used when you haven't wired a real DB yet
    public static CharacterNameDatabase Empty { get; } = new();

    private static string NormalizeCharacterName(string? s)
    {
        if (string.IsNullOrEmpty(s))
            return string.Empty;

        // unify fancy apostrophes/bullets/etc.
        s = s
            .Replace('\u2019', '\'')
            .Replace('\u2018', '\'')
            .Replace('\u02BC', '\'')
            .Replace('\u2022', ' ')
            .Replace('\u00B7', ' ');

        var sb = new StringBuilder(s.Length);
        foreach (char ch in s)
        {
            if (char.IsLetterOrDigit(ch) || ch == ' ')
            {
                sb.Append(char.ToLowerInvariant(ch));
            }
            else
            {
                // punctuation -> space so "Jingliu-" -> "jingliu "
                sb.Append(' ');
            }
        }

        // collapse multiple spaces, trim ends
        var normalized = Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        return normalized;
    }

    private static double TokenSimilarity(string normalizedA, string normalizedB)
    {
        if (string.IsNullOrEmpty(normalizedA) || string.IsNullOrEmpty(normalizedB))
            return 0.0;

        var tokensA = normalizedA.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var tokensB = normalizedB.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var setA = new HashSet<string>(tokensA);
        var setB = new HashSet<string>(tokensB);

        int intersection = 0;
        foreach (var t in setA)
        {
            if (setB.Contains(t)) intersection++;
        }

        int union = setA.Count + setB.Count - intersection;
        if (union == 0) return 0.0;

        return (double)intersection / union; // 0..1
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace HSR_Overlay.Services.Analysis;

public sealed class RelicPieceDatabase
{
    // setName -> slotKey -> list of piece names
    public Dictionary<string, Dictionary<string, List<string>>> Sets { get; private set; }
        = new(StringComparer.OrdinalIgnoreCase);

    public static RelicPieceDatabase Load(string jsonPath)
    {
        if (!File.Exists(jsonPath))
            throw new FileNotFoundException("Relic piece database JSON not found", jsonPath);

        var json = File.ReadAllText(jsonPath);

        var sets = JsonSerializer.Deserialize<
            Dictionary<string, Dictionary<string, List<string>>>?
        >(json);

        if (sets == null)
            throw new Exception("Failed to deserialize relic piece database.");

        return new RelicPieceDatabase
        {
            Sets = new Dictionary<string, Dictionary<string, List<string>>>(
                sets, StringComparer.OrdinalIgnoreCase)
        };
    }

    /// <summary>
    /// Try to determine (setName, slotKey) from a cleaned relic name.
    /// Returns null if no match found.
    /// </summary>
    public (string Set, RelicSlot Slot)? IdentifyRelicByName(string rawName)
    {
        var cleanedName = NormalizeRelicName(rawName);
        if (string.IsNullOrWhiteSpace(cleanedName))
            return null;

        (string Set, RelicSlot Slot)? bestMatch = null;
        double bestScore = 0.0;

        foreach (var (setName, slotMap) in Sets)
        {
            foreach (var (slotKey, pieces) in slotMap)
            {
                var slotEnum = SlotKeyToEnum(slotKey);

                foreach (var pieceName in pieces)
                {
                    var normPiece = NormalizeRelicName(pieceName);

                    // 1) fast path: perfect substring match
                    if (cleanedName.Contains(normPiece, StringComparison.OrdinalIgnoreCase))
                    {
                        return (setName, slotEnum);
                    }

                    // 2) fuzzy token similarity
                    var score = TokenSimilarity(cleanedName, normPiece);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestMatch = (setName, slotEnum);
                    }
                }
            }
        }

        // 3) require a minimum similarity to accept a fuzzy match
        const double MinSimilarity = 0.5; // you can tune this

        return bestScore >= MinSimilarity ? bestMatch : null;
    }

    public static RelicSlot SlotKeyToEnum(string slotKey)
    {
        return slotKey.ToLowerInvariant() switch
        {
            "head" => RelicSlot.Head,
            "hands" => RelicSlot.Hands,
            "body" => RelicSlot.Body,
            "feet" => RelicSlot.Feet,
            "orb" => RelicSlot.Sphere,
            "rope" => RelicSlot.Rope,
            _ => RelicSlot.None
        };
    }

    private static string NormalizeRelicName(string? s)
    {
        if (string.IsNullOrEmpty(s))
            return string.Empty;

        // unify fancy apostrophes into a normal ASCII '
        s = s
            .Replace('\u2019', '\'')
            .Replace('\u2018', '\'')
            .Replace('\u02BC', '\'');


        var sb = new StringBuilder(s.Length);
        foreach (char ch in s)
        {
            if (char.IsLetterOrDigit(ch) || ch == ' ')
            {
                sb.Append(char.ToLowerInvariant(ch));
            }
            else
            {
                // turn punctuation into spaces so "Silver-Rimmed" -> "Silver Rimmed"
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

        return (double)intersection / union; // range [0,1]
    }
}

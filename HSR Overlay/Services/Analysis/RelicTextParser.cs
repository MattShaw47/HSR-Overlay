using HSR_Overlay.Services.State;
using HSR_Overlay.Util;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Shapes;

namespace HSR_Overlay.Services.Analysis;

internal class RelicTextParser : IRelicTextParser
{
    private static readonly HashSet<string> StatKeywords =
    new(StringComparer.OrdinalIgnoreCase)
    {
        "hp",
        "atk",
        "def",
        "spd",
        "crit",
        "effect",
        "break",
        "res",
        "energy",
        "healing",
        "outgoing",
        "dmg"
    };

    private readonly RelicPieceDatabase _pieces;

    public RelicTextParser(RelicPieceDatabase pieces)
    {
        _pieces = pieces;
    }

    public ParsedRelic Parse(string rawOcrText, RelicTextContext context)
    {
        List<string> cleanedText = [];

        switch (context.Source)
        {
            case RelicTextSource.CharacterScreen:
                cleanedText = CleanFromCharacterScreen(rawOcrText); break;
            case RelicTextSource.Farming:
                cleanedText = CleanFromFarming(rawOcrText); break;
        };

        return StringToParsedRelic(cleanedText, _pieces);
    }

    public EquippedRelic ParseEquippedFromCharacterScreen(string rawText, CharacterNameDatabase characters)
    {
        var (relicLines, characterKey) = CleanCharacterScreenWithCharacter(rawText);

        var parsedRelic = StringToParsedRelic(relicLines, _pieces);

        string finalCharacterKey = string.Empty;

        if (!string.IsNullOrWhiteSpace(characterKey))
        {
            var resolved = characters.ResolveCharacter(characterKey);

            if(!string.IsNullOrWhiteSpace(resolved))
            {
                finalCharacterKey = resolved;
            }
        }

        return new EquippedRelic
        {
            CharacterKey = finalCharacterKey,
            Relic = parsedRelic
        };
    }

    private List<string> CleanFromCharacterScreen(string text)
    {
        var (lines, _) = CleanCharacterScreenWithCharacter(text);
        return lines;
    }

    /// <summary>
    /// Cleans up raw OCR from the character equipment screen and returns:
    /// - relicLines: lines usable by StringToParsedRelic (name + stats)
    /// - characterKey: cleaned character name (e.g. "jingliu") if found
    /// </summary>
    private (List<string> relicLines, string? characterKey) CleanCharacterScreenWithCharacter(string text)
    {
        var relicLines = new List<string>();
        string? charKey = null;

        if (string.IsNullOrWhiteSpace(text))
            return (relicLines, charKey);

        // normalize newlines and lowercase
        text = text.Replace("\r\n", "\n").ToLowerInvariant();

        // keep only "safe" chars: letters, digits, %, +, -, ., space, newline, slash, apostrophe
        var sb = new StringBuilder(text.Length);
        foreach (char ch in text)
        {
            if ((ch >= 'a' && ch <= 'z') ||
                char.IsDigit(ch) ||
                ch == '%' || ch == '+' || ch == '-' || ch == '.' ||
                ch == ' ' || ch == '\n' || ch == '/' || ch == '\'')
            {
                sb.Append(ch);
            }
            else
            {
                sb.Append(' ');
            }
        }

        var normalized = sb.ToString();

        // collapse multiple spaces
        normalized = Regex.Replace(normalized, @"[ ]{2,}", " ");

        // split into trimmed, non-empty lines
        var lines = normalized
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        if (lines.Count == 0)
            return (relicLines, charKey);

        // --- 1) find character line (contains '/') BEFORE mutating lines ---

        int charLineIndex = lines.FindIndex(l => l.Contains('/'));
        if (charLineIndex >= 0)
        {
            charKey = ExtractCharacterKey(lines[charLineIndex]);
        }

        // --- 2) build name from a block of non-stat lines at the BOTTOM ---

        string nameLine = string.Empty;
        var nameIndices = new HashSet<int>();

        bool collecting = false;
        for (int i = lines.Count - 1; i >= 0; i--)
        {
            if (i == charLineIndex)
                continue;

            string line = lines[i];

            bool isNameCandidate = !HasDigit(line) && !ContainsStatKeyword(line);
            if (isNameCandidate)
            {
                collecting = true;
                nameIndices.Add(i);
            }
            else if (collecting)
            {
                // we already started grabbing name lines; once we hit a non-name, stop
                break;
            }
        }

        if (nameIndices.Count > 0)
        {
            var ordered = nameIndices.OrderBy(i => i).ToList();
            var parts = ordered.Select(i => lines[i]);
            nameLine = string.Join(" ", parts).Trim();
        }

        // Fallback: if somehow we didn't find a name block, try last non-stat, non-digit line
        if (string.IsNullOrWhiteSpace(nameLine))
        {
            for (int i = lines.Count - 1; i >= 0; i--)
            {
                if (i == charLineIndex) continue;
                var line = lines[i];
                if (!HasDigit(line) && !ContainsStatKeyword(line))
                {
                    nameLine = line.Trim();
                    nameIndices.Add(i);
                    break;
                }
            }
        }

        // --- 3) build stat candidates from the rest ---

        var statCandidates = new List<string>();

        for (int i = 0; i < lines.Count; i++)
        {
            if (i == charLineIndex)
                continue;

            if (nameIndices.Contains(i))
                continue;

            var line = lines[i];

            bool hasDigit = HasDigit(line);
            bool hasKeyword = ContainsStatKeyword(line);

            // Only keep things that look remotely stat-related:
            // either they have digits (values) or stat keywords (labels).
            if (hasDigit || hasKeyword)
            {
                // tidy trailing ".": "10.3% ." -> "10.3%", "22.0%." -> "22.0%"
                line = Regex.Replace(line, @"(%)[\.\s]*$", "$1");
                line = line.Trim();

                if (!string.IsNullOrWhiteSpace(line))
                    statCandidates.Add(line);
            }
        }

        relicLines = MergeStatLines(nameLine, statCandidates);
        return (relicLines, charKey);
    }

    private List<string> CleanFromFarming(string text)
    {
        // to lower case
        text = text.ToLowerInvariant();

        // normalize line endings
        text = text.Replace("\r\n", "\n");

        // collapse random extra spaces
        text = Regex.Replace(text, @"[ ]{2,}", " ");
        text = text.Trim();

        // split into list of strings by \n
        List<string> lines = new(
            text.Split(new[] { "\n" }, StringSplitOptions.RemoveEmptyEntries));

        // remove OCR junk prefixes (@, &, etc.)
        lines = CleanTextPrefixes(lines);

        if (lines.Count == 0)
            return lines;

        // --- remove obvious junk: any "2-pc" line ---

        // look for a "2-pc" description line anywhere
        int twoPcIndex = lines.FindIndex(l => l.Contains("2-pc"));

        if (twoPcIndex >= 0)
        {
            // With the new tighter OCR crops, we only strip the actual 2-pc line,
            // and leave the surrounding lines alone (they may contain the relic name).
            lines.RemoveAt(twoPcIndex);
        }
        else
        {
            // no 2-pc line: in the older layout the trailing line was usually just
            // the set name (no digits), which we still strip if it looks non-stat-ish.
            if (lines.Count > 1 && !HasDigit(lines[^1]))
            {
                lines.RemoveAt(lines.Count - 1);
            }
        }

        if (lines.Count <= 1)
            return lines;

        string nameLine = lines[0];

        // --- detect "split numbers" layout ---

        // find the first line that looks like a standalone number / percentage
        int firstNumericIndex = -1;
        for (int i = 1; i < lines.Count; i++)
        {
            if (IsStandaloneNumber(lines[i]))
            {
                firstNumericIndex = i;
                break;
            }
        }

        // if no pure-number block, we’re in the original layout:
        // [name, "hp 112", "def 21", ...] just return as-is
        if (firstNumericIndex == -1)
            return lines;

        // Otherwise: lines[1..firstNumericIndex-1] are stat labels,
        // lines[firstNumericIndex..] are numeric values.
        var statLabels = lines.GetRange(1, firstNumericIndex - 1);
        var statValues = lines.GetRange(firstNumericIndex, lines.Count - firstNumericIndex);

        int pairCount = Math.Min(statLabels.Count, statValues.Count);

        var merged = new List<string>(capacity: 1 + pairCount)
    {
        nameLine
    };

        for (int i = 0; i < pairCount; i++)
        {
            // e.g. "hp" + "112" -> "hp 112"
            //      "effect res" + "4.3%" -> "effect res 4.3%"
            merged.Add($"{statLabels[i]} {statValues[i]}".Trim());
        }

        return merged;
    }

    private static ParsedRelic StringToParsedRelic(List<string> lines, RelicPieceDatabase pieceDb)
    {
        if (lines == null || lines.Count < 2)
        {
            return new ParsedRelic
            {
                Set = string.Empty,
                Slot = RelicSlot.None,
                MainStat = Stat.None,
                MainStatValue = -1,
                Substats = []
            };
        }

        string nameLine = lines[0];

        var identity = pieceDb.IdentifyRelicByName(nameLine);

        if (identity == null)
        {
            return new ParsedRelic
            {
                Set = string.Empty,
                Slot = RelicSlot.None,
                MainStat = Stat.None,
                MainStatValue = -1,
                Substats = []
            };
        }

        var (setName, slotKey) = identity.Value;

        var (mainStat, mainStatVal) = ParseStat(lines[1]);

        lines.RemoveRange(0, 2);

        List<Substat> substats = new();

        foreach (var line in lines)
        {
            var (stat, statVal) = ParseStat(line);
            substats.Add(new Substat
            {
                Stat = stat,
                Value = statVal,
            });
        }

        return new ParsedRelic
        {
            Set = setName,
            Slot = slotKey,
            MainStat = mainStat,
            MainStatValue = mainStatVal,
            Substats = substats
        };
    }

    private static (Stat stat, double value) ParseStat(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return (Stat.None, -1);

        line = line.Trim().TrimEnd('.').Trim().TrimEnd('.');

        line = Regex.Replace(line, @"\s*[-+:]\s*(\d)", " $1");

        // detect percent and remove trailing '%' for parsing
        bool isPercent = line.EndsWith("%");
        if (isPercent)
        {
            line = line[..^1].Trim();
        }

        // split into "stat name" and "value" at the last space
        int lastSpace = line.LastIndexOf(' ');
        if (lastSpace < 0)
        {
            // no space found -> can't split name/value
            return (Stat.None, -1);
        }

        string statNamePart = line[..lastSpace].Trim();
        string valuePart = line[(lastSpace + 1)..].Trim();

        // parse numeric value
        if (!double.TryParse(valuePart, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
        {
            return (Stat.None, -1);
        }

        // map stat name + percent flag to Stat enum
        Stat stat = Stat.None;

        switch (statNamePart)
        {
            case "hp":
                stat = isPercent ? Stat.HpPercent : Stat.HpFlat;
                break;

            case "atk":
                stat = isPercent ? Stat.AtkPercent : Stat.AtkFlat;
                break;

            case "def":
                stat = isPercent ? Stat.DefPercent : Stat.DefFlat;
                break;

            case "crit rate":
                stat = Stat.CritRate;
                break;

            case "crit dmg":
                stat = Stat.CritDamage;
                break;

            case "spd":
                stat = Stat.Speed;
                break;

            case "break effect":
                stat = Stat.BreakEffect;
                break;

            case "energy regeneration rate":
                stat = Stat.EnergyRegen;
                break;

            case "effect hit rate":
                stat = Stat.EffectHitRate;
                break;

            case "effect res":
                stat = Stat.EffectRes;
                break;
            case "outgoing healing boost":
                stat = Stat.OutgoingHealingBoost;
                break;
            case "physical dmg boost":
                stat = Stat.PhysicalDMGBoost;
                break;
            case "fire dmg boost":
                stat = Stat.FireDMGBoost;
                break;
            case "ice dmg boost":
                stat = Stat.IceDMGBoost;
                break;
            case "wind dmg boost":
                stat = Stat.WindDMGBoost;
                break;
            case "lightning dmg boost":
                stat = Stat.LightningDMGBoost;
                break;
            case "quantum dmg boost":
                stat = Stat.QuantumDMGBoost;
                break;
            case "imaginary dmg boost":
                stat = Stat.ImaginaryDMGBoost;
                break;

            default:
                stat = Stat.None;
                break;
        }

        return (stat, value);
    }

    private static List<string> CleanTextPrefixes(List<String> lines)
    {
        var result = new List<string>(lines.Count);

        foreach (var raw in lines)
        {
            var line = raw?.Trim();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
            if (tokens.Count >= 2)
            {
                string first = tokens[0].TrimEnd(',', '.');
                string second = tokens[1].TrimEnd(',', '.');

                // if the second token looks like a stat keyword,
                // and the first token is short junk (len <= 3 and not itself a keyword),
                // drop the first token.
                if (first.Length <= 3 &&
                    !StatKeywords.Contains(first) &&
                    StatKeywords.Contains(second))
                {
                    tokens.RemoveAt(0);
                }
            }

            line = string.Join(' ', tokens);

            result.Add(line);
        }

        return result;
    }

    private static bool HasDigit(string s)
    {
        foreach (char c in s)
        {
            if (char.IsDigit(c)) return true;
        }
        return false;
    }

    private static bool IsStandaloneNumber(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return false;

        s = s.Trim();

        // strip leading '+' / '-' and trailing '%'
        if (s.StartsWith("+") || s.StartsWith("-"))
            s = s.Substring(1).TrimStart();

        if (s.EndsWith("%"))
            s = s.Substring(0, s.Length - 1).TrimEnd();

        return double.TryParse(s,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out _);
    }

    private static bool ContainsStatKeyword(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return false;

        string lower = line.ToLowerInvariant();

        foreach (var keyword in StatKeywords)
        {
            if (lower.Contains(keyword))
                return true;
        }

        return false;
    }

    private static string ExtractCharacterKey(string rawCharacterLine)
    {
        if (string.IsNullOrWhiteSpace(rawCharacterLine))
            return string.Empty;

        // normalize whitespace
        string s = rawCharacterLine.Replace("\r\n", " ");
        s = Regex.Replace(s, @"\s+", " ").Trim();

        // if there's a '/', assume "path / name" and keep the right side
        int slash = s.IndexOf('/');
        if (slash >= 0 && slash + 1 < s.Length)
        {
            s = s[(slash + 1)..].Trim();
        }

        // keep only letters, digits, and spaces
        var sb = new StringBuilder(s.Length);
        foreach (char ch in s)
        {
            if (char.IsLetterOrDigit(ch) || ch == ' ')
                sb.Append(ch);
        }

        s = sb.ToString().Trim();

        // normalize case: upper first letter of each word if you want pretty keys
        // or just lower-case and let your character DB handle mapping
        return s;
    }

    private static List<string> MergeStatLines(string nameLine, List<string> statCandidates)
    {
        var result = new List<string>();

        if (!string.IsNullOrWhiteSpace(nameLine))
            result.Add(nameLine.Trim());

        if (statCandidates == null || statCandidates.Count == 0)
            return result;

        var labels = new List<string>();
        var values = new List<string>();

        foreach (var raw in statCandidates)
        {
            var line = raw.Trim();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            bool hasDigit = HasDigit(line);
            bool hasKeyword = ContainsStatKeyword(line);

            // Lines that already look like "crit dmg 22.0%" → keep as-is.
            if (hasDigit && hasKeyword)
            {
                result.Add(NormalizeLabelAndValue(line));
                continue;
            }

            if (!hasDigit && hasKeyword)
            {
                // looks like a pure label: "hp", "atk", "crit dmg"
                labels.Add(line);
            }
            else if (hasDigit && !hasKeyword)
            {
                // looks like a pure numeric value: "705", "10.3%", "7. 3.8%", "5.4%", "22.0%"
                values.Add(line);
            }
            else
            {
                // Neither clearly label nor value; ignore for now.
            }
        }

        int pairCount = Math.Min(labels.Count, values.Count);

        for (int i = 0; i < pairCount; i++)
        {
            string label = labels[i].Trim();
            string value = NormalizeNumericValue(values[i]);

            if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(value))
                continue;

            result.Add($"{label} {value}");
        }

        return result;
    }

    private static string NormalizeLabelAndValue(string line)
    {
        // e.g. "crit dmg 22.0%." → "crit dmg 22.0%"
        line = Regex.Replace(line, @"[ ]{2,}", " ").Trim();
        // remove trailing junk after % or digits
        line = Regex.Replace(line, @"(%|\d)[\.\s]*$", "$1");
        return line.Trim();
    }

    private static string NormalizeNumericValue(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return string.Empty;

        line = line.Trim();

        // Remove obvious trailing punctuation / junk: "10.3% ." -> "10.3%", "22.0%." -> "22.0%"
        line = Regex.Replace(line, @"(%|\d)[\.\s]*$", "$1");

        // If the line has multiple numbers (e.g. "7. 3.8%"), keep the last one.
        var matches = Regex.Matches(line, @"\d+(\.\d+)?");
        if (matches.Count > 1)
        {
            var last = matches[matches.Count - 1].Value;
            bool isPercent = line.Contains('%');
            return isPercent ? $"{last}%" : last;
        }

        return line.Trim();
    }
}

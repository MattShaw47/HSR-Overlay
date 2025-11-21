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
        "outgoing"
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

    private List<string> CleanFromCharacterScreen(string text)
    {
        return new List<string>();
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

        // --- remove set name + 2-pc line (if present) ---

        // look for a "2-pc" description line anywhere
        int twoPcIndex = lines.FindIndex(l => l.Contains("2-pc"));

        if (twoPcIndex >= 1)
        {
            // remove 2-pc line and the immediately preceding set-name line
            // remove higher index first so indices stay valid
            lines.RemoveAt(twoPcIndex);       // "2-pc: increases spd by 6%."
            lines.RemoveAt(twoPcIndex - 1);   // "sacerdos' relived ordeal"
        }
        else
        {
            // no 2-pc line: layout 1 case, trailing line is usually just set name
            // (no digits, unlike stat lines which all contain numbers)
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
        // [name, "hp 112", "def 21", ...] → just return as-is
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
            string stat = statLabels[i].Trim();
            string value = statValues[i].Trim();

            if (string.IsNullOrEmpty(stat) || string.IsNullOrEmpty(value))
                continue;

            merged.Add($"{stat} {value}");
        }

        return merged;
    }

    private static ParsedRelic StringToParsedRelic(List<string> lines, RelicPieceDatabase pieceDb)
    {
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

        line = line.Trim();

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
}

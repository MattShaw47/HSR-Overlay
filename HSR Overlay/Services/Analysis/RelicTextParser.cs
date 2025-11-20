using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace HSR_Overlay.Services.Analysis;

internal class RelicTextParser : IRelicTextParser
{
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
        text = text.ToLower();

        // normalize line endings
        text = text.Replace("\r\n", "\n");

        // remove random whitespace
        text = Regex.Replace(text, @"[  ]{2,}", " ");
        text = text.Trim();

        // split into list of strings by \n
        List<string> lines = new(
            text.Split(new string[] { "\n" },
            StringSplitOptions.RemoveEmptyEntries)
            );

        // remove special characters from ocr trying to read symbols
        lines = CleanTextPrefixes(lines);

        // Remove unnecessary additional lines
        if (lines[lines.Count - 1].StartsWith("2-P"))
            lines.RemoveAt(lines.Count - 1);

        lines.RemoveAt((lines.Count - 1));

        return lines;
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
        List<string> endpoints =
        [
            "hp",
            "atk",
            "def",
            "crit",
            "effect",
            "spd",
            "break",
        ];

        List<string> linesCleaned = new(lines.Count);

        foreach (string line in lines)
        {
            int earliestIndex = -1;

            foreach (var endpoint in endpoints)
            {
                int idx = line.IndexOf(endpoint, StringComparison.OrdinalIgnoreCase);

                if (idx >= 0 && (earliestIndex == -1 || idx < earliestIndex))
                {
                    earliestIndex = idx;
                }
            }

            if (earliestIndex == -1)
            {
                linesCleaned.Add(line);
                continue;
            }

            string prefix = line[..earliestIndex];
            string suffix = line[earliestIndex..];

            var goodPrefix = new string(prefix.Where(c => !IsSpecial(c)).ToArray());

            linesCleaned.Add(goodPrefix + suffix);
        }

        return linesCleaned;
    }

    private static bool IsSpecial(char c)
    {
        return !char.IsLetterOrDigit(c);
    }
}

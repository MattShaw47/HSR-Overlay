using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HSR_Overlay.Services.Analysis;

public static class RelicStatSanitizer
{
    public static ParsedRelic Sanitize(ParsedRelic relic, RelicStatTables tables)
    {
        var sanitized = relic;

        // --- main stat ---
        if (sanitized.MainStat != Stat.None && sanitized.MainStatValue > 0)
        {
            var v = SanitizeValue(
                stat: sanitized.MainStat,
                rawValue: sanitized.MainStatValue,
                isMain: true,
                tables: tables);

            if (v > 0)
            {
                sanitized.MainStatValue = v;
            }
            else
            {
                // Drop main stat if we really can't make sense of it
                sanitized.MainStat = Stat.None;
                sanitized.MainStatValue = -1;
            }
        }

        // --- substats ---
        var cleanedSubs = new List<Substat>();

        foreach (var sub in relic.Substats)
        {
            if (sub.Stat == Stat.None || sub.Value <= 0)
                continue;

            var v = SanitizeValue(
                stat: sub.Stat,
                rawValue: sub.Value,
                isMain: false,
                tables: tables);

            if (v > 0)
            {
                cleanedSubs.Add(new Substat
                {
                    Stat = sub.Stat,
                    Value = v
                });
            }
            else
            {
                // Optional: log when a substat is so wild we discard it
                // Log.Debug("relic", $"Dropping implausible substat {sub.Stat}={sub.Value}");
            }
        }

        sanitized.Substats = cleanedSubs;
        return sanitized;
    }

    private static double SanitizeValue(
        Stat stat,
        double rawValue,
        bool isMain,
        RelicStatTables tables)
    {
        double v = Math.Abs(rawValue);

        try
        {
            if (isMain)
            {
                if (!tables.TryGetMainDef(stat, out var def))
                    return v; // unknown main stat, just leave it

                double max = def.MaxValue;

                // within 20% of max is fine
                if (v <= max * 1.2)
                    return Round1(v);

                // common OCR bug: lost decimal → divide by 10
                // e.g. 318% (31.8%), 432 (43.2%), etc.
                if (v <= max * 15.0)
                {
                    double d10 = v / 10.0;
                    if (d10 <= max * 1.2)
                        return Round1(d10);
                }

                // Paranoid: sometimes /100 makes sense
                if (v <= max * 150.0)
                {
                    double d100 = v / 100.0;
                    if (d100 <= max * 1.2)
                        return Round1(d100);
                }

                // Totally insane, give up
                return -1;
            }
            else
            {
                if (!tables.TryGetSubstatDef(stat, out var sdef))
                    return v; // unknown substat

                // Max plausible substat value: 6 rolls of high
                double max = sdef.High * 6.0;

                if (v <= max * 1.2)
                    return Round1(v);

                // Again, try /10 for decimal issues
                if (v <= max * 15.0)
                {
                    double d10 = v / 10.0;
                    if (d10 <= max * 1.2)
                        return Round1(d10);
                }

                // And /100 if really off
                if (v <= max * 150.0)
                {
                    double d100 = v / 100.0;
                    if (d100 <= max * 1.2)
                        return Round1(d100);
                }

                return -1;
            }
        }
        catch
        {
            // If the lookup blows up for some reason, just keep what we had
            return v;
        }
    }

    private static double Round1(double v)
        => Math.Round(v, 1, MidpointRounding.AwayFromZero);
}

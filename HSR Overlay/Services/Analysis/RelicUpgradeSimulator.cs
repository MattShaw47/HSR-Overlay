using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HSR_Overlay.Services.Analysis;

internal class RelicUpgradeSimulator
{
    private static readonly Random Rng = new();

    public static double ProbabilityBeatsReference(
        ParsedRelic candidate,
        int candidateLevel,
        ParsedRelic referenceRelic,
        RelicWeightsProfile profile,
        RelicStatTables tables,
        int trials)
    {
        var refRolls = GetRolls(referenceRelic, tables);
        double refScore = ScoreRelic(referenceRelic, refRolls, profile, tables);

        int remainingRolls = (15 - candidateLevel) / 3;
        if (remainingRolls <= 0)
        {
            var candRolls = GetRolls(candidate, tables);
            double candScore = ScoreRelic(candidate, candRolls, profile, tables);
            return candScore > refScore
                ? 1.0
                : (candScore < refScore ? 0.0 : 0.5);
        }

        int better = 0;

        for (int t = 0; t < trials; t++)
        {
            var rolls = GetRolls(candidate, tables);
            SimulateUpgrades(candidate, rolls, remainingRolls, tables);
            double score = ScoreRelic(candidate, rolls, profile, tables);
            if (score > refScore) better++;
        }

        return (double)better / trials;
    }

    private static Dictionary<Stat, double> GetRolls(ParsedRelic relic, RelicStatTables tables)
        => RelicAnalyzerPrivateHelpers.GetRolls(relic, tables);

    private static double ScoreRelic(
        ParsedRelic relic,
        IReadOnlyDictionary<Stat, double> rolls,
        RelicWeightsProfile profile,
        RelicStatTables tables)
        => RelicAnalyzerPrivateHelpers.ScoreRelic(relic, rolls, profile, tables);

    private static void SimulateUpgrades(
        ParsedRelic relic,
        Dictionary<Stat, double> rolls,
        int remainingRolls,
        RelicStatTables tables)
    {
        int currentSubs = rolls.Count;

        for (int i = 0; i < remainingRolls; i++)
        {
            if (currentSubs < 4)
            {
                var newStat = SampleNewSubstat(relic, rolls, tables);
                var mult = SampleRollMultiplier(newStat, tables);
                rolls[newStat] = mult;
                currentSubs++;
            }
            else
            {
                var index = Rng.Next(rolls.Count);
                var stat = rolls.Keys.ElementAt(index);
                var mult = SampleRollMultiplier(stat, tables);
                rolls[stat] += mult;
            }
        }
    }

    private static Stat SampleNewSubstat(
        ParsedRelic relic,
        Dictionary<Stat, double> existing,
        RelicStatTables tables)
    {
        var candidates = new List<(Stat stat, int weight)>();

        foreach (var stat in tables.AllSubstats)
        {
            if (stat == Stat.None) continue;
            if (stat == relic.MainStat) continue;
            if (existing.ContainsKey(stat)) continue;
            candidates.Add((stat, tables.GetAppearanceWeight(stat)));
        }

        int total = candidates.Sum(c => c.weight);
        int roll = Rng.Next(1, total + 1);
        int acc = 0;

        foreach (var c in candidates)
        {
            acc += c.weight;
            if (roll <= acc) return c.stat;
        }

        return candidates[^1].stat;
    }

    private static double SampleRollMultiplier(Stat stat, RelicStatTables tables)
    {
        var (low, med, high) = tables.GetRollMultipliers(stat);
        int r = Rng.Next(3);
        return r switch
        {
            0 => low,
            1 => med,
            _ => high
        };
    }
}

internal class RelicAnalyzerPrivateHelpers
{
    public static Dictionary<Stat, double> GetRolls(ParsedRelic relic, RelicStatTables tables)
    {
        var dict = new Dictionary<Stat, double>();

        foreach (var sub in relic.Substats)
        {
            if (sub.Stat == Stat.None) continue;
            dict[sub.Stat] = tables.ToRolls(sub.Stat, sub.Value);
        }

        return dict;
    }

    public static double GetMainStatRolls(ParsedRelic relic, RelicStatTables tables)
    {
        if (relic.MainStat == Stat.None)
            return 0.0;

        return tables.GetMainStatRolls(relic.MainStat);
    }

    public static double ScoreRelic(ParsedRelic relic, IReadOnlyDictionary<Stat, double> rolls, RelicWeightsProfile profile, RelicStatTables tables)
    {
        double score = 0.0;

        // main stat bonus (you can make this smarter later)
        if (relic.MainStat == profile.DesiredMainStat)
        {
            score += GetMainStatRolls(relic, tables);
        }


        // substats
        foreach (var (stat, rollCount) in rolls)
        {
            if (!profile.SubstatWeights.TryGetValue(stat, out var weight))
                continue;

            score += rollCount * weight;
        }

        return score;
    }
}

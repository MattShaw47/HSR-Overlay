using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HSR_Overlay.Services.Analysis;

public sealed class RelicStatTables
{
    private readonly Dictionary<Stat, SubstatRollDefinition> _substats;
    private readonly Dictionary<Stat, MainStatDefinition> _mainstats;

    public RelicStatTables(RelicStatConfig config)
    {
        _substats = config.Substats.ToDictionary(s => s.Stat);
        _mainstats = config.MainStats.ToDictionary(m => m.Stat);
    }

    public double GetMed(Stat stat) => _substats[stat].Med;
    public double GetLow(Stat stat) => _substats[stat].Low;
    public double GetHigh(Stat stat) => _substats[stat].High;

    public int GetAppearanceWeight(Stat stat) => _substats[stat].AppearanceWeight;

    // value in-game -> normalized roll count
    public double ToRolls(Stat stat, double value)
        => value / GetMed(stat);

    public (double lowMult, double medMult, double highMult) GetRollMultipliers(Stat stat)
    {
        var def = _substats[stat];
        var m = def.Med;
        return (def.Low / m, 1.0, def.High / m);
    }

    public double GetMainStatRolls(Stat stat)
    {
        if (!_mainstats.TryGetValue(stat, out var def))
            throw new ArgumentException($"No main stat definition for {stat}", nameof(stat));

        if (def.EquivalentSubstat is Stat sub && sub != Stat.None)
        {
            var med = GetMed(sub);
            return def.MaxValue / med;
        }

        if (def.EquivalentRolls is double rolls)
            return rolls;

        // default arbitrary fallback
        return 10.0;
    }

    public IEnumerable<Stat> AllSubstats => _substats.Keys;

    public bool TryGetSubstatDef(Stat stat, out SubstatRollDefinition def)
            => _substats.TryGetValue(stat, out def);

    public bool TryGetMainDef(Stat stat, out MainStatDefinition def)
        => _mainstats.TryGetValue(stat, out def);

    public double GetMainMax(Stat stat)
        => _mainstats[stat].MaxValue;
}


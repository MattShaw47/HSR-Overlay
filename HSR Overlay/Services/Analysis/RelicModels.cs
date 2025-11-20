using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HSR_Overlay.Services.Analysis;

public sealed class ParsedRelic
{
    public string Set { get; init; } = String.Empty;

    public RelicSlot Slot { get; init; } = RelicSlot.None;

    public Stat MainStat { get; init; } = Stat.None;
    public double MainStatValue { get; init; }

    public IReadOnlyList<Substat> Substats { get; init; } = Array.Empty<Substat>();

    public bool HasSubstat(Stat stat) =>
        Substats.Any(s => s.Stat == stat);
}

public class Substat
{
    public required Stat Stat {  get; init; } = Stat.None;
    public required double Value { get; init; }

    public int Rolls { get; init; }
}

public class RelicEvaluation
{
    public required ParsedRelic Relic { get; init; }

    // key: character name
    // val: improvement chance in %
    public Dictionary<String, double> ImprovementChances { get; init; } = new Dictionary<String, double>();
}
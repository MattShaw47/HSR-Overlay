using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HSR_Overlay.Services.Analysis;

public sealed class ParsedRelic
{
    public RelicSet Set { get; init; } = RelicSet.None;

    public RelicSlot Slot { get; init; } = RelicSlot.None;

    public Stat MainStat { get; init; } = Stat.None;
    public double MainStatValue { get; init; }

    public IReadOnlyList<Substat> Substats { get; init; } = Array.Empty<Substat>();

    public bool HasSubstat(Stat stat) =>
        Substats.Any(s => s.Stat == stat);
}

public class Substat
{
    public Stat Stat {  get; init; } = Stat.None;
    public double Value { get; init; }

    public int Rolls { get; init; }
}

public class RelicEvaluation
{
    public required ParsedRelic Relic { get; init; }
    public Dictionary<String, double> ImprovementChances { get; init; } = new Dictionary<String, double>();
    
    // I figure the actual creation of a displayable string from this info can get handled by the MainWindow code?
}
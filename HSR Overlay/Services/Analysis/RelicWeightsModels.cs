using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HSR_Overlay.Services.Analysis;

public class CharacterRelicProfile
{
    public IReadOnlyList<string> PlanarSets { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Set1Candidates { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Set2Candidates { get; init; } = Array.Empty<string>();

    public Dictionary<string, RelicSlotWeights> Slots { get; init; } = new (StringComparer.OrdinalIgnoreCase);
}

public class RelicSlotWeights
{
    public string MainStat { get; init; } = "";

    public Dictionary<string, double> SubstatWeights { get; init; } = new Dictionary<string, double>();
}

public class RelicWeightsProfile
{
    public required string CharacterKey { get; init; }
    public required string SlotKey { get; init; }
    public required string SetName { get; init; }

    public required string DesiredMainStat { get; init; }

    public Dictionary<string, double> SubstatWeights { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public string? PreferredSet1 { get; init; }
    public string? PreferredSet2 { get; init; }
    public string? PreferredPlanar { get; init; }
}
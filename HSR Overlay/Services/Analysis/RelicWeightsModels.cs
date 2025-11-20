using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HSR_Overlay.Services.Analysis;

public class CharacterRelicProfile
{
    public string PlanarSet { get; init; } = string.Empty;
    public string Set1Candidate { get; init; } = string.Empty;
    public string Set2Candidate { get; init; } = string.Empty;

    public Dictionary<RelicSlot, RelicSlotWeights> Slots { get; init; } = [];
}

public class RelicSlotWeights
{
    public string MainStat { get; init; } = "";

    public Dictionary<string, double> SubstatWeights { get; init; } = new Dictionary<string, double>();
}

public class RelicWeightsProfile
{
    public required string CharacterKey { get; init; }
    public required RelicSlot SlotKey { get; init; }
    public required string SetName { get; init; }

    public required string DesiredMainStat { get; init; }

    public Dictionary<string, double> SubstatWeights { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public string? PreferredSet1 { get; init; }
    public string? PreferredSet2 { get; init; }
    public string? PreferredPlanar { get; init; }
}
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
    public Stat MainStat { get; init; } = Stat.None;

    public Dictionary<Stat, double> SubstatWeights { get; init; } = new Dictionary<Stat, double>();
}

public class RelicWeightsProfile
{
    public required string CharacterKey { get; init; }
    public required RelicSlot SlotKey { get; init; }
    public required string SetName { get; init; }

    public required Stat DesiredMainStat { get; init; }

    public Dictionary<Stat, double> SubstatWeights { get; init; } = new();

    public string? PreferredSet1 { get; init; }
    public string? PreferredSet2 { get; init; }
    public string? PreferredPlanar { get; init; }
}
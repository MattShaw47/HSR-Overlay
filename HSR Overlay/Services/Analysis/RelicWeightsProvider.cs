using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HSR_Overlay.Services.Analysis;

internal class RelicWeightsProvider : IRelicWeightsProvider
{
    // key: character name
    private readonly Dictionary<string, CharacterRelicProfile> _characters;

    public RelicWeightsProvider(Dictionary<string, CharacterRelicProfile> characters)
    {
        _characters = characters;
    }

    // returns all weight profiles for all characters who have slot weights for this slotKey and consider this setName relevant for that slot
    IReadOnlyList<IRelicWeightsProvider> IRelicWeightsProvider.GetProfile(string relicSetName, string slotKey)
    {
        var results = new List<RelicWeightsProfile>();

        foreach (var pair in _characters)
        {
            var characterKey = pair.Key;
            var profile = pair.Value;

            if (!profile.Slots.TryGetValue(slotKey, out var slotWeights))
                continue;

            if (!IsSetRelevantForSlot(profile, relicSetName, slotKey))
                continue;

            results.Add(CreateWeightsProfile(
                characterKey,
                relicSetName,
                slotKey,
                profile,
                slotWeights));
        }

        return (IReadOnlyList<IRelicWeightsProvider>)results;
    }

    private bool IsSetRelevantForSlot(CharacterRelicProfile profile, string setName, string slotKey)
    {
        return true;
    }

    private static RelicWeightsProfile CreateWeightsProfile(
        string characterKey,
        string setName,
        string slotKey,
        CharacterRelicProfile profile,
        RelicSlotWeights slotWeights)
    {
        // TODO: Improve later
        var preferredSet1 = profile.Set1Candidates.FirstOrDefault();
        var preferredSet2 = profile.Set2Candidates.FirstOrDefault();
        var prefferedPlanar = profile.PlanarSets.FirstOrDefault();

        return new RelicWeightsProfile
        {
            CharacterKey = characterKey,
            SetName = setName,
            SlotKey = slotKey,
            DesiredMainStat = slotWeights.MainStat,
            SubstatWeights = new Dictionary<string, double>(slotWeights.SubstatWeights, StringComparer.OrdinalIgnoreCase),
            PreferredSet1 = preferredSet1,
            PreferredSet2 = preferredSet2,
            PreferredPlanar = prefferedPlanar
        };
    }
}

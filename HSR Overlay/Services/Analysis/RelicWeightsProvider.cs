using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HSR_Overlay.Services.Analysis;

public sealed class RelicWeightsProvider : IRelicWeightsProvider
{
    // key: character name
    private readonly Dictionary<string, CharacterRelicProfile> _characters;

    public RelicWeightsProvider(Dictionary<string, CharacterRelicProfile> characters)
    {
        _characters = characters;
    }

    // returns each relevant weight profile for all characters who have slot weights for this slot and consider this setName relevant for that slot.
    // This means RelicWeightsProfile only stores weights for slot, and 
    public IReadOnlyList<RelicWeightsProfile> GetProfiles(
        string relicSetName,
        RelicSlot slot
    )
    {
        var results = new List<RelicWeightsProfile>();

        foreach (var pair in _characters)
        {
            var characterKey = pair.Key;
            var profile = pair.Value;

            if (!profile.Slots.TryGetValue(slot, out var slotWeights))
                    continue;

            if (!IsSetRelevantForSlot(profile, relicSetName, slot))
                continue;

            results.Add(CreateWeightsProfile(
                characterKey,
                relicSetName,
                slot,
                profile,
                slotWeights));
        }

        return results;
    }

    private bool IsSetRelevantForSlot(CharacterRelicProfile profile, string setName, RelicSlot slotKey)
    {
        if (profile.Slots.Count == 0)
            return false;

        bool isCavernSlot = slotKey is RelicSlot.Head or RelicSlot.Hands or RelicSlot.Body or RelicSlot.Feet;
        bool isPlanarSlot = slotKey is RelicSlot.Sphere or RelicSlot.Rope;

        // Cavern relic relevance
        if (isCavernSlot)
        {
            if (setName == profile.Set1Candidate || setName == profile.Set2Candidate)
                return true;
            return false;
        }

        // Planar relic relevance
        if (isPlanarSlot)
        {
            if (setName == profile.PlanarSet)
                return true;
            return false;
        }

        return false;
    }

    private static RelicWeightsProfile CreateWeightsProfile(
        string characterKey,
        string setName,
        RelicSlot slotKey,
        CharacterRelicProfile profile,
        RelicSlotWeights slotWeights)
    {
        // TODO: Improve later
        var preferredSet1 = profile.Set1Candidate;
        var preferredSet2 = profile.Set2Candidate;
        var prefferedPlanar = profile.PlanarSet;

        return new RelicWeightsProfile
        {
            CharacterKey = characterKey,
            SetName = setName,
            SlotKey = slotKey,
            DesiredMainStat = slotWeights.MainStat,
            SubstatWeights = new Dictionary<Stat, double>(slotWeights.SubstatWeights),
            PreferredSet1 = preferredSet1,
            PreferredSet2 = preferredSet2,
            PreferredPlanar = prefferedPlanar
        };
    }
}

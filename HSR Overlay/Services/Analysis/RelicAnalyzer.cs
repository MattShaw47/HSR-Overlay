using HSR_Overlay.Services.State;

namespace HSR_Overlay.Services.Analysis;

public static class RelicAnalyzer
{
    public static RelicEvaluation Analyze(
        ParsedRelic relic,
        string characterKey,
        IRelicWeightsProvider weights,
        RelicStatTables tables,
        IRelicInventory inventory,
        int trials = 10_000)
    {
        IReadOnlyList<RelicWeightsProfile> weightList = weights.GetProfiles(relic.Set, relic.Slot);

        RelicEvaluation evaluations = new RelicEvaluation
        {
            Relic = relic
        };

        if (string.IsNullOrWhiteSpace(characterKey))
            return evaluations;

        var equipped = inventory.GetEquipped(characterKey, relic.Slot);
        if (equipped is null)
            return evaluations;

        foreach (var weightProfile in weightList.Where(w => w.CharacterKey == characterKey))
        {
            var probability = RelicUpgradeSimulator.ProbabilityBeatsReference(
                candidate: relic,
                candidateLevel: relic.Level,
                referenceRelic: equipped,
                profile: weightProfile,
                tables: tables,
                trials: trials);

            evaluations.ImprovementChances[weightProfile.CharacterKey] = probability;
        }

        return evaluations;
    }
}

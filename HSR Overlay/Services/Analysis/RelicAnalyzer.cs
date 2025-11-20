using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.UI.Input;

namespace HSR_Overlay.Services.Analysis;

public static class RelicAnalyzer
{
    // add param -> relic inventory obj.
    public static RelicEvaluation Analyze(ParsedRelic relic, IRelicWeightsProvider weights, RelicStatTables tables)
    {
        IReadOnlyList<RelicWeightsProfile> weightList = weights.GetProfiles(relic.Set, relic.Slot);

        RelicEvaluation evaluations = new RelicEvaluation
        {
            Relic = relic
        };


        foreach (var weightProfile in weightList)
        {
            // grab comparison relic from relic inventory according to character and slot
            // something like
            // ParsedRelic comparisonRelic = inventory.getCurrentRelic(weightProfile.CharacterKey, weightProfile.SlotKey);
            var probability = RelicUpgradeSimulator.ProbabilityBeatsReference(
                candidate: relic,
                candidateLevel: 0,
                referenceRelic: new ParsedRelic(),
                profile: weightProfile,
                tables: tables,
                trials: 5_000);

            evaluations.ImprovementChances[weightProfile.CharacterKey] = probability;
        }

        // send to text parser
        // send to relic object creator
        // send to evaluation model with context
        // actual analysis isnt finished yet, but thats just stats work and iterating through each set of weights and making the list of RelicEvaluation objects so I'm not worried about that.
        return evaluations;
    }
}

//public sealed record SubstatRollInfo(
//    double Low,
//    double Med,
//    double High
//)
//{
//    public double LowMultiplier => Low / Med;
//    public double MedMultiplier => High / Med;
//}
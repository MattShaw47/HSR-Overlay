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
    public static List<RelicEvaluation> Analyze(ParsedRelic relic, IRelicWeightsProvider weights, RelicPieceDatabase pieceDb, ParsedRelic comparisonPiece)
    {
        IReadOnlyList<IRelicWeightsProvider> weightList = weights.GetProfile(relic.Set, relic.Slot);

        List<RelicEvaluation> evaluations = [];

        foreach (var weightProvider in weightList)
        {
            evaluations.Add(CompareRelic(relic, comparisonPiece, weightProvider));
        }

        // send to text parser
        // send to relic object creator
        // send to evaluation model with context
        // actual analysis isnt finished yet, but thats just stats work and iterating through each set of weights and making the list of RelicEvaluation objects so I'm not worried about that.
        return evaluations;
    }

    private static RelicEvaluation CompareRelic(ParsedRelic newRelic, ParsedRelic comparisonPiece, IRelicWeightsProvider weights)
    {
        // not implemented yet
        return new RelicEvaluation
        {
            Relic = newRelic,
            ImprovementChances = new Dictionary<string, double>()
        };
    }
}
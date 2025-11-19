using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HSR_Overlay.Services.Analysis;

public interface IRelicWeightsProvider
{
    IReadOnlyList<IRelicWeightsProvider> GetProfile(
        string relicSetName,
        string slotKey
        );
}

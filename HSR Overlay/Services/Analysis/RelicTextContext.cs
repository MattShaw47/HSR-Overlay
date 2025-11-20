using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HSR_Overlay.Services.Analysis;

internal class RelicTextContext(RelicTextSource source)
{
    public RelicTextSource Source { get; init; } = source;
}

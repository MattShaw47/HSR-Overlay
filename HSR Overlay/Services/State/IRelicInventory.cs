using HSR_Overlay.Services.Analysis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HSR_Overlay.Services.State;

public interface IRelicInventory
{
    ParsedRelic? GetEquipped(string characterKey, RelicSlot slot);

    void setEquipped(string characterKey, RelicSlot slot, ParsedRelic relic);

    void Load();

    void Save();
}

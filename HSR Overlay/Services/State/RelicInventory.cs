using HSR_Overlay.Services.Analysis;
using HSR_Overlay.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace HSR_Overlay.Services.State;

internal class RelicInventory : IRelicInventory
{
    private readonly Dictionary<(string characterKey, RelicSlot slot), ParsedRelic> _equipped = new();

    private readonly string _filePath;
    private readonly JsonSerializerOptions _jsonOptions;

    public RelicInventory(string? filePath = null)
    {
        var baseDir = AppContext.BaseDirectory;
        var dataDir = Path.Combine(baseDir, "data");

        if (!Directory.Exists(dataDir))
            Directory.CreateDirectory(dataDir);

        _filePath = filePath ?? Path.Combine(dataDir, "RelicInventory.json");

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        _jsonOptions.Converters.Add(new JsonStringEnumConverter());

        Load();
    }

    public ParsedRelic? GetEquipped(string characterKey, RelicSlot slot)
    {
        return _equipped.TryGetValue((characterKey, slot), out var relic) ? relic : null;
    }

    public void setEquipped(string characterKey, RelicSlot slot, ParsedRelic relic)
    {
        _equipped[(characterKey, slot)] = relic;
    }
    
    public void Load()
    {
        _equipped.Clear();

        if (!File.Exists(_filePath))
            return;

        try
        {
            var json = File.ReadAllText(_filePath);
            var data = JsonSerializer.Deserialize<RelicInventoryData>(json, _jsonOptions);
            if (data == null) return;

            foreach (var entry in data.Equipped)
            {
                if (string.IsNullOrWhiteSpace(entry.CharacterKey))
                    continue;

                _equipped[(entry.CharacterKey, entry.Relic.Slot)] = entry.Relic;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Inventory", $"Failed to load relic inventory: {ex}");
        }
    }

    public void Save()
    {
        var data = new RelicInventoryData
        {
            Equipped = _equipped
                .Select(kvp => new EquippedRelic
                {
                    CharacterKey = kvp.Key.characterKey,
                    Relic = kvp.Value
                })
                .ToList()
        };

        var json = JsonSerializer.Serialize(data, _jsonOptions);
        File.WriteAllText(_filePath, json);
    }

}

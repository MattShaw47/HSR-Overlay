using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace HSR_Overlay.Services.Analysis;

public sealed class SubstatRollDefinition
{
    public Stat Stat { get; init; }
    public double Low { get; init; }
    public double Med { get; init; }
    public double High { get; init; }
    public int AppearanceWeight { get; init; }
}

public sealed class MainStatDefinition
{
    public Stat Stat { get; init; }
    public double MaxValue { get; init; }

    // If the main stat exists as a substat, link it here.
    public Stat? EquivalentSubstat { get; init; }

    // If it doesn't, force an arbitrary equivalence like 10 rolls.
    public double? EquivalentRolls { get; init; }
}

public sealed class RelicStatConfig
{
    public List<SubstatRollDefinition> Substats { get; init; } = new();
    public List<MainStatDefinition> MainStats { get; init; } = new();

    public static RelicStatConfig Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Relic stat config JSON not found", path);

        var json = File.ReadAllText(path);

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        var config = JsonSerializer.Deserialize<RelicStatConfig>(json, options);
        if (config is null)
            throw new InvalidOperationException("Failed to deserialize relic stat config.");

        return config;
    }
}

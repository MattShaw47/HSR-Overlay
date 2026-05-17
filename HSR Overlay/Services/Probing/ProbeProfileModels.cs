using System.Text.Json;

namespace HSR_Overlay.Services.Probing;

internal sealed class ProbeProfileSet
{
    public double DefaultRequiredHitRatio { get; set; } = 1.0;
    public int DefaultHysteresis { get; set; } = 1;
    public Dictionary<string, ProbeProfile> Probes { get; set; } = new();

    public static ProbeProfileSet Load(string path)
    {
        var json = File.ReadAllText(path);
        var loaded = JsonSerializer.Deserialize<ProbeProfileSet>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (loaded is null)
            throw new InvalidOperationException($"Failed to deserialize probe profile file: {path}");

        return loaded;
    }
}

internal sealed class ProbeProfile
{
    public double? RequiredHitRatio { get; set; }
    public int? Hysteresis { get; set; }
    public int? MinIntervalMs { get; set; }
    public List<ProbeSentinelProfile> Sentinels { get; set; } = new();
}

internal sealed class ProbeSentinelProfile
{
    public double U { get; set; }
    public double V { get; set; }
    public uint ExpectedArgb { get; set; }
    public int Tolerance { get; set; }
}

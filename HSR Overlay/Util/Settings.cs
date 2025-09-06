using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace HSR_Overlay.Util;

public sealed class Settings : INotifyPropertyChanged
{
    private static readonly string Dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config");
    private static readonly string PathFile = System.IO.Path.Combine(Dir, "overlay.settings.json");

    // ---- Example options (add more later) ----
    private bool _enableRelicPopup = true;
    public bool EnableRelicPopup
    {
        get => _enableRelicPopup;
        set { if (_enableRelicPopup == value) return; _enableRelicPopup = value; OnChanged(nameof(EnableRelicPopup)); }
    }

    private int _captureIntervalMs = 333;
    public int CaptureIntervalMs
    {
        get => _captureIntervalMs;
        set { if (_captureIntervalMs == value) return; _captureIntervalMs = Math.Max(50, value); OnChanged(nameof(CaptureIntervalMs)); }
    }

    private LogLevel _logLevel = Microsoft.Extensions.Logging.LogLevel.Debug; // Trace|Debug|Info|Warn|Error
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public LogLevel LogLevel
    {
        get => _logLevel;
        set { if (_logLevel == value) return; _logLevel = value; OnChanged(nameof(LogLevel)); }
    }

    // ------------------------------------------

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    // Singleton-ish current settings
    public static Settings Current { get; private set; } = new();

    public static void Load()
    {
        try
        {
            Log.Info("Settings", "Attempting loading save file");
            Directory.CreateDirectory(Dir);
            if (!File.Exists(PathFile)) { Save(); return; }
            var json = File.ReadAllText(PathFile);
            var loaded = JsonSerializer.Deserialize<Settings>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (loaded != null) Current = loaded;
        }
        catch { Log.Error("Settings", $"Failed loading from {Dir}"); }
    }

    public static void Save()
    {
        try
        {
            Log.Info("Settings", $"Attempting saving file to {Dir}");
            Directory.CreateDirectory(Dir);
            var json = JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(PathFile, json);
        }
        catch { Log.Error("Settings", "Failed saving."); }
    }
}

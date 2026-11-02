using System.Text.Json;
using System.Text.Json.Serialization;
using FreeScreenRecorder.Models;

namespace FreeScreenRecorder.Services;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _settingsPath;

    public SettingsStore()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FreeScreenRecorder");
        Directory.CreateDirectory(folder);
        _settingsPath = Path.Combine(folder, "settings.json");
    }

    public RecordingSettings Load()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                var loaded = JsonSerializer.Deserialize<RecordingSettings>(json, SerializerOptions);
                if (loaded is not null)
                {
                    return loaded;
                }
            }
        }
        catch
        {
            // Fall back to defaults on a corrupt settings file.
        }

        return new RecordingSettings();
    }

    public void Save(RecordingSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, SerializerOptions);
            File.WriteAllText(_settingsPath, json);
        }
        catch
        {
            // Settings are a convenience; a write failure must not stop recording.
        }
    }
}

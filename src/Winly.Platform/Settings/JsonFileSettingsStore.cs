using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;
using Winly.Core.Settings;

namespace Winly.Platform.Settings;

/// <summary>Persists <see cref="UserSettings"/> as JSON at <c>%LOCALAPPDATA%\Winly\settings.json</c> (FR-030).</summary>
public sealed class JsonFileSettingsStore(string filePath) : ISettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string DefaultFilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Winly", "settings.json");

    public string FilePath { get; } = filePath;

    public bool HasPersistedSettings => File.Exists(FilePath);

    public async Task<UserSettings> Load()
    {
        if (!File.Exists(FilePath))
        {
            return new UserSettings();
        }

        try
        {
            await using var stream = File.OpenRead(FilePath);
            return await JsonSerializer.DeserializeAsync<UserSettings>(stream, Options) ?? new UserSettings();
        }
        catch (JsonException exception)
        {
            Log.Warning(exception, "Settings file {Path} is unreadable; using defaults", FilePath);
            return new UserSettings();
        }
    }

    public async Task Save(UserSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        await using var stream = File.Create(FilePath);
        await JsonSerializer.SerializeAsync(stream, settings, Options);
    }
}

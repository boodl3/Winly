using Winly.Core.Settings;
using Winly.Platform.Settings;

namespace Winly.Core.Tests.Settings;

public class SettingsStoreTests
{
    private static string TemporarySettingsPath() =>
        Path.Combine(Path.GetTempPath(), "winly-tests", Guid.NewGuid().ToString("N"), "settings.json");

    [Fact]
    public async Task LoadReturnsDefaultsWhenNothingHasBeenSaved()
    {
        var store = new JsonFileSettingsStore(TemporarySettingsPath());

        Assert.False(store.HasPersistedSettings);
        Assert.Equal(new UserSettings(), await store.Load());
    }

    [Fact]
    public async Task DefaultsRoundTripThroughSaveAndLoad()
    {
        var store = new JsonFileSettingsStore(TemporarySettingsPath());

        await store.Save(new UserSettings());

        Assert.True(store.HasPersistedSettings);
        Assert.Equal(new UserSettings(), await store.Load());
    }

    [Fact]
    public async Task ChangesPersistAcrossLoadAndSave()
    {
        var store = new JsonFileSettingsStore(TemporarySettingsPath());
        var changed = new UserSettings { ActivationKeyCombination = "LCtrl+Space", ScreenCaptureEnabled = false };

        await store.Save(changed);
        var reloaded = await store.Load();
        await store.Save(reloaded with { RunAtLogin = true });

        Assert.Equal(changed with { RunAtLogin = true }, await store.Load());
    }

    [Fact]
    public async Task UnreadableFileFallsBackToDefaults()
    {
        var path = TemporarySettingsPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "{ not json");

        Assert.Equal(new UserSettings(), await new JsonFileSettingsStore(path).Load());
    }
}

using Winly.Core.Settings;
using Winly.Platform.Settings;

namespace Winly.Core.Tests.Settings;

public class SettingsPersistenceTests
{
    [Fact]
    public async Task EverySettingSurvivesASimulatedRestart()
    {
        var path = Path.Combine(Path.GetTempPath(), "winly-tests", Guid.NewGuid().ToString("N"), "settings.json");
        var changed = new UserSettings
        {
            ActivationKeyCombination = "F9",
            CompanionVisibilityMode = CompanionVisibilityMode.AlwaysVisible,
            ScreenCaptureEnabled = false,
            MicrophoneCaptureEnabled = false,
            RunAtLogin = true,
        };
        await new JsonFileSettingsStore(path).Save(changed);

        // A fresh store instance stands in for the next process launch.
        var afterRestart = new JsonFileSettingsStore(path);

        Assert.True(afterRestart.HasPersistedSettings);
        Assert.Equal(changed, await afterRestart.Load());
    }
}

namespace Winly.Core.Settings;

public interface ISettingsStore
{
    /// <summary>False until the first <see cref="Save"/>; used to detect first launch (FR-026).</summary>
    bool HasPersistedSettings { get; }

    /// <summary>Returns defaults when nothing has been saved yet.</summary>
    Task<UserSettings> Load();

    Task Save(UserSettings settings);
}

using Winly.Core.Abstractions;

namespace Winly.Platform.Input;

/// <summary>Resolves the key names used in <c>UserSettings.ActivationKeyCombination</c> to virtual-key codes.</summary>
internal static class VirtualKeyNames
{
    private static readonly Dictionary<string, uint> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["LWin"] = 0x5B,
        ["RWin"] = 0x5C,
        ["LAlt"] = 0xA4,
        ["RAlt"] = 0xA5,
        ["LCtrl"] = 0xA2,
        ["RCtrl"] = 0xA3,
        ["LShift"] = 0xA0,
        ["RShift"] = 0xA1,
        ["Space"] = 0x20,
        ["Tab"] = 0x09,
        ["CapsLock"] = 0x14,
        ["Escape"] = 0x1B,
        ["Insert"] = 0x2D,
        ["Pause"] = 0x13,
        ["ScrollLock"] = 0x91,
    };

    public static uint Resolve(string keyName)
    {
        if (NamedKeys.TryGetValue(keyName, out var code))
        {
            return code;
        }

        if (keyName.Length == 1 && char.IsAsciiLetterOrDigit(keyName[0]))
        {
            return char.ToUpperInvariant(keyName[0]);
        }

        if (keyName.Length is 2 or 3 && keyName[0] is 'F' or 'f'
            && int.TryParse(keyName.AsSpan(1), out var functionKeyNumber) && functionKeyNumber is >= 1 and <= 24)
        {
            return (uint)(0x6F + functionKeyNumber);
        }

        throw new ActivationKeyUnavailableException($"Unknown key name '{keyName}'.");
    }
}

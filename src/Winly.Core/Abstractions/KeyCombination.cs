namespace Winly.Core.Abstractions;

/// <summary>Keys that must be held together, e.g. <c>LWin+LAlt</c>. Names are resolved by the platform layer.</summary>
public sealed record KeyCombination(IReadOnlyList<string> KeyNames)
{
    public static KeyCombination Parse(string descriptor)
    {
        var names = descriptor.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (names.Length == 0)
        {
            throw new ArgumentException("A key combination needs at least one key.", nameof(descriptor));
        }

        return new KeyCombination(names);
    }

    public override string ToString() => string.Join('+', KeyNames);
}

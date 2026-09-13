namespace Winly.Core.Providers;

/// <summary>The backend answered with its <c>{ "error": … }</c> body; <see cref="Reason"/> is that value.</summary>
public sealed class ProviderFailureException(string reason) : Exception($"Backend reported '{reason}'.")
{
    public string Reason { get; } = reason;
}

public sealed class ProxyNotConfiguredException() : Exception("No backend base address is configured.");

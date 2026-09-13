using Winly.Core.Providers;

namespace Winly.Providers;

/// <summary>Where the backend lives. A base address only — never a key (Constitution Principle I).</summary>
public sealed record ProxyEndpointOptions(Uri? BaseAddress)
{
    public const string EnvironmentVariableName = "WINLY_PROXY_BASE_URL";

    public static ProxyEndpointOptions FromEnvironment()
    {
        var value = Environment.GetEnvironmentVariable(EnvironmentVariableName);

        // A process started from a shell, launcher or IDE that was already open when the variable
        // was first set inherits an environment without it. Reading the stored user value directly
        // is what keeps that from looking like "you never completed setup".
        if (string.IsNullOrWhiteSpace(value) && OperatingSystem.IsWindows())
        {
            value = Environment.GetEnvironmentVariable(EnvironmentVariableName, EnvironmentVariableTarget.User);
        }

        return new ProxyEndpointOptions(BaseAddress: Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null);
    }

    public bool IsConfigured => BaseAddress is not null;

    /// <exception cref="ProxyNotConfiguredException">No base address is set.</exception>
    public Uri Resolve(string route)
    {
        if (BaseAddress is null)
        {
            throw new ProxyNotConfiguredException();
        }

        var baseText = BaseAddress.AbsoluteUri.TrimEnd('/');
        return new Uri($"{baseText}/{route.TrimStart('/')}");
    }
}

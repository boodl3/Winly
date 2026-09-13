using Winly.Core.Providers;

namespace Winly.Providers;

/// <summary>
/// Where the backend lives, and the token that opens it. No third-party provider key is ever here
/// (Constitution Principle I) — <see cref="Token"/> gates Winly's own Worker and nothing else, so
/// a stranger who finds the URL cannot spend the provider credits sitting behind it.
/// </summary>
public sealed record ProxyEndpointOptions(Uri? BaseAddress, string? Token)
{
    public const string EnvironmentVariableName = "WINLY_PROXY_BASE_URL";
    public const string TokenVariableName = "WINLY_BACKEND_TOKEN";

    public static ProxyEndpointOptions FromEnvironment() => new(
        BaseAddress: Uri.TryCreate(Read(EnvironmentVariableName), UriKind.Absolute, out var uri) ? uri : null,
        Token: Read(TokenVariableName));

    private static string? Read(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);

        // A process started from a shell, launcher or IDE that was already open when the variable
        // was first set inherits an environment without it. Reading the stored user value directly
        // is what keeps that from looking like "you never completed setup".
        if (string.IsNullOrWhiteSpace(value) && OperatingSystem.IsWindows())
        {
            value = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User);
        }

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public bool IsConfigured => BaseAddress is not null;

    /// <summary>The backend refuses every request without this, so an unset token is a setup failure.</summary>
    public bool HasToken => Token is not null;

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

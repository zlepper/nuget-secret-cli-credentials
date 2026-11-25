using System.Collections.Concurrent;
using NuGet.Common;
using NuGetSecretCliCredentials.Config;
using NuGetSecretCliCredentials.Logging;
using NuGetSecretCliCredentials.Secrets;

namespace NuGetSecretCliCredentials.Credentials;

internal sealed class Credential(Secret username, Secret password)
{
    public Secret Username { get; } = username;
    public Secret Password { get; } = password;

    public override string ToString() => "Credential(***)";
}

internal sealed class CredentialResult
{
    private CredentialResult(Credential? credential, string message)
    {
        Credential = credential;
        Message = message;
    }

    /// <summary>The credential, or null when the plugin answers <c>NotFound</c>.</summary>
    public Credential? Credential { get; }

    /// <summary>A message for NuGet. Never contains a secret.</summary>
    public string Message { get; }

    public static CredentialResult Found(Credential credential, string message) => new(credential, message);

    public static CredentialResult NotFound(string message) => new(null, message);
}

/// <summary>
/// Resolves credentials for configured sources and caches them in memory for the lifetime of the plugin process.
/// Nothing is ever written anywhere.
/// </summary>
internal sealed class CredentialService(PluginConfig config, SecretResolver resolver, OpCli op, IPluginLog log)
{
    internal const string LockedMessage =
        "1Password is locked; run an interactive restore (`dotnet restore --interactive`) or unlock op";

    private readonly ConcurrentDictionary<SourceConfig, Credential> _cache = new();
    private readonly ConcurrentDictionary<SourceConfig, SemaphoreSlim> _locks = new();

    public PluginConfig Config => config;

    public async Task<CredentialResult> GetAsync(Uri uri, bool isRetry, bool isNonInteractive, CancellationToken cancellationToken)
    {
        if (SourceMatcher.Match(config.Sources, uri) is not { } source)
        {
            return CredentialResult.NotFound($"nuget-plugin-op serves no source for {uri}.");
        }

        // One resolution per source at a time, so concurrent requests during a restore share one 1Password prompt.
        var sourceLock = _locks.GetOrAdd(source, static _ => new SemaphoreSlim(1, 1));
        await sourceLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (isRetry)
            {
                log.Log(LogLevel.Verbose, $"Retry for {source}: dropping any cached credential and reading again.");
                _cache.TryRemove(source, out _);
            }
            else if (_cache.TryGetValue(source, out var cached))
            {
                return CredentialResult.Found(cached, $"Credentials for {source} from nuget-plugin-op (cached).");
            }

            if (isNonInteractive && config.NonInteractive == NonInteractiveMode.Skip && UsesOnePassword(source)
                && !await op.IsAuthorizedAsync(cancellationToken).ConfigureAwait(false))
            {
                log.Log(LogLevel.Warning, $"{LockedMessage} (source {source}).");
                return CredentialResult.NotFound(LockedMessage);
            }

            var username = await resolver.ResolveAsync(source.Username, cancellationToken).ConfigureAwait(false);
            if (username.Value is null)
            {
                return CredentialResult.NotFound(username.Failure!);
            }

            var password = await resolver.ResolveAsync(source.Password, cancellationToken).ConfigureAwait(false);
            if (password.Value is null)
            {
                return CredentialResult.NotFound(password.Failure!);
            }

            var credential = new Credential(username.Value, password.Value);
            _cache[source] = credential;
            log.Log(LogLevel.Verbose, $"Read credentials for {source}.");
            return CredentialResult.Found(credential, $"Credentials for {source} from nuget-plugin-op.");
        }
        finally
        {
            sourceLock.Release();
        }
    }

    private static bool UsesOnePassword(SourceConfig source) =>
        SecretReference.KindOf(source.Username) == SecretReferenceKind.OnePassword
        || SecretReference.KindOf(source.Password) == SecretReferenceKind.OnePassword;
}

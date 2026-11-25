using NuGet.Common;
using NuGetOpCredentialProvider.Config;
using NuGetOpCredentialProvider.Logging;

namespace NuGetOpCredentialProvider.Secrets;

/// <summary>Reads the GitHub CLI's own OAuth token (<c>gh auth token</c>) for a <c>gh://&lt;host&gt;</c> reference.</summary>
internal sealed class GhCli(PluginConfig config, IPluginLog log, string? pathVariable)
{
    private const string DefaultHost = "github.com";

    private static readonly string[] Fallbacks =
    [
        $"/etc/profiles/per-user/{Environment.UserName}/bin/gh",
        "/run/current-system/sw/bin/gh",
    ];

    public async Task<SecretResult> TokenAsync(string reference, CancellationToken cancellationToken)
    {
        var host = HostOf(reference);
        if (ToolLocator.Locate(config.GhPath, "gh", Fallbacks, pathVariable) is not { } gh)
        {
            return SecretResult.Fail(
                $"The GitHub CLI 'gh' was not found on PATH or at {string.Join(", ", Fallbacks)}; set 'ghPath' in the config.");
        }

        var result = await ProcessRunner.RunAsync(gh, ["auth", "token", "--hostname", host], config.Timeout, cancellationToken)
            .ConfigureAwait(false);

        var token = result.StandardOutput.Reveal().Trim();
        if (result.Succeeded && token.Length > 0)
        {
            return SecretResult.Ok(new Secret(token));
        }

        var reason = result switch
        {
            { StartError: { } startError } => startError,
            { TimedOut: true } => $"timed out after {config.Timeout.TotalSeconds:0} s",
            { Succeeded: true } => "gh returned an empty token",
            _ => $"gh is not logged in to {host}; run 'gh auth login --hostname {host} --scopes read:packages'",
        };

        log.Log(LogLevel.Warning, $"gh auth token for '{host}' failed (exit code {result.ExitCode?.ToString() ?? "none"}): {reason}. stderr: {result.StandardError}");
        return SecretResult.Fail($"Could not get a GitHub CLI token for '{host}': {reason}.");
    }

    internal static string HostOf(string reference)
    {
        var host = reference[SecretReference.GitHubCliScheme.Length..].Trim('/');
        var slash = host.IndexOf('/');
        if (slash >= 0)
        {
            host = host[..slash];
        }

        return host.Length == 0 ? DefaultHost : host;
    }
}

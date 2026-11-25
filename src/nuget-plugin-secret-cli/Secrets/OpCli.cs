using NuGet.Common;
using NuGetSecretCliCredentials.Config;
using NuGetSecretCliCredentials.Logging;

namespace NuGetSecretCliCredentials.Secrets;

/// <summary>Reads secrets with the 1Password CLI (<c>op read</c>).</summary>
internal sealed class OpCli(PluginConfig config, IPluginLog log, string? pathVariable)
{
    // On NixOS the desktop-app integration only works through the setgid wrapper.
    private static readonly string[] Fallbacks = ["/run/wrappers/bin/op"];
    private static readonly TimeSpan WhoAmITimeout = TimeSpan.FromSeconds(15);

    public async Task<SecretResult> ReadAsync(string reference, CancellationToken cancellationToken)
    {
        if (Locate() is not { } op)
        {
            return SecretResult.Fail(NotFoundMessage());
        }

        var result = await ProcessRunner.RunAsync(op, ["read", "--no-newline", .. AccountArguments(), reference], config.Timeout, cancellationToken)
            .ConfigureAwait(false);

        if (result.Succeeded && !result.StandardOutput.IsEmpty)
        {
            return SecretResult.Ok(result.StandardOutput);
        }

        var reason = result switch
        {
            { StartError: { } startError } => startError,
            { TimedOut: true } => $"timed out after {config.Timeout.TotalSeconds:0} s; was the 1Password approval prompt answered?",
            { Succeeded: true } => "op returned an empty value",
            _ => Classify(result.StandardError),
        };

        log.Log(LogLevel.Warning, $"op read '{reference}' failed (exit code {result.ExitCode?.ToString() ?? "none"}): {reason}. stderr: {result.StandardError}");
        return SecretResult.Fail($"Could not read '{reference}' from 1Password: {reason}.");
    }

    /// <summary>
    /// Checks whether <c>op</c> can read without prompting, using <c>op whoami</c>, which reports the session state.
    /// </summary>
    public async Task<bool> IsAuthorizedAsync(CancellationToken cancellationToken)
    {
        if (Locate() is not { } op)
        {
            log.Log(LogLevel.Warning, NotFoundMessage());
            return false;
        }

        var result = await ProcessRunner.RunAsync(op, ["whoami", .. AccountArguments()], WhoAmITimeout, cancellationToken)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            log.Log(LogLevel.Verbose, $"op whoami reports no authorized session (exit code {result.ExitCode?.ToString() ?? "none"}): {result.StartError ?? result.StandardError}");
        }

        return result.Succeeded;
    }

    private string? Locate() => ToolLocator.Locate(config.OpPath, "op", Fallbacks, pathVariable);

    private string NotFoundMessage() =>
        $"The 1Password CLI 'op' was not found on PATH or at {string.Join(", ", Fallbacks)}; set 'opPath' in the config";

    private IEnumerable<string> AccountArguments() =>
        config.Account is { } account ? ["--account", account] : [];

    private static string Classify(string standardError)
    {
        if (ContainsAny(standardError, "not currently signed in", "not signed in", "no accounts configured", "sign in"))
        {
            return "op is not signed in";
        }

        if (ContainsAny(standardError, "dismissed", "denied", "cancelled", "canceled", "authorization prompt"))
        {
            return "the 1Password approval prompt was declined";
        }

        if (ContainsAny(standardError, "isn't an item", "isn't a vault", "could not find", "no item", "not found"))
        {
            return "the item or field does not exist";
        }

        return "op failed";
    }

    private static bool ContainsAny(string text, params string[] needles) =>
        needles.Any(needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase));
}

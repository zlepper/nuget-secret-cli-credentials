using NuGet.Common;
using NuGet.Protocol.Plugins;
using NuGetSecretCliCredentials.Config;
using NuGetSecretCliCredentials.Handlers;
using NuGetSecretCliCredentials.Logging;

namespace NuGetSecretCliCredentials;

internal static class Program
{
    private const string Usage =
        """
        nuget-plugin-op: a NuGet credential provider that reads feed credentials with the 1Password CLI (op)
        or the GitHub CLI (gh).

        NuGet starts this program with -Plugin and talks to it over stdin/stdout; it is not meant to be run by hand.
        Configuration: $XDG_CONFIG_HOME/nuget-secret-cli-credentials/config.json (default ~/.config/...).
        """;


    public static async Task<int> Main(string[] args)
    {
        if (!args.Any(arg => string.Equals(arg, "-Plugin", StringComparison.OrdinalIgnoreCase)))
        {
            await Console.Error.WriteLineAsync(Usage);
            return 1;
        }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        var log = new PluginLog(Console.Error);
        var configLoad = ConfigLoader.Load(ConfigLoader.DefaultPath(Environment.GetEnvironmentVariable));
        var context = PluginContext.Create(configLoad, log, Environment.GetEnvironmentVariable("PATH"));

        var handlers = new RequestHandlers();
        handlers.TryAdd(MessageMethod.Initialize, new InitializeRequestHandler(log));
        handlers.TryAdd(MessageMethod.SetLogLevel, new SetLogLevelRequestHandler(log));
        handlers.TryAdd(MessageMethod.SetCredentials, new SetCredentialsRequestHandler(log));
        handlers.TryAdd(MessageMethod.GetOperationClaims, new GetOperationClaimsRequestHandler(context));
        handlers.TryAdd(
            MessageMethod.GetAuthenticationCredentials,
            new GetAuthenticationCredentialsRequestHandler(context, GetAuthenticationCredentialsRequestHandler.DefaultProgressInterval));

        try
        {
            using var plugin = await PluginFactory.CreateFromCurrentProcessAsync(handlers, ConnectionOptions.CreateDefault(), cancellation.Token);
            log.Attach(plugin.Connection);
            await WaitForCloseAsync(plugin, log, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        catch (Exception ex)
        {
            log.Log(LogLevel.Error, $"Plugin failed: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }

        return 0;
    }

    /// <summary>Completes when NuGet sends <c>Close</c> (or the connection faults), so the process exits promptly.</summary>
    private static async Task WaitForCloseAsync(IPlugin plugin, IPluginLog log, CancellationToken cancellationToken)
    {
        var closing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        plugin.BeforeClose += (_, _) => closing.TrySetResult();
        plugin.Closed += (_, _) => closing.TrySetResult();
        plugin.Connection.Faulted += (_, eventArgs) =>
        {
            log.Log(LogLevel.Error, $"Protocol connection faulted: {eventArgs.Exception.Message}");
            closing.TrySetResult();
        };

        await closing.Task.WaitAsync(cancellationToken);
    }
}

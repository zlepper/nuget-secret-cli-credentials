using NuGet.Common;
using NuGet.Protocol.Plugins;

namespace NuGetOpCredentialProvider.Logging;

/// <summary>
/// Until NuGet has sent <c>SetLogLevel</c>, writes warnings and errors to stderr; after that, forwards messages at or
/// above that level through the protocol's <c>Log</c> message. Never writes to stdout, which is the protocol channel.
/// </summary>
/// <remarks>NuGet doesn't redirect the plugin's stderr, so anything written there lands in the user's terminal.</remarks>
internal sealed class PluginLog(TextWriter standardError) : IPluginLog
{
    private const string Prefix = "[nuget-plugin-op] ";
    private readonly Lock _lock = new();
    private IConnection? _connection;
    private LogLevel? _minimumLevel;

    public void Attach(IConnection connection)
    {
        lock (_lock)
        {
            _connection = connection;
        }
    }

    public void SetLevel(LogLevel level)
    {
        lock (_lock)
        {
            _minimumLevel = level;
        }
    }

    public void Log(LogLevel level, string message)
    {
        IConnection? connection;
        LogLevel? minimumLevel;
        lock (_lock)
        {
            connection = _connection;
            minimumLevel = _minimumLevel;
        }

        if (connection is null || minimumLevel is null)
        {
            if (level < LogLevel.Warning)
            {
                return;
            }

            lock (_lock)
            {
                standardError.WriteLine($"{Prefix}{level}: {message}");
                standardError.Flush();
            }

            return;
        }

        if (level < minimumLevel)
        {
            return;
        }

        _ = connection
            .SendRequestAndReceiveResponseAsync<LogRequest, LogResponse>(
                MessageMethod.Log,
                new LogRequest(level, Prefix + message),
                CancellationToken.None)
            .ContinueWith(
                static (task, state) => ((TextWriter)state!).WriteLine($"{Prefix}Could not send log message: {task.Exception?.GetBaseException().Message}"),
                standardError,
                TaskContinuationOptions.OnlyOnFaulted);
    }
}

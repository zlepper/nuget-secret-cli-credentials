using System.Collections.Concurrent;
using NuGet.Common;
using NuGetOpCredentialProvider.Logging;

namespace NuGetOpCredentialProvider.Tests.Support;

internal sealed class RecordingLog : IPluginLog
{
    private readonly ConcurrentQueue<(LogLevel Level, string Message)> _entries = new();

    public IReadOnlyList<(LogLevel Level, string Message)> Entries => [.. _entries];

    public string AllText => string.Join('\n', _entries.Select(entry => entry.Message));

    public void Log(LogLevel level, string message) => _entries.Enqueue((level, message));
}

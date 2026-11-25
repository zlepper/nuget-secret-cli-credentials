using System.ComponentModel;
using System.Diagnostics;

namespace NuGetOpCredentialProvider.Secrets;

internal sealed class ProcessResult
{
    public required int? ExitCode { get; init; }

    /// <summary>The process's stdout. For the tools this plugin runs, that is the secret.</summary>
    public required Secret StandardOutput { get; init; }

    /// <summary>The process's stderr, with every line that contains the stdout value removed.</summary>
    public required string StandardError { get; init; }

    public required bool TimedOut { get; init; }

    /// <summary>Set when the process could not be started at all.</summary>
    public required string? StartError { get; init; }

    public bool Succeeded => StartError is null && !TimedOut && ExitCode == 0;
}

internal static class ProcessRunner
{
    /// <summary>
    /// Runs <paramref name="fileName"/> without a shell, with stdin closed, and captures stdout and stderr.
    /// The process tree is killed when <paramref name="timeout"/> elapses or <paramref name="cancellationToken"/> fires.
    /// </summary>
    public static async Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return new ProcessResult
            {
                ExitCode = null,
                StandardOutput = new Secret(string.Empty),
                StandardError = string.Empty,
                TimedOut = false,
                StartError = $"Could not start '{fileName}': {ex.Message}",
            };
        }

        process.StandardInput.Close();
        var stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            timedOut = !cancellationToken.IsCancellationRequested;
            Kill(process);
            cancellationToken.ThrowIfCancellationRequested();
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        return new ProcessResult
        {
            ExitCode = timedOut ? null : process.ExitCode,
            StandardOutput = new Secret(stdout),
            StandardError = Scrub(stderr, stdout),
            TimedOut = timedOut,
            StartError = null,
        };
    }

    /// <summary>Removes every line of <paramref name="text"/> that contains <paramref name="secretValue"/>.</summary>
    internal static string Scrub(string text, string secretValue)
    {
        var trimmedSecret = secretValue.Trim();
        if (trimmedSecret.Length == 0)
        {
            return text.Trim();
        }

        var lines = text
            .Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => !line.Contains(trimmedSecret, StringComparison.Ordinal));

        return string.Join('\n', lines).Trim();
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // The process exited on its own in the meantime.
        }
    }
}

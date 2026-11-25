namespace NuGetSecretCliCredentials.Tests.Support;

/// <summary>What a fake <c>op</c> or <c>gh</c> does when it is asked for a secret.</summary>
internal sealed record FakeBehavior
{
    public string Output { get; init; } = string.Empty;
    public string StandardError { get; init; } = string.Empty;
    public int ExitCode { get; init; }
    public int SleepSeconds { get; init; }

    /// <summary>Exit code of <c>op whoami</c>.</summary>
    public int WhoAmIExitCode { get; init; }

    public static FakeBehavior Prints(string value) => new() { Output = value };

    public static FakeBehavior Fails(string standardError, int exitCode = 1) => new() { StandardError = standardError, ExitCode = exitCode };
}

/// <summary>
/// A shell script standing in for <c>op</c> or <c>gh</c>. Every invocation appends its arguments, one per line and
/// followed by a <c>--</c> line, to <see cref="InvocationsFile"/>.
/// </summary>
internal sealed class FakeTool
{
    private FakeTool(string path, string invocationsFile)
    {
        Path = path;
        InvocationsFile = invocationsFile;
    }

    public string Path { get; }

    public string InvocationsFile { get; }

    public IReadOnlyList<IReadOnlyList<string>> Invocations
    {
        get
        {
            if (!File.Exists(InvocationsFile))
            {
                return [];
            }

            var invocations = new List<IReadOnlyList<string>>();
            var current = new List<string>();
            foreach (var line in File.ReadAllLines(InvocationsFile))
            {
                if (line == "--")
                {
                    invocations.Add(current);
                    current = [];
                }
                else
                {
                    current.Add(line);
                }
            }

            return invocations;
        }
    }

    public static FakeTool Create(TempDirectory directory, string name, FakeBehavior behavior)
    {
        var toolDirectory = directory.Combine("bin");
        Directory.CreateDirectory(toolDirectory);
        var path = System.IO.Path.Combine(toolDirectory, name);
        var invocationsFile = directory.Combine($"{name}.invocations");

        var script = $"""
            #!/bin/sh
            for arg in "$@"; do printf '%s\n' "$arg" >> {Quote(invocationsFile)}; done
            printf -- '--\n' >> {Quote(invocationsFile)}
            if [ "$1" = "whoami" ]; then
              exit {behavior.WhoAmIExitCode}
            fi
            sleep {behavior.SleepSeconds}
            printf '%s' {Quote(behavior.Output)}
            printf '%s' {Quote(behavior.StandardError)} >&2
            exit {behavior.ExitCode}
            """;

        File.WriteAllText(path, script.ReplaceLineEndings("\n") + "\n");
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The fake tools are POSIX shell scripts.");
        }

        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return new FakeTool(path, invocationsFile);
    }

    private static string Quote(string value) => "'" + value.Replace("'", "'\\''") + "'";
}

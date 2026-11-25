namespace NuGetOpCredentialProvider.Secrets;

internal static class ToolLocator
{
    /// <summary>
    /// Returns <paramref name="configuredPath"/> when set. Otherwise searches <c>PATH</c> for <paramref name="name"/>,
    /// then the <paramref name="fallbacks"/>, because a desktop-launched IDE may have a different <c>PATH</c>.
    /// Returns null when nothing executable is found.
    /// </summary>
    public static string? Locate(string? configuredPath, string name, IEnumerable<string> fallbacks, string? pathVariable)
    {
        if (configuredPath is not null)
        {
            return configuredPath;
        }

        var pathCandidates = (pathVariable ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory, name));

        return pathCandidates.Concat(fallbacks).FirstOrDefault(IsExecutableFile);
    }

    private static bool IsExecutableFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            if (OperatingSystem.IsWindows())
            {
                return true;
            }

            const UnixFileMode anyExecute = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            return (File.GetUnixFileMode(path) & anyExecute) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

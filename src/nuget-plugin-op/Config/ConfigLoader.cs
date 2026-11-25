using NuGetSecretCliCredentials.Secrets;
using System.Text.Json;

namespace NuGetSecretCliCredentials.Config;

internal sealed class ConfigLoadResult
{
    private ConfigLoadResult(PluginConfig? config, string? error, IReadOnlyList<string> warnings)
    {
        Config = config;
        Error = error;
        Warnings = warnings;
    }

    /// <summary>The loaded config, or null when loading failed and the plugin must claim nothing.</summary>
    public PluginConfig? Config { get; }

    public string? Error { get; }

    public IReadOnlyList<string> Warnings { get; }

    public static ConfigLoadResult Loaded(PluginConfig config, IReadOnlyList<string> warnings) => new(config, null, warnings);

    public static ConfigLoadResult Failed(string error) => new(null, error, []);
}

internal static class ConfigLoader
{
    private const string AppDirectoryName = "nuget-secret-cli-credentials";
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(120);

    /// <summary><c>$XDG_CONFIG_HOME/nuget-secret-cli-credentials/config.json</c>, falling back to <c>~/.config</c>.</summary>
    public static string DefaultPath(Func<string, string?> getEnvironmentVariable)
    {
        var xdgConfigHome = getEnvironmentVariable("XDG_CONFIG_HOME");
        var configHome = !string.IsNullOrEmpty(xdgConfigHome) && Path.IsPathRooted(xdgConfigHome)
            ? xdgConfigHome
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");

        return Path.Combine(configHome, AppDirectoryName, "config.json");
    }

    public static ConfigLoadResult Load(string path)
    {
        ConfigFile? file;
        try
        {
            using var stream = File.OpenRead(path);
            file = JsonSerializer.Deserialize(stream, ConfigJsonContext.Default.ConfigFile);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return ConfigLoadResult.Failed($"Config file '{path}' does not exist, so no sources are served.");
        }
        catch (JsonException ex)
        {
            return ConfigLoadResult.Failed($"Config file '{path}' is not valid: {ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ConfigLoadResult.Failed($"Config file '{path}' could not be read: {ex.Message}");
        }

        if (file is null)
        {
            return ConfigLoadResult.Failed($"Config file '{path}' is empty.");
        }

        return Validate(file, path);
    }

    private static ConfigLoadResult Validate(ConfigFile file, string path)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        NonInteractiveMode nonInteractive = NonInteractiveMode.Prompt;
        switch (file.NonInteractive)
        {
            case null or "prompt":
                break;
            case "skip":
                nonInteractive = NonInteractiveMode.Skip;
                break;
            default:
                errors.Add($"'nonInteractive' must be \"prompt\" or \"skip\", not \"{file.NonInteractive}\".");
                break;
        }

        if (file.TimeoutSeconds is <= 0)
        {
            errors.Add("'timeoutSeconds' must be greater than 0.");
        }

        var sources = new List<SourceConfig>();
        foreach (var (source, index) in (file.Sources ?? []).Select((s, i) => (s, i)))
        {
            var label = $"sources[{index}]";
            if (!Uri.TryCreate(source.UriPrefix, UriKind.Absolute, out var prefix))
            {
                errors.Add($"{label}: 'uriPrefix' must be an absolute URI.");
                continue;
            }

            label = $"{label} ({prefix})";
            var isHttps = prefix.Scheme == Uri.UriSchemeHttps;
            var isAllowedHttp = prefix.Scheme == Uri.UriSchemeHttp && file.AllowInsecureHttpForTesting;
            if (!isHttps && !isAllowedHttp)
            {
                errors.Add($"{label}: 'uriPrefix' must use https://. Credentials are never sent to {prefix.Scheme}:// sources.");
                continue;
            }

            if (string.IsNullOrEmpty(source.Username))
            {
                errors.Add($"{label}: 'username' is required.");
                continue;
            }

            if (string.IsNullOrEmpty(source.Password))
            {
                errors.Add($"{label}: 'password' is required.");
                continue;
            }

            if (!SecretReference.IsReference(source.Password))
            {
                warnings.Add($"{label}: 'password' is a literal value. Store it in 1Password and use an op:// reference, or use gh://<host>.");
            }

            sources.Add(new SourceConfig { UriPrefix = prefix, Username = source.Username, Password = source.Password });
        }

        if (file.AllowInsecureHttpForTesting)
        {
            warnings.Add("'allowInsecureHttpForTesting' is enabled. It exists for automated tests only; remove it.");
        }

        if (errors.Count > 0)
        {
            return ConfigLoadResult.Failed($"Config file '{path}' is not valid, so no sources are served: {string.Join(" ", errors)}");
        }

        var config = new PluginConfig
        {
            OpPath = NullIfEmpty(file.OpPath),
            GhPath = NullIfEmpty(file.GhPath),
            Account = NullIfEmpty(file.Account),
            NonInteractive = nonInteractive,
            Timeout = file.TimeoutSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : DefaultTimeout,
            Sources = sources,
        };

        return ConfigLoadResult.Loaded(config, warnings);
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}

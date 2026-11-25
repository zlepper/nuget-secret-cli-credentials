using System.Text.Json;
using System.Text.Json.Nodes;
using NuGetSecretCliCredentials.Config;

namespace NuGetSecretCliCredentials.Tests.Support;

internal static class TestConfig
{
    /// <summary>Writes <paramref name="config"/> where the plugin looks when <c>XDG_CONFIG_HOME</c> is <paramref name="configHome"/>.</summary>
    public static string Write(string configHome, JsonObject config)
    {
        var path = ConfigLoader.DefaultPath(name => name == "XDG_CONFIG_HOME" ? configHome : null);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, config.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return path;
    }

    public static PluginConfig Load(TempDirectory directory, JsonObject config)
    {
        var result = ConfigLoader.Load(Write(directory.Path, config));
        return result.Config ?? throw new InvalidOperationException(result.Error);
    }

    public static JsonObject Source(string uriPrefix, string username, string password) => new()
    {
        ["uriPrefix"] = uriPrefix,
        ["username"] = username,
        ["password"] = password,
    };
}

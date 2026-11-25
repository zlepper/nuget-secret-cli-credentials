using System.Text.Json.Serialization;

namespace NuGetOpCredentialProvider.Config;

/// <summary>The on-disk shape of <c>config.json</c>.</summary>
internal sealed class ConfigFile
{
    public string? OpPath { get; set; }
    public string? GhPath { get; set; }
    public string? Account { get; set; }
    public string? NonInteractive { get; set; }
    public int? TimeoutSeconds { get; set; }
    public List<SourceFile>? Sources { get; set; }

    /// <summary>Test-only: permits <c>http://</c> prefixes so tests can run a plain-HTTP feed.</summary>
    public bool AllowInsecureHttpForTesting { get; set; }
}

internal sealed class SourceFile
{
    public string? UriPrefix { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }

    public override string ToString() => UriPrefix ?? "(no uriPrefix)";
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(ConfigFile))]
internal sealed partial class ConfigJsonContext : JsonSerializerContext;

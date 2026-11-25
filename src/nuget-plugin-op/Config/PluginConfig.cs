namespace NuGetOpCredentialProvider.Config;

internal enum NonInteractiveMode
{
    /// <summary>Read secrets even when NuGet runs non-interactively; 1Password may show its approval dialog.</summary>
    Prompt,

    /// <summary>When NuGet runs non-interactively, only read from 1Password if it is already authorized.</summary>
    Skip,
}

internal sealed class PluginConfig
{
    public required string? OpPath { get; init; }
    public required string? GhPath { get; init; }
    public required string? Account { get; init; }
    public required NonInteractiveMode NonInteractive { get; init; }
    public required TimeSpan Timeout { get; init; }
    public required IReadOnlyList<SourceConfig> Sources { get; init; }
}

/// <summary>
/// A feed the plugin serves. <see cref="Username"/> and <see cref="Password"/> are references
/// (<c>op://…</c>, <c>gh://…</c>) or literals, so <see cref="ToString"/> deliberately leaves them out.
/// </summary>
internal sealed class SourceConfig
{
    public required Uri UriPrefix { get; init; }
    public required string Username { get; init; }
    public required string Password { get; init; }

    public override string ToString() => UriPrefix.ToString();
}

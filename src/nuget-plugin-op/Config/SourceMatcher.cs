namespace NuGetOpCredentialProvider.Config;

internal static class SourceMatcher
{
    /// <summary>
    /// Finds the source with the longest <see cref="SourceConfig.UriPrefix"/> that <paramref name="uri"/> starts with.
    /// Scheme and host compare case-insensitively, the port must be equal, and the path compares exactly.
    /// </summary>
    public static SourceConfig? Match(IReadOnlyList<SourceConfig> sources, Uri uri)
    {
        if (!uri.IsAbsoluteUri)
        {
            return null;
        }

        SourceConfig? best = null;
        foreach (var source in sources)
        {
            var prefix = source.UriPrefix;
            var matches = string.Equals(uri.Scheme, prefix.Scheme, StringComparison.OrdinalIgnoreCase)
                          && string.Equals(uri.Host, prefix.Host, StringComparison.OrdinalIgnoreCase)
                          && uri.Port == prefix.Port
                          && uri.AbsolutePath.StartsWith(prefix.AbsolutePath, StringComparison.Ordinal);

            if (matches && (best is null || prefix.AbsolutePath.Length > best.UriPrefix.AbsolutePath.Length))
            {
                best = source;
            }
        }

        return best;
    }

    public static SourceConfig? Match(IReadOnlyList<SourceConfig> sources, string? uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var parsed) ? Match(sources, parsed) : null;
}

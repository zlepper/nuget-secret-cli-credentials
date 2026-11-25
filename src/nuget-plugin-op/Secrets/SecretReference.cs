namespace NuGetOpCredentialProvider.Secrets;

internal enum SecretReferenceKind
{
    Literal,
    OnePassword,
    GitHubCli,
}

internal static class SecretReference
{
    public const string OnePasswordScheme = "op://";
    public const string GitHubCliScheme = "gh://";

    public static SecretReferenceKind KindOf(string value)
    {
        if (value.StartsWith(OnePasswordScheme, StringComparison.Ordinal))
        {
            return SecretReferenceKind.OnePassword;
        }

        if (value.StartsWith(GitHubCliScheme, StringComparison.Ordinal))
        {
            return SecretReferenceKind.GitHubCli;
        }

        return SecretReferenceKind.Literal;
    }

    public static bool IsReference(string value) => KindOf(value) != SecretReferenceKind.Literal;
}

namespace NuGetOpCredentialProvider.Secrets;

internal sealed class SecretResolver(OpCli op, GhCli gh)
{
    public Task<SecretResult> ResolveAsync(string value, CancellationToken cancellationToken) =>
        SecretReference.KindOf(value) switch
        {
            SecretReferenceKind.OnePassword => op.ReadAsync(value, cancellationToken),
            SecretReferenceKind.GitHubCli => gh.TokenAsync(value, cancellationToken),
            _ => Task.FromResult(SecretResult.Ok(new Secret(value))),
        };
}

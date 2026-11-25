namespace NuGetSecretCliCredentials.Secrets;

internal sealed class SecretResult
{
    private SecretResult(Secret? value, string? failure)
    {
        Value = value;
        Failure = failure;
    }

    public Secret? Value { get; }

    /// <summary>A message for the NuGet user saying why the secret could not be read. Never contains the secret.</summary>
    public string? Failure { get; }

    public static SecretResult Ok(Secret value) => new(value, null);

    public static SecretResult Fail(string failure) => new(null, failure);
}

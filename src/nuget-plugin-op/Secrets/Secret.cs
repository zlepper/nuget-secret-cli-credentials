namespace NuGetOpCredentialProvider.Secrets;

/// <summary>
/// Holds a secret value. <see cref="ToString"/> never reveals it, so interpolating or logging one by accident is
/// harmless; call <see cref="Reveal"/> only where the value has to leave the process.
/// </summary>
internal sealed class Secret(string value)
{
    public bool IsEmpty => value.Length == 0;

    public string Reveal() => value;

    public override string ToString() => "***";
}

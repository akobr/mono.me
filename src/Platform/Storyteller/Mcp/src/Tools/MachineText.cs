namespace _42.Platform.Storyteller.Mcp.Tools;

// Descriptions shared by the machine tools.
public static class MachineText
{
    public const string Id = "The machine id (Id from list_machines or create_machine).";

    // Who manages a machine (docs/Platform/Storyteller/authentication.md): its creator can use its credentials.
    public const string Roles =
        "Needs Contributor on the project; ContributorWithSecrets when the machine has SecretsRead or SecretsWrite; "
        + "Administrator when it has a management scope.";

    public const string CertificateReset =
        "A Certificate machine has no API key and cannot be reset: the machine renews its certificate itself over mTLS, "
        + "and to revoke the certificate, delete the machine and create a new one.";

    public const string FederationRules =
        "Federation rules, 1 to 10, as a JSON array " + FederationRuleArguments.Shape + ". Issuer names a trusted issuer of the "
        + "project; Audience is the token's aud (usually the machine's TokenEndpoint); Subject is the token's sub exactly, or "
        + "a prefix when it ends with :* or /*; Claims (optional) pins further claims to accepted string values.";
}

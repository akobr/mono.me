namespace _42.Platform.Storyteller.Mcp.Tools;

// The names of Sdk.MachineScope and Sdk.MachineCredentialKind as the tool descriptions list them. The lists are
// constants because attribute arguments must be; a test keeps them equal to the enums.
public static class MachineScopeNames
{
    public const string Runtime = "AnnotationRead, AnnotationWrite, ConfigurationRead, ConfigurationWrite, SecretsRead, SecretsWrite";

    public const string Management = "ConfigurationTransfer, SchemaWrite, TemplateWrite, AnnotationManage, KeyValueManage";

    public const string CredentialKinds = "ApiKey, Certificate, CertificateAndApiKey, ClientCredentials or Federated";

    public static List<Sdk.MachineScope> Parse(IReadOnlyCollection<string>? scopes, string parameter)
    {
        if (scopes is null || scopes.Count == 0)
        {
            throw new ToolInputException($"'{parameter}' needs at least one scope; the runtime scopes are {Runtime}.");
        }

        return scopes.Select(scope => EnumNames.Parse<Sdk.MachineScope>(scope, parameter)).Distinct().ToList();
    }
}

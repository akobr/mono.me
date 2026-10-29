using System.ComponentModel;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace _42.Platform.Storyteller.Mcp.Tools;

// The tools that return secret values, registered only with Mcp:SecretTools:Enabled and offered only while the API lets
// the MCP server's actor read secrets (SecretToolNames). None is read-only, so clients that confirm writes confirm these
// too, and each is a tool of its own, so clients that allow or deny per tool tell them apart.
[McpServerToolType]
public sealed class SecretTools(Sdk.IKeyValuesApiClient keyValues, Sdk.IConfigurationsApiClient configurations)
{
    public const string AskFirst =
        "Ask the person first: the value enters the conversation, the client's transcript and the model provider, and it "
        + "cannot be taken back.";

    [McpServerTool(Name = SecretToolNames.RevealKeyValue, Title = "Reveal a key/value pair", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(
        "Returns a pair with its value, a secret's value included, or one stored version of it. " + AskFirst + " Needs "
        + "ContributorWithSecrets on the project for a secret. The API audits secret reads: a reveal of the current value, "
        + "with or without its version number, is recorded as a Reveal event in the project's audit trail "
        + "(get_key_value_audit); a reveal by an older version number is not audited by the API. A Hyok value comes back as "
        + "the stored JWE document (ClientEncrypted: true), which only the holder of a recipient's private key decrypts "
        + "(sform story kv get <key> --reveal --private-key <pem>).")]
    public async Task<CallToolResult> RevealKeyValue(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        [Description(KeyValueText.Key)] string key,
        [Description(ToolText.View)] string view = ToolText.DefaultView,
        [Description(KeyValueText.Version + " Omit it for the current value.")] int? version = null,
        CancellationToken cancellationToken = default)
    {
        var name = ToolArguments.Required(key, nameof(key));

        if (version is not { } number)
        {
            return ToolResults.Json(KeyValueProtections.Describe(await keyValues.GetKeyValueAsync(organization, project, view, name, true, null, cancellationToken)));
        }

        // The API audits a reveal through the pair, not through its version route, so the current version is revealed
        // through the pair.
        var current = await KeyValueProtections.FindAsync(keyValues, organization, project, view, name, cancellationToken);

        if (current is not null && current.Version == number)
        {
            var pair = await keyValues.GetKeyValueAsync(organization, project, view, name, true, current.IsDeleted ? true : null, cancellationToken);

            // A write between the two reads made the asked version an older one, which only the version route returns.
            if (pair.Version == number)
            {
                return ToolResults.Json(KeyValueProtections.Describe(pair));
            }
        }

        return ToolResults.Json(KeyValueProtections.Describe(await keyValues.GetKeyValueVersionAsync(organization, project, view, name, number, true, cancellationToken)));
    }

    [McpServerTool(Name = SecretToolNames.GetResolvedConfiguration, Title = "Get a configuration with its bindings evaluated", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(
        "Returns the effective configuration of an annotation with its bindings evaluated: @key references and $binding "
        + "objects replaced by their values. With ContributorWithSecrets the secret bindings are filled in with the secret "
        + "values; without it they stay as stored. " + AskFirst + " The API audits secret reads: each secret of the values "
        + "store that a resolved read uses is counted per hour as a BindingRead event in the project's audit trail "
        + "(get_key_value_audit). A binding that cannot be evaluated is 422. Needs Reader on the project.")]
    public async Task<CallToolResult> GetResolvedConfiguration(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        [Description(ToolText.AnnotationKey)] string key,
        [Description(ToolText.View)] string view = ToolText.DefaultView,
        CancellationToken cancellationToken = default)
    {
        var annotationKey = AnnotationKeys.Parse(key).ToString();
        return ToolResults.Json(await configurations.GetResolvedConfigurationAsync(organization, project, view, annotationKey, null, cancellationToken));
    }
}

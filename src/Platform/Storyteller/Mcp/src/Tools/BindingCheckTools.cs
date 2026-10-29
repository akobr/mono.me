using System.ComponentModel;

using _42.Platform.Storyteller.Mcp.Understanding;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Mcp.Tools;

// Registered only with Mcp:SecretTools:Enabled: the check runs a resolved read, which fills in secrets for a caller with
// secret rights and records each secret it reads. The result says per binding whether the server replaced it, and never
// carries a value.
[McpServerToolType]
public sealed class BindingCheckTools(Sdk.IConfigurationsApiClient configurations)
{
    public const string Unknown = "unknown";

    private const string CheckDescription =
        "Checks whether the bindings of an annotation's effective configuration resolve: it reads the effective document (bindings as stored) and the "
        + "resolved one, and reports per binding pointer Resolved true (the server replaced the expression) or false (it stayed as stored: the source "
        + "declined, for example a missing pair, a secret without secret rights or an unknown source). It never returns a resolved value. When the "
        + "resolved read fails, every binding is Resolved \"unknown\" and only the status and error code are returned. Ask the person first: the "
        + "resolved read evaluates the bindings on the server, fills in secret bindings for a caller with secret rights (ContributorWithSecrets) and "
        + "records a BindingRead audit event for each secret it reads. Needs the Reader role on the project.";

    [McpServerTool(Name = SecretToolNames.CheckConfigurationBindings, Title = "Check configuration bindings", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(CheckDescription)]
    public async Task<CallToolResult> CheckConfigurationBindings(
        [Description(ToolText.Organization)] string organization,
        [Description(ToolText.Project)] string project,
        [Description(ToolText.AnnotationKey)] string key,
        [Description(ToolText.View)] string view = ToolText.DefaultView,
        CancellationToken cancellationToken = default)
    {
        var annotationKey = AnnotationKeys.Parse(key).ToString();
        var effective = await configurations.GetConfigurationAsync(organization, project, view, annotationKey, null, cancellationToken);
        var stored = ToolResults.ToToken(effective.Content) as JObject ?? new JObject();

        // a binding in the $context of an object binding is replaced together with that object
        var bindings = BindingScanner.Scan(stored).Where(binding => binding.EnvelopePointer is null).ToList();
        var result = new JObject { ["AnnotationKey"] = annotationKey };

        if (bindings.Count == 0)
        {
            result["Bindings"] = new JArray();
            result["Message"] = "The effective configuration holds no bindings; nothing was resolved.";
            return ToolResults.Json(result);
        }

        JObject? resolved = null;

        try
        {
            var read = await configurations.GetResolvedConfigurationAsync(organization, project, view, annotationKey, null, cancellationToken);
            resolved = ToolResults.ToToken(read.Content) as JObject ?? new JObject();
        }
        catch (Sdk.ApiException exception)
        {
            // the status and the error code only: a binding error message can quote an evaluated secret
            var failure = new JObject { ["Status"] = exception.StatusCode };

            if (ApiErrors.GetErrorCode(exception) is { Length: > 0 } errorCode)
            {
                failure["ErrorCode"] = errorCode;
            }

            result["ResolvedRead"] = failure;
        }

        result["Bindings"] = new JArray(bindings.Select(binding => new JObject
        {
            ["Pointer"] = binding.Pointer,
            ["Expression"] = binding.Expression,
            ["Form"] = binding.Parsed.Form.ToString(),
            ["Resolved"] = resolved is null
                ? new JValue(Unknown)
                : new JValue(!JToken.DeepEquals(JsonPointer.Find(stored, binding.Pointer), JsonPointer.Find(resolved, binding.Pointer))),
        }));

        return ToolResults.Json(result);
    }
}

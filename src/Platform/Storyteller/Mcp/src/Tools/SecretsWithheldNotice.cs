using ModelContextProtocol.Protocol;

using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Mcp.Tools;

// X-2S-Secrets-Withheld on a resolved read: the API answered 200 without the right to read secrets, although the person's
// role gives it. The header follows the caller's rights, not the document, which may have no secret binding at all. The
// reason and what helps are the first line of the text and the first properties of the structured content, so that the
// model does not take the expressions for the values.
public static class SecretsWithheldNotice
{
    public const string ReasonProperty = "SecretsWithheld";
    public const string PropertyName = "SecretsWithheldNotice";

    public static string For(string reason)
    {
        var unresolved = $"The API withheld the access to secrets ({reason}): any secret binding stays unresolved, its value the expression as stored";

        return reason switch
        {
            Sdk.SecretsWithheldReasons.StepUpRequired => unresolved
                + ", because the person signed in longer ago than the API allows for secret values. "
                + ApiErrors.SignOutFirst + " and read it again. The assistant cannot do this itself.",
            Sdk.SecretsWithheldReasons.ActorRestricted => unresolved
                + ", because the MCP server's actor may not reveal secrets (NoSecretReveal). Read it with sform or the admin UI.",
            Sdk.SecretsWithheldReasons.MtlsRequired => unresolved
                + ", because the project delivers secrets to machines only over mTLS.",
            _ => unresolved + ".",
        };
    }

    public static CallToolResult Result(object value, string? reason)
    {
        if (string.IsNullOrEmpty(reason))
        {
            return ToolResults.Json(value);
        }

        var notice = For(reason);
        var content = ToolResults.ToToken(value) as JObject ?? new JObject { ["Value"] = ToolResults.ToToken(value) };
        content.AddFirst(new JProperty(PropertyName, notice));
        content.AddFirst(new JProperty(ReasonProperty, reason));
        return ToolResults.Json(content, notice);
    }
}

using System.Text.Json;

using ModelContextProtocol.Protocol;

using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Mcp.Tools;

// Shared by the configuration and template writes: the arguments of replace and patch, and their results.
public static class DocumentWrites
{
    private static readonly HashSet<string> PatchOps = new(StringComparer.Ordinal) { "add", "remove", "replace", "move", "copy", "test" };

    private static readonly string[] Directives = ["$remove", "$patch"];

    // A replace sends the document as it should be stored. "$remove" and "$patch" are directives of a merge write: a
    // patch would store them as properties, and the first write of a document would apply them.
    public static JObject ReadDesired(JsonElement content, string parameter = "content")
    {
        var document = ToolArguments.ToObject(content, parameter);
        EnsureNoDirectives(document, $"'{parameter}'");
        return document;
    }

    public static void EnsureNoDirectives(JObject document, string source)
    {
        var directive = Directives.FirstOrDefault(name => document.Property(name, StringComparison.Ordinal) is not null);

        if (directive is not null)
        {
            throw new ToolInputException(
                $"{source} has a top-level \"{directive}\", a directive of a merge write; a replace stores the document as "
                + "given. Remove it, or use the merge write (set_configuration or set_template).");
        }
    }

    // The API applies RFC 6902 with JSON Pointers whose escapes it takes literally (a remove of "/a~1b" removes nothing),
    // so such pointers are refused rather than sent.
    public static JArray ReadPatch(JsonElement operations, string parameter = "operations")
    {
        var patch = ToolArguments.ToArray(operations, parameter);

        if (patch.Count == 0)
        {
            throw new ToolInputException($"'{parameter}' has no operations.");
        }

        for (var index = 0; index < patch.Count; index++)
        {
            if (patch[index] is not JObject operation
                || Text(operation, "op") is not { } op
                || !PatchOps.Contains(op)
                || Text(operation, "path") is null)
            {
                throw new ToolInputException(
                    $"Operation {index} of '{parameter}' is not an RFC 6902 operation: an object with \"op\" (add, remove, "
                    + "replace, move, copy or test) and a string \"path\".");
            }

            foreach (var name in new[] { "path", "from" })
            {
                if (Text(operation, name) is { } pointer && pointer.Contains('~', StringComparison.Ordinal))
                {
                    throw new ToolInputException(
                        $"Operation {index} of '{parameter}' has the pointer '{pointer}' with an escape (~0 or ~1), which "
                        + "the API applies as literal characters. Change such properties with the replace tool instead.");
                }
            }
        }

        return patch;
    }

    public static CallToolResult Written(object stored)
    {
        var result = (JObject)ToolResults.ToToken(stored).DeepClone();
        result["Changed"] = true;
        return ToolResults.Json(result);
    }

    public static CallToolResult Unchanged(JObject result)
    {
        result["Changed"] = false;
        return ToolResults.Json(result);
    }

    private static string? Text(JObject operation, string name)
    {
        return operation[name] is JValue { Type: JTokenType.String } value ? (string?)value : null;
    }
}

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Configuring;
using global::Json.Patch;
using global::Json.Pointer;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller;

public static class JsonExtensions
{
    private static readonly JsonLoadSettings LoadSettings = new()
    {
        CommentHandling = CommentHandling.Ignore,
        LineInfoHandling = LineInfoHandling.Ignore,
    };

    private static readonly JsonMergeSettings MergeSettings = new()
    {
        MergeArrayHandling = MergeArrayHandling.Union,
        MergeNullValueHandling = MergeNullValueHandling.Ignore,
    };

    public static async Task<JObject> ToJObjectAsync(this JsonObject @this)
    {
        using var memoryStream = new MemoryStream();
        await using var writer = new Utf8JsonWriter(memoryStream);

        @this.WriteTo(writer, JsonSerializerOptions.Default);
        await writer.FlushAsync();
        memoryStream.Seek(0, SeekOrigin.Begin);

        using var reader = new StreamReader(memoryStream);
        await using var jsonReader = new JsonTextReader(reader);
        var jObject = await JObject.LoadAsync(jsonReader, LoadSettings);
        return jObject;
    }

    public static async Task<JsonObject> ToJsonObjectAsync(this JObject @this)
    {
        using var memoryStream = new MemoryStream();
        await using var writer = new StreamWriter(memoryStream);
        await using var jsonWriter = new JsonTextWriter(writer);

        await @this.WriteToAsync(jsonWriter);
        await jsonWriter.FlushAsync();
        memoryStream.Seek(0, SeekOrigin.Begin);

        var jsonNode = await JsonNode.ParseAsync(memoryStream);

        if (jsonNode is not JsonObject jsonObject)
        {
            throw new InvalidOperationException("The JSON object is expected.");
        }

        return jsonObject;
    }

    public static void MergeInto(this JObject @this, JObject jObject)
    {
        @this.Merge(jObject, MergeSettings);
    }

    public static JObject RemoveRequested(this JObject @this)
    {
        const string removePropertyName = "$remove";
        var removeProp = @this.Property(removePropertyName);

        if (removeProp is null)
        {
            return @this;
        }

        if (removeProp.Value.Type == JTokenType.Array)
        {
            var removePaths = (JArray)removeProp.Value;

            foreach (var path in removePaths
                         .Where(prop => prop.Type == JTokenType.String)
                         .Select(prop => prop.ToString()))
            {
                var tokens = @this.SelectTokens(path).ToArray();

                foreach (var token in tokens)
                {
                    var container = token.Parent;
                    if (container is not null
                        && container.Type == JTokenType.Property)
                    {
                        container.Remove();
                    }
                    else
                    {
                        token.Remove();
                    }
                }
            }
        }

        @this.Remove(removePropertyName);
        return @this;
    }

    public static async Task<JObject> ApplyPatch(this JObject @this, JArray patchOperations)
    {
        var patch = ParsePatch(patchOperations);
        var jsonObject = await @this.ToJsonObjectAsync();
        var result = patch.Apply(jsonObject);

        if (!result.IsSuccess)
        {
            var failedOperation = result.Operation >= 0 && result.Operation < patch.Operations.Count
                ? patch.Operations[result.Operation]
                : null;
            var kind = failedOperation?.Op == OperationType.Test
                ? JsonPatchFailureKind.TestFailed
                : JsonPatchFailureKind.OperationFailed;
            throw new JsonPatchException($"JSON Patch operation {result.Operation} failed: {result.Error}", kind, result.Operation);
        }

        if (result.Result is not JsonObject patchedObject)
        {
            throw new JsonPatchException("JSON Patch result is not a JSON object.", JsonPatchFailureKind.Invalid);
        }

        return await patchedObject.ToJObjectAsync();
    }

    public static async Task<JObject> ApplyPatchRequested(this JObject @this)
    {
        const string patchPropertyName = "$patch";
        var patchProp = @this.Property(patchPropertyName);

        if (patchProp is null)
        {
            return @this;
        }

        if (patchProp.Value.Type != JTokenType.Array)
        {
            throw new JsonPatchException($"'{patchPropertyName}' must be an array.", JsonPatchFailureKind.Invalid);
        }

        var patchArray = (JArray)patchProp.Value;
        @this.Remove(patchPropertyName);
        return await @this.ApplyPatch(patchArray);
    }

    private static JsonPatch ParsePatch(JArray patchOperations)
    {
        var patchArrayJson = patchOperations.ToString(Formatting.None);

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<JsonPatch>(patchArrayJson)
                ?? throw new JsonPatchException("Invalid JSON Patch document.", JsonPatchFailureKind.Invalid);
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or PointerParseException)
        {
            // Unknown op, missing path or value, or a malformed pointer: the document itself is not a valid patch.
            throw new JsonPatchException($"Invalid JSON Patch document: {exception.Message}", JsonPatchFailureKind.Invalid);
        }
    }
}

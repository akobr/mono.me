using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
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
}

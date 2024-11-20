using System.IO;
using System.Text.Json;
using _42.Platform.Storyteller.Json;
using Newtonsoft.Json;

using JsonSerializer = Newtonsoft.Json.JsonSerializer;

namespace _42.Platform.Storyteller;

public static class StreamExtensions
{
    public static TItem? DeserializeNewtonsoft<TItem>(this Stream @this, JsonSerializerSettings? options = null)
        where TItem : class
    {
        var serializer = JsonSerializer.Create(options ?? JsonSerializationOptions.NewtonsoftOptions);
        using var tReader = new StreamReader(@this);
        using var jReader = new JsonTextReader(tReader);
        var item = serializer.Deserialize<TItem>(jReader);
        return item;
    }

    public static TItem? DeserializeSystemTextJson<TItem>(this Stream @this, JsonSerializerOptions? options = null)
    {
        var item = System.Text.Json.JsonSerializer.Deserialize<TItem>(@this, options ?? JsonSerializationOptions.SystemOptions);
        return item;
    }
}

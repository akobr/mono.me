using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Kiota.Abstractions.Serialization;

namespace ApiSdk;

public static class IParsableExtensions
{
    public static TOutput? ToTyped<TInput, TOutput>(this TInput source, JsonSerializerOptions? options = null)
        where TInput : IParsable
    {
        var json = KiotaJsonSerializer.SerializeAsString(source);
        return JsonSerializer.Deserialize<TOutput>(json, options);
    }

    public static async Task<T?> ToTypedAsync<T>(this IParsable source, JsonSerializerOptions? options = null)
    {
        var json = await KiotaJsonSerializer.SerializeAsStringAsync(source);
        return JsonSerializer.Deserialize<T>(json, options);
    }
}

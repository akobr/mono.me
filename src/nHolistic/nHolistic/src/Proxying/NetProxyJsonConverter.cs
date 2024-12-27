using System.Text.Json;
using System.Text.Json.Serialization;

namespace _42.nHolistic;

public class NetProxyJsonConverter<T>(ProxyFactory proxyFactory) : JsonConverter<T>
    where T : class
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var proxy = proxyFactory.CreateInterfaceProxy<T>();

        if (proxy == null)
        {
            throw new JsonException($"Unable to create proxy for type {typeof(T).Name}");
        }

        using var jsonDoc = JsonDocument.ParseValue(ref reader);
        foreach (var prop in jsonDoc.RootElement.EnumerateObject())
        {
            var propInfo = typeof(T).GetProperty(prop.Name);

            if (propInfo == null || !propInfo.CanWrite)
            {
                continue;
            }

            var value = JsonSerializer.Deserialize(prop.Value.GetRawText(), propInfo.PropertyType, options);
            propInfo.SetValue(proxy, value);
        }

        return proxy;
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        throw new NotImplementedException();
    }
}

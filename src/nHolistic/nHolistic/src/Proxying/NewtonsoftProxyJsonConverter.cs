using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _42.nHolistic;

public class NewtonsoftProxyJsonConverter(
    IProxyFactory factory,
    Type interfaceType)
    : JsonConverter
{
    public override bool CanConvert(Type objectType)
    {
        return interfaceType.IsAssignableFrom(objectType);
    }

    public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        var jsonObject = serializer.Deserialize<JObject>(reader);

        if (jsonObject is null)
        {
            return null;
        }

        var proxy = factory.CreateInterfaceProxy(interfaceType);

        if (proxy == null)
        {
            throw new JsonSerializationException($"Unable to create proxy for type '{interfaceType.FullName}'");
        }

        foreach (var property in jsonObject.Properties())
        {
            var propInfo = interfaceType.GetProperty(property.Name);

            if (propInfo is not null && propInfo.CanWrite)
            {
                var value = property.Value.ToObject(propInfo.PropertyType, serializer);
                propInfo.SetValue(proxy, value);
            }
        }

        return proxy;
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        serializer.Serialize(writer, value, interfaceType);
    }
}

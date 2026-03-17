using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _42.tHolistic;

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
            var propInfo = GetPropertyWithSetter(interfaceType, property.Name);

            if (propInfo is not null)
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

    private static PropertyInfo? GetPropertyWithSetter(Type type, string propertyName)
    {
        var propertyInfo = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);

        if (propertyInfo is not null && propertyInfo.CanWrite)
        {
            return propertyInfo;
        }

        foreach (var interfaceType in type.GetInterfaces())
        {
            propertyInfo = GetPropertyWithSetter(interfaceType, propertyName);

            if (propertyInfo is not null)
            {
                return propertyInfo;
            }
        }

        return null;
    }

}

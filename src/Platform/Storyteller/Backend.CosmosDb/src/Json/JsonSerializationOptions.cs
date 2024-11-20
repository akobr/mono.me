using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

using JsonConverter = Newtonsoft.Json.JsonConverter;

namespace _42.Platform.Storyteller.Json;

public static class JsonSerializationOptions
{
    public static readonly JsonSerializerOptions SystemOptions = new(JsonSerializerOptions.Default)
    {
        PropertyNamingPolicy = new NoChangeNamingPolicy(),
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
    };

    public static readonly JsonSerializerSettings NewtonsoftOptions = new()
    {
        ContractResolver = new DefaultContractResolver
        {
            NamingStrategy = new DefaultNamingStrategy(),
            IgnoreSerializableAttribute = true,
        },
        Converters = new List<JsonConverter> { new StringEnumConverter(new DefaultNamingStrategy(), false) },
        NullValueHandling = NullValueHandling.Ignore,
        ObjectCreationHandling = ObjectCreationHandling.Replace,
        ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor,
        MissingMemberHandling = MissingMemberHandling.Ignore,
        DateFormatHandling = DateFormatHandling.IsoDateFormat,
        DateTimeZoneHandling = DateTimeZoneHandling.RoundtripKind,
        Formatting = Formatting.None,
        DefaultValueHandling = DefaultValueHandling.Include,
        TypeNameHandling = TypeNameHandling.None,
    };
}

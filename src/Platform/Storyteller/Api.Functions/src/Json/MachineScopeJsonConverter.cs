using System.Text.Json;
using System.Text.Json.Serialization;

namespace _42.Platform.Storyteller.Api.Json;

// A machine scope in a body is one name, in any case, or an integer, also in a string. JsonStringEnumConverter reads
// names joined by commas as their values ORed, although MachineScope is no [Flags] enum: "AnnotationWrite,
// ConfigurationRead" is 1 | 2, ConfigurationWrite, a scope nobody asked for. Such a string is no scope here; every
// other value is read and written by JsonStringEnumConverter, so an undefined integer still reaches the handler.
public sealed class MachineScopeJsonConverter : JsonConverter<MachineScope>
{
    private static readonly JsonConverter<MachineScope> EnumConverter =
        (JsonConverter<MachineScope>)new JsonStringEnumConverter<MachineScope>().CreateConverter(typeof(MachineScope), JsonSerializerOptions.Default);

    public override MachineScope Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Without a message, as the enum converter fails a value: the serializer adds the path, $.Scopes[0].
        if (reader.TokenType == JsonTokenType.String && reader.GetString()!.Contains(','))
        {
            throw new JsonException();
        }

        return EnumConverter.Read(ref reader, typeToConvert, options);
    }

    public override void Write(Utf8JsonWriter writer, MachineScope value, JsonSerializerOptions options)
    {
        EnumConverter.Write(writer, value, options);
    }
}

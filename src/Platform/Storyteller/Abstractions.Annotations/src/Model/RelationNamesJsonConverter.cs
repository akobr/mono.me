using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace _42.Platform.Storyteller;

// The relation arrays of the models (ContextNames, ResponsibilityNames, UnitNames) as JSON arrays of strings.
// System.Text.Json creates no IReadOnlySet<T>: without this converter every body with one of them, even [], threw
// NotSupportedException (424-129). Null reads as an empty set, so the non-nullable property is never null. The names
// are ordinal, as the server compares them, so a duplicate collapses and the case is kept.
internal sealed class RelationNamesJsonConverter : JsonConverter<IReadOnlySet<string>>
{
    public override bool HandleNull => true;

    public override IReadOnlySet<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        if (reader.TokenType == JsonTokenType.Null)
        {
            return names;
        }

        // Without a message, as the serializer fails a value of the wrong type: it adds the path, $.ContextNames.
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException();
        }

        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException();
            }

            names.Add(reader.GetString()!);
        }

        return names;
    }

    public override void Write(Utf8JsonWriter writer, IReadOnlySet<string> value, JsonSerializerOptions options)
    {
        // With HandleNull, the serializer passes a null set here unless its options ignore nulls.
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartArray();

        foreach (var name in value)
        {
            writer.WriteStringValue(name);
        }

        writer.WriteEndArray();
    }
}

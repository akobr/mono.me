using System;
using System.IO;
using System.Text.Json;
using _42.Platform.Storyteller.Entities.Annotations;
using _42.Platform.Storyteller.Entities.Configurations;
using Newtonsoft.Json;

using JsonSerializer = System.Text.Json.JsonSerializer;

namespace _42.Platform.Storyteller.Annotating;

// Reads and writes the items of annotations. Annotations and audits are System.Text.Json documents;
// configurations are written by the configuration service with Newtonsoft and are read the same way.
public sealed class AnnotationItemSerializer
{
    private readonly JsonSerializerOptions _systemOptions;
    private readonly JsonSerializerSettings _newtonsoftSettings;

    public AnnotationItemSerializer(JsonSerializerOptions systemOptions, JsonSerializerSettings newtonsoftSettings)
    {
        _systemOptions = systemOptions;
        _newtonsoftSettings = newtonsoftSettings;
    }

    public JsonSerializerOptions SystemOptions => _systemOptions;

    public JsonSerializerSettings NewtonsoftSettings => _newtonsoftSettings;

    internal AnnotationEntity? ReadAnnotation(JsonElement document)
    {
        if (!document.TryGetProperty(nameof(AnnotationEntity.AnnotationType), out var typeProperty)
            || typeProperty.ValueKind != JsonValueKind.String
            || !Enum.TryParse<AnnotationType>(typeProperty.GetString(), out var annotationType))
        {
            return null;
        }

        return (AnnotationEntity?)document.Deserialize(CosmosTypeCodes.GetEntityType(annotationType), _systemOptions);
    }

    internal AnnotationAuditEntity? ReadAudit(JsonElement document)
    {
        return document.Deserialize<AnnotationAuditEntity>(_systemOptions);
    }

    internal ConfigurationEntity? ReadConfiguration(JsonElement document)
    {
        return JsonConvert.DeserializeObject<ConfigurationEntity>(document.GetRawText(), _newtonsoftSettings);
    }

    internal MemoryStream WriteAnnotation(AnnotationEntity entity)
    {
        var stream = new MemoryStream();
        JsonSerializer.Serialize(stream, entity, entity.GetType(), _systemOptions);
        stream.Seek(0, SeekOrigin.Begin);
        return stream;
    }

    internal byte[] WriteAudit(AnnotationAuditEntity entity)
    {
        return JsonSerializer.SerializeToUtf8Bytes(entity, _systemOptions);
    }

    internal int MeasureAuditEntry(AnnotationAuditEntry entry)
    {
        return JsonSerializer.SerializeToUtf8Bytes(entry, _systemOptions).Length;
    }
}

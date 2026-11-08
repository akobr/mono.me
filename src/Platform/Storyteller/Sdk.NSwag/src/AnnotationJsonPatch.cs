using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Sdk;

// A JSON Patch (RFC 6902) of an annotation's editable document, for PatchAnnotationVersionedAsync. The document has
// IsDisabled, ValidFrom, ExpiresAt, TimeZone, Title, Description, DocumentationLink, Labels, Values and, for a unit,
// UnitType and UnitDefinition, always present (null when unset; Labels and Values never null), so every operation is an
// add: it sets a member, a value or appends a label whatever was there. The server keeps a label once.
public sealed class AnnotationJsonPatch
{
    private readonly JArray _operations = [];
    private readonly JsonSerializer _serializer;

    // serializer: how the values become JSON; the default one writes a DateTimeOffset in ISO 8601 with its offset.
    public AnnotationJsonPatch(JsonSerializer? serializer = null)
    {
        _serializer = serializer ?? JsonSerializer.CreateDefault();
    }

    public bool IsEmpty => _operations.Count == 0;

    public int Count => _operations.Count;

    // A member of the editable document, for example nameof(Annotation.Title); a whole Labels or Values replaces the set.
    public AnnotationJsonPatch Set(string member, object? value)
    {
        ArgumentException.ThrowIfNullOrEmpty(member);
        return Add("/" + Escape(member), value);
    }

    // Unsets a member; Labels and Values become empty.
    public AnnotationJsonPatch Clear(string member)
    {
        return Set(member, null);
    }

    // One value of Values, by its name, replaced whole.
    public AnnotationJsonPatch SetValue(string name, object? value)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Add("/Values/" + Escape(name), value);
    }

    // Appends a label; one the annotation has changes nothing.
    public AnnotationJsonPatch AddLabel(string label)
    {
        ArgumentNullException.ThrowIfNull(label);
        return Add("/Labels/-", label);
    }

    // A copy of the operations, for PatchAnnotationVersionedAsync.
    public JArray ToJson()
    {
        return (JArray)_operations.DeepClone();
    }

    public override string ToString()
    {
        return _operations.ToString(Formatting.None);
    }

    // A reference token of a JSON Pointer (RFC 6901): ~ is ~0 and / is ~1.
    public static string Escape(string segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        return segment.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
    }

    private AnnotationJsonPatch Add(string path, object? value)
    {
        _operations.Add(new JObject
        {
            ["op"] = "add",
            ["path"] = path,
            ["value"] = ToToken(value),
        });
        return this;
    }

    private JToken ToToken(object? value)
    {
        return value switch
        {
            null => JValue.CreateNull(),
            JToken token => token.DeepClone(),
            _ => JToken.FromObject(value, _serializer),
        };
    }
}

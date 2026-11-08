using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using _42.Platform.Storyteller.Configuring;
using global::Json.Patch;
using global::Json.Pointer;

namespace _42.Platform.Storyteller.Annotating;

// A partial write of an annotation's editable document: an RFC 7396 merge patch or an RFC 6902 JSON Patch.
// Parsed up front; Apply is pure, so a write can run it again on every attempt against fresh state.
public sealed class AnnotationPatch
{
    // The most a document may weigh while a JSON Patch builds it (about its size in characters). A copy doubles a value
    // with a few bytes of patch, so the weight of every value an operation adds is counted before it is added.
    internal const long MaxDocumentWeight = 16 * 1024 * 1024;

    // The deepest a JSON Patch may nest a value of the document, the root being 1: the depth the API reads a body to.
    internal const int MaxDocumentDepth = 64;

    // The public properties of each runtime type. Those that are not editable (identity, type-specific, relation
    // and server-owned members) a merge patch ignores, as a full write does, so a read body can be sent back.
    private static readonly ConcurrentDictionary<Type, IReadOnlySet<string>> Properties = new();

    private readonly JsonObject? _mergePatch;
    private readonly JsonPatch? _jsonPatch;

    private AnnotationPatch(AnnotationPatchFormat format, JsonObject? mergePatch, JsonPatch? jsonPatch)
    {
        Format = format;
        _mergePatch = mergePatch;
        _jsonPatch = jsonPatch;
    }

    public AnnotationPatchFormat Format { get; }

    // A merge patch without members, or a JSON Patch without operations.
    public bool IsEmpty => Format == AnnotationPatchFormat.MergePatch
        ? _mergePatch!.Count == 0
        : _jsonPatch!.Operations.Count == 0;

    public static AnnotationPatch Merge(JsonObject document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new AnnotationPatch(AnnotationPatchFormat.MergePatch, (JsonObject)document.DeepClone(), null);
    }

    public static AnnotationPatch Json(JsonArray operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        return new AnnotationPatch(AnnotationPatchFormat.JsonPatch, null, ParseJsonPatch(operations));
    }

    // The patched annotation, normalized and validated as a full write is. Never changes the current annotation.
    public Annotation Apply(Annotation current)
    {
        ArgumentNullException.ThrowIfNull(current);

        var document = AnnotationPatchDocument.From(current);
        var patched = Format == AnnotationPatchFormat.MergePatch
            ? ApplyMergePatch(document, current)
            : ApplyJsonPatch(document);
        var annotation = AnnotationPatchDocument.ToAnnotation(patched, current);
        var normalized = AnnotationFactory.Normalize(annotation, AnnotationKey.Parse(current.AnnotationKey));

        AnnotationRules.Validate(normalized);
        return normalized;
    }

    private static JsonPatch ParseJsonPatch(JsonArray operations)
    {
        // JsonPatch.Net fails a null operation with a NullReferenceException
        if (operations.Any(operation => operation is not JsonObject))
        {
            throw new JsonPatchException("Invalid JSON Patch document: every operation must be a JSON object.", JsonPatchFailureKind.Invalid);
        }

        try
        {
            return operations.Deserialize<JsonPatch>()
                ?? throw new JsonPatchException("Invalid JSON Patch document.", JsonPatchFailureKind.Invalid);
        }
        catch (Exception exception) when (exception is JsonException or PointerParseException or InvalidOperationException)
        {
            // Unknown op, missing path or value, a malformed pointer, or a value nested deeper than a body is read: the
            // document itself is not a valid patch.
            throw new JsonPatchException($"Invalid JSON Patch document: {exception.Message}", JsonPatchFailureKind.Invalid);
        }
    }

    // RFC 7396 MergePatch(Target, Patch) where the target is an object: null removes a member, an object merges
    // into an object, anything else replaces.
    private static void MergeInto(JsonObject target, JsonObject patch)
    {
        foreach (var (name, value) in patch)
        {
            if (value is null)
            {
                target.Remove(name);
            }
            else if (value is JsonObject patchObject && target[name] is JsonObject targetObject)
            {
                MergeInto(targetObject, patchObject);
            }
            else
            {
                target[name] = MergePatch(value);
            }
        }
    }

    // RFC 7396 MergePatch over a target that is not an object: an object patch applies to {} (dropping its nulls),
    // anything else is a copy of the patch, so the patch is never attached to the document.
    private static JsonNode MergePatch(JsonNode patch)
    {
        if (patch is not JsonObject patchObject)
        {
            return patch.DeepClone();
        }

        var result = new JsonObject();
        MergeInto(result, patchObject);
        return result;
    }

    // One RFC 6902 operation over the document; the error, or null when it applied. Values are copied in, so the
    // parsed patch is never attached to a document.
    private static string? ApplyOperation(ref JsonNode? root, PatchOperation operation)
    {
        var path = GetSegments(operation.Path);

        switch (operation.Op)
        {
            case OperationType.Add:
                return Add(ref root, path, operation.Value?.DeepClone(), operation.Path);

            case OperationType.Remove:
                return Remove(root, path, operation.Path, out _);

            case OperationType.Replace:
                return Replace(ref root, path, operation.Value?.DeepClone(), operation.Path);

            case OperationType.Move:
            {
                var from = GetSegments(operation.From);

                if (from.Length < path.Length && from.SequenceEqual(path.Take(from.Length), StringComparer.Ordinal))
                {
                    return $"The value at '{operation.From}' cannot be moved into itself ('{operation.Path}').";
                }

                return Remove(root, from, operation.From, out var value)
                    ?? Add(ref root, path, value, operation.Path);
            }

            case OperationType.Copy:
            {
                var from = GetSegments(operation.From);

                return TryResolve(root, from, from.Length, out var value)
                    ? Add(ref root, path, value?.DeepClone(), operation.Path)
                    : NotFound(operation.From);
            }

            case OperationType.Test:
                if (!TryResolve(root, path, path.Length, out var actual))
                {
                    return NotFound(operation.Path);
                }

                return JsonNode.DeepEquals(actual, operation.Value)
                    ? null
                    : $"The value at '{operation.Path}' is not the expected value.";

            default:
                return $"The operation '{operation.Op}' is not supported.";
        }
    }

    private static string? Add(ref JsonNode? root, string[] path, JsonNode? value, JsonPointer pointer)
    {
        if (path.Length == 0)
        {
            root = value;
            return null;
        }

        if (!TryResolve(root, path, path.Length - 1, out var parent))
        {
            return NotFound(pointer);
        }

        var key = path[^1];

        switch (parent)
        {
            case JsonObject parentObject:
                parentObject[key] = value;
                return null;

            case JsonArray parentArray when key == "-":
                parentArray.Add(value);
                return null;

            case JsonArray parentArray when TryGetIndex(key, parentArray.Count + 1, out var index):
                parentArray.Insert(index, value);
                return null;

            default:
                return NotFound(pointer);
        }
    }

    private static string? Remove(JsonNode? root, string[] path, JsonPointer pointer, out JsonNode? removed)
    {
        removed = null;

        if (path.Length == 0)
        {
            return "The whole document cannot be removed.";
        }

        if (!TryResolve(root, path, path.Length - 1, out var parent))
        {
            return NotFound(pointer);
        }

        var key = path[^1];

        switch (parent)
        {
            case JsonObject parentObject when parentObject.TryGetPropertyValue(key, out removed):
                parentObject.Remove(key);
                return null;

            case JsonArray parentArray when TryGetIndex(key, parentArray.Count, out var index):
                removed = parentArray[index];
                parentArray.RemoveAt(index);
                return null;

            default:
                return NotFound(pointer);
        }
    }

    private static string? Replace(ref JsonNode? root, string[] path, JsonNode? value, JsonPointer pointer)
    {
        if (path.Length == 0)
        {
            root = value;
            return null;
        }

        if (!TryResolve(root, path, path.Length - 1, out var parent))
        {
            return NotFound(pointer);
        }

        var key = path[^1];

        switch (parent)
        {
            case JsonObject parentObject when parentObject.ContainsKey(key):
                parentObject[key] = value;
                return null;

            case JsonArray parentArray when TryGetIndex(key, parentArray.Count, out var index):
                parentArray[index] = value;
                return null;

            default:
                return NotFound(pointer);
        }
    }

    // What the operation adds: its weight joins the document's (a replaced or moved value is not taken off, so the sum
    // only grows), and the value may not end deeper than MaxDocumentDepth. The error, or null.
    private static string? CheckGrowth(JsonNode? root, PatchOperation operation, ref long weight)
    {
        JsonNode? value;

        switch (operation.Op)
        {
            case OperationType.Add:
            case OperationType.Replace:
                value = operation.Value;
                break;

            case OperationType.Copy:
            case OperationType.Move:
                // a missing source fails in the operation itself
                if (!TryResolve(root, GetSegments(operation.From), GetSegments(operation.From).Length, out value))
                {
                    return null;
                }

                break;

            default:
                return null;
        }

        var added = Weigh(value, MaxDocumentWeight - weight, out var depth);

        if (operation.Op != OperationType.Move)
        {
            weight += added;
        }

        if (weight > MaxDocumentWeight)
        {
            return $"The patched annotation would be larger than {MaxDocumentWeight} characters.";
        }

        // the root is 1, so a value at a path of n segments starts at depth n + 1
        return GetSegments(operation.Path).Length + depth > MaxDocumentDepth
            ? $"The patched annotation would nest deeper than {MaxDocumentDepth} levels."
            : null;
    }

    // About the size of the node in characters, and its depth (a value is 1). Iterative, so no depth overflows the
    // stack; it stops counting once the weight passes limit.
    private static long Weigh(JsonNode? node, long limit, out int depth)
    {
        long weight = 0;
        depth = 0;
        var pending = new Stack<(JsonNode? Node, int Depth)>();
        pending.Push((node, 1));

        while (pending.Count > 0 && weight <= limit)
        {
            var (current, level) = pending.Pop();
            depth = Math.Max(depth, level);

            switch (current)
            {
                case JsonObject currentObject:
                    weight += 2;

                    foreach (var (name, child) in currentObject)
                    {
                        weight += name.Length + 4;
                        pending.Push((child, level + 1));
                    }

                    break;

                case JsonArray currentArray:
                    weight += 2;

                    foreach (var item in currentArray)
                    {
                        weight += 1;
                        pending.Push((item, level + 1));
                    }

                    break;

                case JsonValue currentValue when currentValue.GetValueKind() == JsonValueKind.String:
                    weight += currentValue.GetValue<string>().Length + 2;
                    break;

                default:
                    // a number, a boolean or null
                    weight += 8;
                    break;
            }
        }

        return weight;
    }

    // The node at the first count segments; false when one of them does not exist. A JSON null is a node too (null).
    private static bool TryResolve(JsonNode? root, string[] segments, int count, out JsonNode? node)
    {
        node = root;

        for (var index = 0; index < count; index++)
        {
            switch (node)
            {
                case JsonObject nodeObject when nodeObject.TryGetPropertyValue(segments[index], out var child):
                    node = child;
                    break;

                case JsonArray nodeArray when TryGetIndex(segments[index], nodeArray.Count, out var item):
                    node = nodeArray[item];
                    break;

                default:
                    node = null;
                    return false;
            }
        }

        return true;
    }

    // An RFC 6901 array index: 0, or digits without a leading zero, below the limit.
    private static bool TryGetIndex(string segment, int limit, out int index)
    {
        index = -1;

        return segment.Length > 0
               && (segment.Length == 1 || segment[0] != '0')
               && int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out index)
               && index < limit;
    }

    // The unescaped segments of a pointer (RFC 6901: ~1 is '/', then ~0 is '~'). JsonPatch.Net 5.0.3 does not unescape
    // the last segment of a path, so add /Values/a~1b would write the member "a~1b": the operations are applied here.
    private static string[] GetSegments(JsonPointer pointer)
    {
        var segments = new string[pointer.SegmentCount];

        for (var index = 0; index < segments.Length; index++)
        {
            segments[index] = pointer[index].ToString()
                .Replace("~1", "/", StringComparison.Ordinal)
                .Replace("~0", "~", StringComparison.Ordinal);
        }

        return segments;
    }

    private static string NotFound(JsonPointer pointer)
    {
        return $"The path '{pointer}' does not exist.";
    }

    private static IReadOnlySet<string> GetProperties(Type runtimeType)
    {
        return runtimeType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static InvalidInputException Invalid(string message)
    {
        return new InvalidInputException(message, ErrorCodes.InvalidAnnotation);
    }

    // RFC 7396 over the editable document. Member names match case-insensitively, as the API reads bodies;
    // Values merge recursively, every other member is replaced (null clears it).
    private JsonObject ApplyMergePatch(JsonObject document, Annotation current)
    {
        var members = AnnotationPatchDocument.GetMembers(current);
        var properties = Properties.GetOrAdd(current.GetType(), GetProperties);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, value) in _mergePatch!)
        {
            if (!seen.Add(name))
            {
                throw Invalid($"The annotation patch has the member '{name}' more than once.");
            }

            var member = members.FirstOrDefault(editable => string.Equals(editable, name, StringComparison.OrdinalIgnoreCase));

            if (member is null)
            {
                // a member of the annotation that is not editable is ignored, anything else is a mistake
                if (properties.Contains(name))
                {
                    continue;
                }

                throw Invalid($"The annotation patch has an unknown member '{name}'.");
            }

            if (value is null)
            {
                document.Remove(member);
            }
            else if (member == nameof(Annotation.Values) && value is JsonObject values && document[member] is JsonObject target)
            {
                MergeInto(target, values);
            }
            else
            {
                document[member] = MergePatch(value);
            }
        }

        return document;
    }

    // RFC 6902 over the editable document; paths are case-sensitive. JsonPatch.Net parsed and validated the operations.
    // The document may not outgrow MaxDocumentWeight or MaxDocumentDepth on the way.
    private JsonObject ApplyJsonPatch(JsonObject document)
    {
        JsonNode? root = document;
        var weight = Weigh(document, MaxDocumentWeight, out _);

        for (var index = 0; index < _jsonPatch!.Operations.Count; index++)
        {
            var operation = _jsonPatch.Operations[index];
            var error = CheckGrowth(root, operation, ref weight) ?? ApplyOperation(ref root, operation);

            if (error is not null)
            {
                var kind = operation.Op == OperationType.Test
                    ? JsonPatchFailureKind.TestFailed
                    : JsonPatchFailureKind.OperationFailed;
                throw new JsonPatchException($"JSON Patch operation {index} failed: {error}", kind, index);
            }
        }

        return root as JsonObject
            ?? throw new JsonPatchException("JSON Patch result is not a JSON object.", JsonPatchFailureKind.Invalid);
    }
}

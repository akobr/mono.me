using System.Text.Json.Nodes;

namespace _42.Platform.Storyteller.Binding.Object;

/// <summary>
/// Serialized length used by the per-evaluation size budget.
/// Null is the four characters of the JSON text <c>null</c>.
/// </summary>
internal static class EvaluationSize
{
    public static int SerializedLength(JsonNode? node)
    {
        return node?.ToJsonString().Length ?? 4;
    }
}

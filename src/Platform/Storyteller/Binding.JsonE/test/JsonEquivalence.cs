using System.Linq;
using System.Text.Json.Nodes;

namespace _42.Platform.Storyteller.Binding.JsonE.UnitTests;

/// <summary>
/// Structural JSON equality: object properties in any order, numbers by value.
/// </summary>
internal static class JsonEquivalence
{
    public static bool AreEquivalent(JsonNode? left, JsonNode? right)
    {
        switch (left, right)
        {
            case (null, null):
                return true;
            case (JsonObject leftObject, JsonObject rightObject):
                return leftObject.Count == rightObject.Count &&
                       leftObject.All(property =>
                           rightObject.TryGetPropertyValue(property.Key, out var other) &&
                           AreEquivalent(property.Value, other));
            case (JsonArray leftArray, JsonArray rightArray):
                return leftArray.Count == rightArray.Count &&
                       leftArray.Zip(rightArray).All(pair => AreEquivalent(pair.First, pair.Second));
            case (JsonValue leftValue, JsonValue rightValue):
                if (leftValue.TryGetValue<decimal>(out var leftNumber) && rightValue.TryGetValue<decimal>(out var rightNumber))
                {
                    return leftNumber == rightNumber;
                }

                return leftValue.ToJsonString() == rightValue.ToJsonString();
            default:
                return false;
        }
    }
}

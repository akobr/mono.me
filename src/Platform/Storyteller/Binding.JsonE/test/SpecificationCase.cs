using System.Text.Json.Nodes;

namespace _42.Platform.Storyteller.Binding.JsonE.UnitTests;

internal sealed record SpecificationCase(
    string Name,
    JsonNode? Template,
    JsonObject Context,
    JsonNode? Expected,
    bool HasExpected,
    bool ExpectsError)
{
    public JsonObject CreateContext()
    {
        var context = (JsonObject)Context.DeepClone();
        context["now"] = SpecificationSuite.Now;
        return context;
    }

    public override string ToString()
    {
        return Name;
    }
}

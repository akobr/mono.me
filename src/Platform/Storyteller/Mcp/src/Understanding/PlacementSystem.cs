using System.ComponentModel;

namespace _42.Platform.Storyteller.Mcp.Understanding;

// A system a value belongs to, by the names its most specific annotation key is made of.
public sealed class PlacementSystem
{
    [Description("The subject name (sbt.{subject}), for example northwind.")]
    public string? Subject { get; init; }

    [Description("The responsibility name (rst.{responsibility}), for example invoicing.")]
    public string? Responsibility { get; init; }

    [Description("The context name of the subject (cnt.{subject}.{context}), for example production; needs subject.")]
    public string? Context { get; init; }

    [Description("The unit name of the responsibility (unt.{responsibility}.{unit}), for example export; needs responsibility.")]
    public string? Unit { get; init; }
}

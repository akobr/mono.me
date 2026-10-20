using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Configuring;

// The step that set each value of a composed document, carried by the real merge instead of a second implementation of
// its rules. Every applied document is marked with its step; MergeInto clones each token it takes from the incoming
// document, and a clone copies the annotations, while a token it keeps (an equal scalar, an existing container or array
// item) keeps its own.
public static class ConfigurationCompositionOrigins
{
    private static readonly JsonCloneSettings WithoutAnnotations = new() { CopyAnnotations = false };

    // A deep clone of the document in which every value token carries the step; the document itself is never annotated.
    public static JObject Mark(JObject content, int step)
    {
        var marked = (JObject)content.DeepClone(WithoutAnnotations);
        var origin = new StepOrigin(step);
        marked.AddAnnotation(origin);

        foreach (var token in marked.Descendants())
        {
            // the merge recreates the properties, their values carry the step
            if (token.Type != JTokenType.Property)
            {
                token.AddAnnotation(origin);
            }
        }

        return marked;
    }

    // One origin per value of the document, in document order: a container before its children, a property as its
    // value, the root left out. The paths are RFC 6901 JSON Pointers.
    public static IReadOnlyList<ConfigurationCompositionOrigin> Collect(JObject content)
    {
        var origins = new List<ConfigurationCompositionOrigin>();
        CollectChildren(content, string.Empty, origins);
        return origins;
    }

    // A clone without the steps.
    public static JObject Strip(JObject content)
    {
        return (JObject)content.DeepClone(WithoutAnnotations);
    }

    private static void CollectChildren(JContainer container, string path, List<ConfigurationCompositionOrigin> origins)
    {
        switch (container)
        {
            case JObject jObject:
                foreach (var property in jObject.Properties())
                {
                    CollectValue(property.Value, $"{path}/{EscapePointerSegment(property.Name)}", origins);
                }

                break;

            case JArray jArray:
                for (var index = 0; index < jArray.Count; index++)
                {
                    CollectValue(jArray[index], $"{path}/{index.ToString(CultureInfo.InvariantCulture)}", origins);
                }

                break;
        }
    }

    private static void CollectValue(JToken value, string path, List<ConfigurationCompositionOrigin> origins)
    {
        // every applied document is marked, so a value without a step is a defect of the composition
        var origin = value.Annotation<StepOrigin>()
            ?? throw new InvalidOperationException($"The value at '{path}' of the composed configuration has no origin.");

        origins.Add(new ConfigurationCompositionOrigin { Path = path, Step = origin.Step });

        if (value is JContainer container)
        {
            CollectChildren(container, path, origins);
        }
    }

    private static string EscapePointerSegment(string name)
    {
        return name
            .Replace("~", "~0", StringComparison.Ordinal)
            .Replace("/", "~1", StringComparison.Ordinal);
    }

    private sealed record class StepOrigin(int Step);
}

using _42.Platform.Storyteller.Mcp.Tools;

using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Mcp.Understanding;

// Rebuilds a composition's Content as docs/Platform/Storyteller/inheritance.md "Replaying" describes: for each node in
// order, the results of the nodes merged into it, then its own steps in order, skipping Missing sources; the result of
// the last node is the document. A flat replay of the steps would differ, because a finished document merged as a whole
// is not the same as its parts merged one after the other. Every value is marked with its step, as the API's
// ConfigurationCompositionOrigins does: the merge clones the tokens it takes and a clone keeps the mark, so the step that
// set any value can be read back.
public static class CompositionReplay
{
    private static readonly JsonCloneSettings WithoutAnnotations = new() { CopyAnnotations = false };

    // The steps of the composition in order of application, with the stored content of their sources.
    public static List<ReplayStep> StepsOf(Sdk.ConfigurationComposition composition)
    {
        var sources = (composition.Sources ?? []).ToDictionary(source => source.Id, StringComparer.Ordinal);

        return (composition.Steps ?? [])
            .OrderBy(step => step.Order)
            .Select(step => new ReplayStep(step.Order, step.SourceId, step.Node, ContentOf(sources.GetValueOrDefault(step.SourceId))))
            .ToList();
    }

    // The stored document of a source; null when it is Missing, as the composition skips it.
    public static JObject? ContentOf(Sdk.ConfigurationCompositionSource? source)
    {
        if (source is null || source.Presence == Sdk.ConfigurationCompositionSourcePresence.Missing)
        {
            return null;
        }

        return ToolResults.ToToken(source.Content) as JObject ?? new JObject();
    }

    public static ReplayResult Run(IEnumerable<Sdk.ConfigurationCompositionNode> nodes, IReadOnlyList<ReplayStep> steps)
    {
        var ordered = nodes.ToList();
        var results = new Dictionary<int, JObject>();
        var document = new JObject();

        foreach (var node in ordered)
        {
            document = new JObject();

            foreach (var child in ordered.Where(child => child.MergedInto == node.Index))
            {
                if (results.TryGetValue(child.Index, out var childResult))
                {
                    ConfigurationMerge.Merge(document, childResult);
                }
            }

            for (var index = 0; index < steps.Count; index++)
            {
                if (steps[index].Node == node.Index && steps[index].Content is { } content)
                {
                    ConfigurationMerge.Merge(document, MarkAll(content, index));
                }
            }

            results[node.Index] = document;
        }

        // nodes are listed in the order they finish, so the target is last
        return new ReplayResult(document, steps);
    }

    // The annotation keys from the node to the target, following MergedInto: the branch a value arrived through.
    public static IReadOnlyList<string> Via(IEnumerable<Sdk.ConfigurationCompositionNode> nodes, int node)
    {
        var byIndex = nodes.ToDictionary(item => item.Index);
        var path = new List<string>();
        int? current = node;

        while (current is { } index && byIndex.TryGetValue(index, out var item) && path.Count <= byIndex.Count)
        {
            path.Add(item.AnnotationKey);
            current = item.MergedInto;
        }

        return path;
    }

    // A clone in which every value carries the step; the document itself is never marked.
    private static JObject MarkAll(JObject content, int step)
    {
        var marked = (JObject)content.DeepClone(WithoutAnnotations);
        var mark = new Mark(step);

        foreach (var token in marked.Descendants())
        {
            // the merge recreates the properties, their values carry the step
            if (token.Type != JTokenType.Property)
            {
                token.AddAnnotation(mark);
            }
        }

        return marked;
    }

    internal sealed record Mark(int Step);
}

using _42.Platform.Storyteller.Mcp.Tools;

namespace _42.Platform.Storyteller.Mcp.Understanding;

// The places a value of some systems could be written, in the order of suggestion: the view template of their type (with
// typeDefault); for one system its own annotation, then its ancestors from the most specific; for several systems the
// lowest annotations they all share, then the other shared ones. Deepest first; at equal depth the type merged later
// first, because a later branch overrides an earlier one (docs/Platform/Storyteller/inheritance.md).
public static class PlacementCandidates
{
    public const string TemplateMeaning =
        "A value for every annotation of this type in the view, overriding what ancestors set; only an annotation's own content wins over it.";

    public static (IReadOnlyList<PlacementCandidate> Candidates, string? Notice) For(IReadOnlyList<AnnotationKey> systems, bool typeDefault)
    {
        var distinct = systems.DistinctBy(key => key.ToString(), StringComparer.Ordinal).ToList();
        var candidates = new List<PlacementCandidate>();
        string? notice = null;

        if (typeDefault)
        {
            var types = distinct.Select(key => key.TypeCode).Distinct(StringComparer.Ordinal).ToList();

            if (types.Count > 1)
            {
                throw new ToolInputException($"'typeDefault' needs systems of one annotation type, because a template belongs to one type; these are {string.Join(", ", types)}.");
            }

            var type = distinct[0].Type;
            candidates.Add(new PlacementCandidate
            {
                Kind = PlacementCandidate.TemplateKind,
                TemplateTypeCode = types[0],
                Level = type.ToString(),
                Meaning = TemplateMeaning,
                Reason = $"A default for every {type} annotation of the view (template:{types[0]}).",
            });
        }

        if (distinct.Count == 1)
        {
            var key = distinct[0];
            candidates.Add(ForAnnotation(key, "The value belongs to this system only."));
            candidates.AddRange(Deepest(key.GetAllAncestorKeys())
                .Select(ancestor => ForAnnotation(ancestor, $"Shared by every descendant of {ancestor}, {key} among them.")));
            return (candidates, notice);
        }

        var shared = distinct
            .Select(key => key.GetAllAncestorKeys().Append(key).Select(item => item.ToString()).ToHashSet(StringComparer.Ordinal))
            .Aggregate((left, right) =>
            {
                left.IntersectWith(right);
                return left;
            });
        var common = distinct[0].GetAllAncestorKeys().Append(distinct[0]).Where(key => shared.Contains(key.ToString())).ToList();

        if (common.Count == 0)
        {
            notice = typeDefault
                ? "These systems share no annotation: write the value on each system's own annotation, or use the template above."
                : distinct.Select(key => key.TypeCode).Distinct(StringComparer.Ordinal).Count() == 1
                    ? "These systems share no annotation: write the value on each system's own annotation, or ask with typeDefault for the template of their type."
                    : "These systems share no annotation: write the value on each system's own annotation.";
            return (candidates, notice);
        }

        // the lowest: no other shared annotation is a descendant of them
        var lowest = common.Where(key => !common.Any(other => other.IsDescendantOf(key))).ToList();
        candidates.AddRange(Deepest(lowest).Select(key => ForAnnotation(key, "The value is shared by these systems: the most specific annotation they all share.")));
        candidates.AddRange(Deepest(common.Except(lowest))
            .Select(key => ForAnnotation(key, $"Shared by these systems and by every other descendant of {key}.")));
        return (candidates, notice);
    }

    public static string MeaningOf(AnnotationType type)
    {
        return type switch
        {
            AnnotationType.Responsibility => "A value true for every customer of this capability (the responsibility).",
            AnnotationType.Unit => "This job of the capability, for every subject (the unit).",
            AnnotationType.Subject => "A value true for this subject, whichever capability you look at.",
            AnnotationType.Usage => "A value true for this subject consuming this capability, in every context.",
            AnnotationType.Context => "A value true for this subject in this world, whichever capability you look at.",
            AnnotationType.Execution => "A value true for this capability for this subject in this world.",
            AnnotationType.UnitOfExecution => "A value true for this job for this subject in this world.",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown annotation type."),
        };
    }

    private static PlacementCandidate ForAnnotation(AnnotationKey key, string reason)
    {
        return new PlacementCandidate
        {
            Kind = PlacementCandidate.AnnotationKind,
            AnnotationKey = key.ToString(),
            Level = key.Type.ToString(),
            Meaning = MeaningOf(key.Type),
            Reason = reason,
        };
    }

    private static IEnumerable<AnnotationKey> Deepest(IEnumerable<AnnotationKey> keys)
    {
        return keys
            .OrderByDescending(key => key.GetDepth())
            .ThenByDescending(key => key.Type)
            .ThenBy(key => key.ToString(), StringComparer.Ordinal);
    }
}

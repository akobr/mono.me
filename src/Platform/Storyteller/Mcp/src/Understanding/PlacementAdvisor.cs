using _42.Platform.Storyteller.Mcp.Tools;

using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Mcp.Understanding;

// Tells whether a candidate takes effect by replaying each system's composition with the value placed on the candidate
// (docs/Platform/Storyteller/inheritance.md, "Replaying"). An annotation candidate gets the value merged into every step
// of its configuration; a template candidate into the template step of every node of its type, added before the node's
// configuration when the view has no such template yet. A value is effective for a system and pointer when the replayed
// document holds it there. That catches a value overridden by a descendant and a value lost to a shared ancestor that is
// merged again after the candidate (the currency example of inheritance.md).
public static class PlacementAdvisor
{
    public const int MaxSystems = 20;

    // Evidence costs reads per candidate (the annotation, the deleted lookup, the descendants), so only the leaders get it.
    public const int EvidenceCandidates = 3;

    public const string IgnoredNullsNote = "Nulls in the fragment were ignored: a write never erases a value with null.";

    public const string SameValueNote =
        "The effective value already equals the placed one, but the winning step decides it: a change there changes it, a change on this candidate does not.";

    public static readonly IReadOnlyList<string> Notes =
    [
        "The tool writes nothing. Write on an annotation with set_configuration (it merges into the stored document) and on a template with set_template.",
        "Effective is the replay of each system's composition as it is stored now; a later write on another annotation can change it.",
        "A configuration written on an annotation that does not exist is inherited but not listed: create the annotation first (set_annotation or create_annotations create the missing ancestors).",
    ];

    // The most specific key the names of a system make (the AnnotationKey factories); names follow the rule of new names.
    public static AnnotationKey KeyOf(PlacementSystem? system, int index)
    {
        var prefix = $"systems[{index}]";

        if (system is null)
        {
            throw new ToolInputException($"'{prefix}' is empty; give the names of the system, for example {{\"subject\": \"northwind\", \"responsibility\": \"invoicing\"}}.");
        }

        var subject = Name(system.Subject, $"{prefix}.subject");
        var responsibility = Name(system.Responsibility, $"{prefix}.responsibility");
        var context = Name(system.Context, $"{prefix}.context");
        var unit = Name(system.Unit, $"{prefix}.unit");

        if (subject is null && responsibility is null)
        {
            throw new ToolInputException($"'{prefix}' names neither a subject nor a responsibility; every annotation key starts from one of them.");
        }

        if (unit is not null && responsibility is null)
        {
            throw new ToolInputException($"'{prefix}.unit' needs the responsibility the unit belongs to (unt.{{responsibility}}.{{unit}}).");
        }

        if (context is not null && subject is null)
        {
            throw new ToolInputException($"'{prefix}.context' needs the subject the context belongs to (cnt.{{subject}}.{{context}}).");
        }

        if (unit is not null && subject is not null && context is null)
        {
            throw new ToolInputException(
                $"'{prefix}' names a subject and a unit but no context: a unit of execution needs the context too "
                + "(uxe.{subject}.{responsibility}.{context}.{unit}); without the subject it is the unit itself (unt.{responsibility}.{unit}).");
        }

        return (subject, responsibility, context, unit) switch
        {
            ({ } s, { } r, { } c, { } u) => AnnotationKey.CreateUnitOfExecution(s, r, c, u),
            ({ } s, { } r, { } c, null) => AnnotationKey.CreateExecution(s, r, c),
            ({ } s, { } r, null, null) => AnnotationKey.CreateUsage(s, r),
            ({ } s, null, { } c, null) => AnnotationKey.CreateContext(s, c),
            (null, { } r, null, { } u) => AnnotationKey.CreateUnit(r, u),
            ({ } s, null, null, null) => AnnotationKey.CreateSubject(s),
            (null, { } r, null, null) => AnnotationKey.CreateResponsibility(r),
            _ => throw new ToolInputException($"'{prefix}' is not a combination of names that makes an annotation key; the forms are {AnnotationKeys.Format}."),
        };
    }

    // The candidate with its effect per system and pointer; EffectiveForAll over the systems that could be replayed.
    public static PlacementCandidate Assess(PlacementCandidate candidate, IReadOnlyList<SystemComposition> systems, PlacementTargets targets)
    {
        var effects = systems.SelectMany(system => AssessSystem(candidate, system, targets)).ToList();
        var known = effects.Where(effect => effect.Effective is not null).ToList();

        return candidate with
        {
            Effects = effects,
            EffectiveForAll = known.Count == 0 ? null : known.All(effect => effect.Effective == true),
        };
    }

    // Candidates that are effective for every system first; otherwise the order of suggestion stays.
    public static List<PlacementCandidate> Rank(IEnumerable<PlacementCandidate> candidates)
    {
        var list = candidates.ToList();
        return list.Where(candidate => candidate.EffectiveForAll == true).Concat(list.Where(candidate => candidate.EffectiveForAll != true)).ToList();
    }

    // Where each target's value comes from today in each system that has a composition.
    public static IReadOnlyList<CurrentDefinition> Today(IReadOnlyList<SystemComposition> systems, PlacementTargets targets)
    {
        var definitions = new List<CurrentDefinition>();

        foreach (var system in systems)
        {
            if (system.Composition is not { } composition)
            {
                continue;
            }

            var content = ToolResults.ToToken(composition.Content) as JObject ?? new JObject();
            var origins = (composition.Origins ?? []).DistinctBy(origin => origin.Path, StringComparer.Ordinal).ToDictionary(origin => origin.Path, StringComparer.Ordinal);
            var steps = (composition.Steps ?? []).ToDictionary(step => step.Order);
            var nodes = composition.Nodes ?? [];

            foreach (var pointer in targets.Pointers)
            {
                var token = JsonPointer.Find(content, pointer);

                if (token is null || !origins.TryGetValue(pointer, out var origin))
                {
                    definitions.Add(new CurrentDefinition(system.AnnotationKey, pointer, false));
                    continue;
                }

                var step = steps.GetValueOrDefault(origin.Step);
                definitions.Add(new CurrentDefinition(system.AnnotationKey, pointer, true)
                {
                    Value = token is JContainer ? null : token.DeepClone(),
                    ValueKind = token switch { JObject => "object", JArray => "array", _ => null },
                    Step = origin.Step,
                    SourceId = step?.SourceId,
                    Via = step is null ? null : CompositionReplay.Via(nodes, step.Node),
                });
            }
        }

        return definitions;
    }

    private static string? Name(string? value, string parameter)
    {
        return string.IsNullOrWhiteSpace(value) ? null : AnnotationNames.Ensure(value.Trim(), parameter);
    }

    private static IEnumerable<PlacementEffect> AssessSystem(PlacementCandidate candidate, SystemComposition system, PlacementTargets targets)
    {
        if (system.Composition is not { } composition)
        {
            return targets.Pointers.Select(pointer => new PlacementEffect(system.AnnotationKey, pointer, null)
            {
                Note = "The system's annotation has no configuration to replay: it does not exist yet, or it is deleted or inactive.",
            });
        }

        var nodes = (composition.Nodes ?? []).ToList();
        var steps = CompositionReplay.StepsOf(composition);
        var placed = candidate.Kind == PlacementCandidate.TemplateKind
            ? PlaceTemplate(steps, nodes, composition, candidate.TemplateTypeCode!, targets)
            : PlaceAnnotation(steps, candidate.AnnotationKey!, targets);

        if (!placed)
        {
            return targets.Pointers.Select(pointer => new PlacementEffect(system.AnnotationKey, pointer, null)
            {
                Note = "The candidate is not part of this system's lineage.",
            });
        }

        var result = CompositionReplay.Run(nodes, steps);
        var effective = ToolResults.ToToken(composition.Content);
        var sourceId = SourceIdOf(candidate);
        var stored = CompositionReplay.ContentOf((composition.Sources ?? []).FirstOrDefault(source => string.Equals(source.Id, sourceId, StringComparison.Ordinal)));
        var effects = new List<PlacementEffect>();

        for (var index = 0; index < targets.Pointers.Count; index++)
        {
            var pointer = targets.Pointers[index];

            // a replay of a scalar there would describe a write nobody makes
            if (targets.Unplaceable(index, effective, stored) is { } unplaceable)
            {
                effects.Add(new PlacementEffect(system.AnnotationKey, pointer, null) { Note = unplaceable });
                continue;
            }

            var token = JsonPointer.Find(result.Content, pointer);

            if (targets.IsEffective(index, token))
            {
                effects.Add(new PlacementEffect(system.AnnotationKey, pointer, true));
                continue;
            }

            // the value that took its place, or the value that replaced a container on the way to it
            var winner = result.OriginOf(token ?? JsonPointer.FindDeepest(result.Content, pointer));
            var placedValue = targets.ValueAt(index);
            effects.Add(new PlacementEffect(system.AnnotationKey, pointer, false)
            {
                WinningStep = winner?.Order,
                WinningSourceId = winner?.SourceId,
                Via = winner is null ? null : CompositionReplay.Via(nodes, winner.Node),
                Note = token is not null && placedValue is not null && JToken.DeepEquals(token, placedValue) ? SameValueNote : null,
            });
        }

        return effects;
    }

    // The composition source the candidate writes: an annotation's configuration or a view template.
    private static string SourceIdOf(PlacementCandidate candidate)
    {
        return candidate.Kind == PlacementCandidate.TemplateKind
            ? $"template:{candidate.TemplateTypeCode}"
            : $"configuration:{candidate.AnnotationKey}";
    }

    private static bool PlaceAnnotation(List<ReplayStep> steps, string annotationKey, PlacementTargets targets)
    {
        var sourceId = $"configuration:{annotationKey}";
        var placed = false;

        for (var index = 0; index < steps.Count; index++)
        {
            if (string.Equals(steps[index].SourceId, sourceId, StringComparison.Ordinal))
            {
                steps[index] = steps[index] with { Content = ConfigurationMerge.Merged(steps[index].Content, targets.Content) };
                placed = true;
            }
        }

        return placed;
    }

    // The template is merged at every node of its type, after the node's ancestors and before the node's own content.
    private static bool PlaceTemplate(List<ReplayStep> steps, List<Sdk.ConfigurationCompositionNode> nodes, Sdk.ConfigurationComposition composition, string typeCode, PlacementTargets targets)
    {
        var sourceId = $"template:{typeCode}";
        var stored = CompositionReplay.ContentOf((composition.Sources ?? []).FirstOrDefault(source => string.Equals(source.Id, sourceId, StringComparison.Ordinal)));
        var placed = false;

        foreach (var node in nodes.Where(node => string.Equals(node.AnnotationKey?.Split('.')[0], typeCode, StringComparison.OrdinalIgnoreCase)))
        {
            var content = ConfigurationMerge.Merged(stored, targets.Content);
            var existing = steps.FindIndex(step => step.Node == node.Index && string.Equals(step.SourceId, sourceId, StringComparison.Ordinal));

            if (existing >= 0)
            {
                steps[existing] = steps[existing] with { Content = content };
            }
            else
            {
                var first = steps.FindIndex(step => step.Node == node.Index);
                steps.Insert(first < 0 ? steps.Count : first, new ReplayStep(null, sourceId, node.Index, content));
            }

            placed = true;
        }

        return placed;
    }

    public sealed record SystemComposition(string AnnotationKey, Sdk.ConfigurationComposition? Composition);
}

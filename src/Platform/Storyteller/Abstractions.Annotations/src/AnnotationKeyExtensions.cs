using System;
using System.Collections.Generic;
using System.Linq;

namespace _42.Platform.Storyteller;

public static class AnnotationKeyExtensions
{
    /// <summary>
    /// The direct parents of the key in the inheritance graph, in merge order (by <see cref="AnnotationType"/>):
    /// a usage has its responsibility and subject, an execution its usage and context, a unit of execution its unit
    /// and execution. Responsibilities and subjects have none.
    /// </summary>
    public static IReadOnlyList<AnnotationKey> GetDirectAncestorKeys(this AnnotationKey @this)
    {
        return @this.Type switch
        {
            AnnotationType.Responsibility or AnnotationType.Subject => [],
            AnnotationType.Unit => [@this.GetResponsibilityKey()],
            AnnotationType.Usage => [@this.GetResponsibilityKey(), @this.GetSubjectKey()],
            AnnotationType.Context => [@this.GetSubjectKey()],
            AnnotationType.Execution => [@this.GetUsageKey(), @this.GetContextKey()],
            AnnotationType.UnitOfExecution => [@this.GetUnitKey(), @this.GetExecutionKey()],
            _ => throw new ArgumentOutOfRangeException(nameof(@this), @this.Type, "Unknown annotation type."),
        };
    }

    /// <summary>
    /// Every ancestor of the key, roots first: each ancestor comes after all of its own ancestors. The key itself is
    /// not included. A shared ancestor (the subject of an execution) is listed once.
    /// </summary>
    public static IReadOnlyList<AnnotationKey> GetAllAncestorKeys(this AnnotationKey @this)
    {
        var ancestors = new Dictionary<string, AnnotationKey>(StringComparer.Ordinal);
        var pending = new Queue<AnnotationKey>(@this.GetDirectAncestorKeys());

        while (pending.Count > 0)
        {
            var ancestor = pending.Dequeue();

            if (!ancestors.TryAdd(ancestor.ToString(), ancestor))
            {
                continue;
            }

            foreach (var parent in ancestor.GetDirectAncestorKeys())
            {
                pending.Enqueue(parent);
            }
        }

        // Depth in the graph orders roots first; the type and the key keep the order stable.
        return ancestors.Values
            .OrderBy(key => key.GetDepth())
            .ThenBy(key => key.Type)
            .ThenBy(key => key.ToString(), StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// The longest path from the key to a root of the graph: 0 for responsibilities and subjects, 3 for units of
    /// execution.
    /// </summary>
    public static int GetDepth(this AnnotationKey @this)
    {
        return @this.Type switch
        {
            AnnotationType.Responsibility or AnnotationType.Subject => 0,
            AnnotationType.Unit or AnnotationType.Usage or AnnotationType.Context => 1,
            AnnotationType.Execution => 2,
            AnnotationType.UnitOfExecution => 3,
            _ => throw new ArgumentOutOfRangeException(nameof(@this), @this.Type, "Unknown annotation type."),
        };
    }

    /// <summary>
    /// Whether <paramref name="ancestor"/> is one of the ancestors of the key in the inheritance graph.
    /// </summary>
    public static bool IsDescendantOf(this AnnotationKey @this, AnnotationKey ancestor)
    {
        var ancestorText = ancestor.ToString();
        return @this.GetAllAncestorKeys().Any(key => string.Equals(key.ToString(), ancestorText, StringComparison.Ordinal));
    }

    public static bool IsValid(this AnnotationKey @this)
    {
        return @this.Type switch
        {
            AnnotationType.Responsibility or AnnotationType.Subject => @this.Segments.Count == 2,
            AnnotationType.Usage or AnnotationType.Context or AnnotationType.Unit => @this.Segments.Count == 3,
            AnnotationType.Execution => @this.Segments.Count == 4,
            AnnotationType.UnitOfExecution => @this.Segments.Count == 5,
            _ => false,
        };
    }

    public static string GetSubjectName(this AnnotationKey @this)
    {
        if (@this.Type < AnnotationType.Subject)
        {
            throw new ArgumentException("Annotation which is not subject specific.", nameof(@this));
        }

        return @this.SubjectName;
    }

    public static AnnotationKey GetSubjectKey(this AnnotationKey @this)
    {
        return AnnotationKey.CreateSubject(@this.GetSubjectName());
    }

    public static string GetResponsibilityName(this AnnotationKey @this)
    {
        return @this.Type switch
        {
            AnnotationType.Responsibility or AnnotationType.Unit or AnnotationType.Usage or AnnotationType.Execution or AnnotationType.UnitOfExecution => @this.ResponsibilityName,
            _ => throw new ArgumentException("Annotation which is not responsibility specific.", nameof(@this)),
        };
    }

    public static AnnotationKey GetResponsibilityKey(this AnnotationKey @this)
    {
        return AnnotationKey.CreateResponsibility(@this.GetResponsibilityName());
    }

    public static string GetContextName(this AnnotationKey @this)
    {
        return @this.Type switch
        {
            AnnotationType.Context or AnnotationType.Execution or AnnotationType.UnitOfExecution => @this.ContextName,
            _ => throw new ArgumentException("Annotation which is not context specific.", nameof(@this)),
        };
    }

    public static AnnotationKey GetContextKey(this AnnotationKey @this)
    {
        return AnnotationKey.CreateContext(@this.GetSubjectName(), @this.GetContextName());
    }

    public static AnnotationKey GetUsageKey(this AnnotationKey @this)
    {
        return AnnotationKey.CreateUsage(@this.GetSubjectName(), @this.GetResponsibilityName());
    }

    public static string GetUnitName(this AnnotationKey @this)
    {
        return @this.Type switch
        {
            AnnotationType.Unit or AnnotationType.UnitOfExecution => @this.UnitName,
            _ => throw new ArgumentException("Annotation which is not unit specific.", nameof(@this)),
        };
    }

    public static AnnotationKey GetUnitKey(this AnnotationKey @this)
    {
        return AnnotationKey.CreateUnit(@this.GetResponsibilityName(), @this.GetUnitName());
    }

    public static AnnotationKey GetExecutionKey(this AnnotationKey @this)
    {
        return AnnotationKey.CreateExecution(@this.GetSubjectName(), @this.GetResponsibilityName(), @this.GetContextName());
    }

    /// <summary>
    /// Attempts to compute the ancestor key of <paramref name="ancestorType"/> for <paramref name="this"/>. Every
    /// ancestor's name is already embedded directly in a descendant key's own segments (e.g. an
    /// <see cref="AnnotationType.Execution"/> key carries its subject, responsibility, and context names), so no
    /// graph traversal is required. Returns <c>null</c> when <paramref name="ancestorType"/> is not a valid
    /// ancestor of <paramref name="this"/>'s type (e.g. asking a <see cref="AnnotationType.Responsibility"/> key for
    /// its <see cref="AnnotationType.Subject"/> ancestor), and <see cref="AnnotationType.UnitOfExecution"/> is
    /// never a valid ancestor of anything. Requesting <paramref name="this"/>'s own type returns an equivalent key
    /// to itself rather than throwing or returning <c>null</c>.
    /// </summary>
    public static AnnotationKey? TryGetAncestorKey(this AnnotationKey @this, AnnotationType ancestorType)
    {
        try
        {
            return ancestorType switch
            {
                AnnotationType.Responsibility => @this.GetResponsibilityKey(),
                AnnotationType.Subject => @this.GetSubjectKey(),
                AnnotationType.Usage => @this.GetUsageKey(),
                AnnotationType.Context => @this.GetContextKey(),
                AnnotationType.Unit => @this.GetUnitKey(),
                AnnotationType.Execution => @this.GetExecutionKey(),
                _ => null,
            };
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}

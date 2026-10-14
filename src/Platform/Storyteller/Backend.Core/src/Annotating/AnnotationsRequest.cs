using System.Linq.Expressions;

namespace _42.Platform.Storyteller.Annotating;

public class AnnotationsRequest
{
    public string Organization { get; set; } = Constants.DefaultTenantName;

    public string Project { get; set; } = Constants.DefaultProjectName;

    public string View { get; set; } = Constants.DefaultViewName;

    public string? PartitionKey { get; set; }

    public IEnumerable<ICondition> Conditions { get; set; } = Enumerable.Empty<ICondition>();

    public IEnumerable<AnnotationType> Types { get; set; } = Enumerable.Empty<AnnotationType>();

    public string? ContinuationToken { get; set; }

    public AnnotationDeletedFilter Deleted { get; set; } = AnnotationDeletedFilter.Exclude;

    // Also the disabled, scheduled and expired annotations; without it only the active ones (AnnotationStatus).
    // Independent of Deleted: a deleted annotation that is disabled needs both.
    public bool IncludeInactive { get; set; }

    // With Deleted = Only: just the roots of deletions.
    public bool DeletionRootsOnly { get; set; }

    // With Deleted = Only or Include: just what one delete removed.
    public string? DeletionId { get; set; }

    // Only the descendants of this key in the inheritance graph, of the requested types.
    public AnnotationKey? DescendantsOf { get; set; }

    public interface ICondition
    {
        public AnnotationType Against { get; }
    }

    public class Condition<TEntity> : ICondition
    {
        public AnnotationType Against { get; set; }

        public Expression<Func<TEntity, bool>> Predicate { get; set; } = _ => true;
    }
}

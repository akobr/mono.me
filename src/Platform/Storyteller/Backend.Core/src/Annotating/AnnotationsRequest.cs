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

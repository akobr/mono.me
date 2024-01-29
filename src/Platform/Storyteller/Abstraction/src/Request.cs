using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using _42.Platform.Storyteller.Entities;

namespace _42.Platform.Storyteller;

public class Request
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

    public class Condition<TAnnotation> : ICondition
        where TAnnotation : Annotation
    {
        public AnnotationType Against { get; } = AnnotationTypeMap.GetAnnotationType(typeof(TAnnotation));

        public Expression<Func<TAnnotation, bool>> Predicate { get; set; } = _ => true;
    }
}

using System.Collections.Generic;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Annotating;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// The writes of the tests that do not look at the audit, made by one test account.
public static class AnnotationServiceTestExtensions
{
    public static readonly AuditActor TestActor = new() { Kind = AuditActorKind.Account, Id = "user_test", Name = "Test User" };

    public static Task<IEnumerable<Annotation>> CreateAnnotationAsync(this IAnnotationService @this, string organization, Annotation annotation)
    {
        return @this.CreateAnnotationAsync(organization, annotation, TestActor);
    }

    public static Task UpdateAnnotationAsync(this IAnnotationService @this, string organization, Annotation annotation)
    {
        return @this.UpdateAnnotationAsync(organization, annotation, TestActor);
    }

    public static Task<IEnumerable<Annotation>> CreateAnnotationsAsync(this IAnnotationService @this, string organization, IEnumerable<Annotation> annotations)
    {
        return @this.CreateAnnotationsAsync(organization, annotations, TestActor);
    }

    public static Task<IEnumerable<Annotation>> CreateAnnotationsFromStringAsync(this IAnnotationService @this, string organization, IEnumerable<string> annotations)
    {
        return @this.CreateAnnotationsFromStringAsync(organization, annotations, TestActor);
    }

    public static Task<AnnotationDeletionResult> DeleteAnnotationAsync(this IAnnotationService @this, FullKey fullKey)
    {
        return @this.DeleteAnnotationAsync(fullKey, TestActor);
    }
}

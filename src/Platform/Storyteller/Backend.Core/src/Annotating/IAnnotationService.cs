using System.Threading.Tasks;

namespace _42.Platform.Storyteller.Annotating;

public interface IAnnotationService
{
    Task<bool> ExistAnnotationAsync(FullKey fullKey);

    Task<Annotation?> GetAnnotationAsync(FullKey fullKey);

    Task<AnnotationsResponse> GetAnnotationsAsync(AnnotationsRequest request);

    Task CreateAnnotationAsync(string organization, Annotation annotation);

    Task UpdateAnnotationAsync(string organization, Annotation annotation);

    Task DeleteAnnotationAsync(FullKey fullKey);
}

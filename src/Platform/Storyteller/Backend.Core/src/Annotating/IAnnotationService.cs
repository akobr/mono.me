using System.Threading.Tasks;

namespace _42.Platform.Storyteller.Annotating;

public interface IAnnotationService
{
    Task<Annotation?> GetAnnotationAsync(FullKey fullKey);

    Task<Annotation?> GetAnnotationAsync(string fullKey);

    Task<AnnotationsResponse> GetAnnotationsAsync(AnnotationsRequest request);

    Task CreateOrUpdateAnnotationAsync(string organization, Annotation annotation);
}

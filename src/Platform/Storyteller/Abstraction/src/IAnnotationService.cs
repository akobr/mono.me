using System.Threading.Tasks;
using _42.Platform.Storyteller.Entities;

namespace _42.Platform.Storyteller;

public interface IAnnotationService
{
    Task<Annotation?> GetAnnotationAsync(FullKey fullKey);

    Task<Annotation?> GetAnnotationAsync(string fullKey);

    Task<Response> GetAnnotationsAsync(Request request);

    Task CreateOrUpdateAnnotationAsync(string organization, Annotation annotation);
}

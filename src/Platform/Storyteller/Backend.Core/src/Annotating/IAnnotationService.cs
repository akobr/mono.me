namespace _42.Platform.Storyteller.Annotating;

public interface IAnnotationService
{
    Task<bool> ExistAnnotationAsync(FullKey fullKey);

    Task<Annotation?> GetAnnotationAsync(FullKey fullKey);

    Task<AnnotationsResponse> GetAnnotationsAsync(AnnotationsRequest request);

    Task<IEnumerable<Annotation>> CreateAnnotationAsync(string organization, Annotation annotation);

    Task UpdateAnnotationAsync(string organization, Annotation annotation);

    Task<IEnumerable<Annotation>> CreateAnnotationsAsync(string organization, IEnumerable<Annotation> annotations);

    Task<IEnumerable<Annotation>> CreateAnnotationsFromStringAsync(string organization, IEnumerable<string> annotations);

    Task DeleteAnnotationAsync(FullKey fullKey);
}

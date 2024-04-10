using System.Collections.Generic;
using System.Linq;

namespace _42.Platform.Storyteller;

public class AnnotationsResponse<TAnnotation>
    where TAnnotation : Annotation
{
    public IEnumerable<TAnnotation> Annotations { get; set; } = Enumerable.Empty<TAnnotation>();

    public string? ContinuationToken { get; set; }

    public int Count { get; set; }

    public static implicit operator List<TAnnotation>(AnnotationsResponse<TAnnotation> response)
    {
        return response.Annotations.ToList();
    }
}

using System.Collections.Generic;
using System.Linq;
using _42.Platform.Storyteller.Entities;

namespace _42.Platform.Storyteller;

public class AnnotationsResponse
{
    public IEnumerable<Annotation> Annotations { get; set; } = Enumerable.Empty<Annotation>();

    public string? ContinuationToken { get; set; }

    public int Count { get; set; }

    public static implicit operator List<Annotation>(AnnotationsResponse response)
    {
        return response.Annotations.ToList();
    }

    public AnnotationsResponse<TAnnotation> AsTyped<TAnnotation>()
        where TAnnotation : Annotation
    {
        return new AnnotationsResponse<TAnnotation>
        {
            Annotations = Annotations.OfType<TAnnotation>(),
            ContinuationToken = ContinuationToken,
            Count = Count,
        };
    }
}

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using _42.Platform.Storyteller.Entities;

namespace _42.Platform.Storyteller;

public class Response
{
    public IEnumerable<Annotation> Annotations { get; set; } = Enumerable.Empty<Annotation>();

    public string? ContinuationToken { get; set; }

    public int Count { get; set; }

    public static implicit operator List<Annotation>(Response response)
    {
        return response.Annotations.ToList();
    }

    public Response<TAnnotation> AsTyped<TAnnotation>()
        where TAnnotation : Annotation
    {
        return new Response<TAnnotation>
        {
            Annotations = Annotations.OfType<TAnnotation>(),
            ContinuationToken = ContinuationToken,
            Count = Count,
        };
    }
}

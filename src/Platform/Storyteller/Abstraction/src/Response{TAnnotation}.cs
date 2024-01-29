using System.Collections;
using System.Collections.Generic;
using System.Linq;
using _42.Platform.Storyteller.Entities;

namespace _42.Platform.Storyteller;

public class Response<TAnnotation> : IEnumerable<TAnnotation>
    where TAnnotation : Annotation
{
    public IEnumerable<TAnnotation> Annotations { get; set; } = Enumerable.Empty<TAnnotation>();

    public string? ContinuationToken { get; set; }

    public int Count { get; set; }

    public static implicit operator List<TAnnotation>(Response<TAnnotation> response)
    {
        return response.Annotations.ToList();
    }

    public IEnumerator<TAnnotation> GetEnumerator()
    {
        return Annotations.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return Annotations.GetEnumerator();
    }
}

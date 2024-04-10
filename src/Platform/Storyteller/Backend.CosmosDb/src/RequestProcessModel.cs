using System.Collections.Generic;
using System.Linq;
using _42.Platform.Storyteller.Annotating;
using Microsoft.Azure.Cosmos;

namespace _42.Platform.Storyteller;

public class RequestProcessModel
{
    public RequestProcessModel(AnnotationsRequest request)
    {
        Request = request;
        TypesMap = request.Types.ToHashSet();

        _ = ContinuationToken.TryParse(request.ContinuationToken, out var token);
        InputContinuationToken = token;

        HasPartitionKey = !string.IsNullOrWhiteSpace(request.PartitionKey);
        PartitionKey = HasPartitionKey ? new PartitionKey(request.PartitionKey) : null;
    }

    public AnnotationsRequest Request { get; }

    public bool HasPartitionKey { get; }

    public PartitionKey? PartitionKey { get; }

    public ISet<AnnotationType> TypesMap { get; }

    public ContinuationToken? InputContinuationToken { get; set; }

    public IEnumerable<Annotation> Annotations { get; set; } = Enumerable.Empty<Annotation>();

    public ContinuationToken? OutputContinuationToken { get; set; }

    public int TotalCount { get; set; }
}

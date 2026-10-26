namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// One operation of a transactional batch; the id is null for a stream payload.
public sealed record RecordedOperation(BatchOperationKind Kind, string Id, string IfMatchEtag);

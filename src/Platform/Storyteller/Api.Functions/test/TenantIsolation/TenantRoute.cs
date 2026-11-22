namespace _42.Platform.Storyteller.Api.Functions.UnitTests.TenantIsolation;

// One HTTP function: its family (a group of routes of one kind of resource), what decides its tenant, and whether a machine
// may call it. Body is the JSON a request needs to get past the function's own input checks; {organization}, {project} and
// {key} stand for the tenant the request names.
public sealed record TenantRoute(string Function, string Family, TenantGate Gate, TenantAddress Address, bool UsersOnly = false)
{
    public string? Body { get; init; }

    public string ContentType { get; init; } = "application/json";

    public IReadOnlyDictionary<string, string> Query { get; init; } = new Dictionary<string, string>();

    public override string ToString() => Function;
}

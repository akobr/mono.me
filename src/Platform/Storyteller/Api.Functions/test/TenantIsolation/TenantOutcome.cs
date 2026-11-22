namespace _42.Platform.Storyteller.Api.Functions.UnitTests.TenantIsolation;

// The answer of one request: its status code, and what it reached past the gate.
internal sealed record TenantOutcome(int Status, IReadOnlyList<TripwireCall> Reached, string? Detail = null)
{
    public bool IsRefused => Status is 401 or 403;

    public override string ToString()
    {
        var answer = Detail is null ? $"{Status}" : $"{Status} {Detail}";
        return Reached.Count == 0 ? answer : $"{answer}, reached {string.Join("; ", Reached)}";
    }
}

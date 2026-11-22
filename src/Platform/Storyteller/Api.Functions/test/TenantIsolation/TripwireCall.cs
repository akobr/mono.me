namespace _42.Platform.Storyteller.Api.Functions.UnitTests.TenantIsolation;

// A call that passed the gate of the API: a member of a service of a handler, or a member of the access service past the
// access check. Values are the strings among its arguments and their public properties, which tell the tenant it addressed.
internal sealed record TripwireCall(string Service, string Member, IReadOnlyList<string> Values)
{
    public override string ToString() => $"{Service}.{Member}({string.Join(", ", Values)})";
}

namespace _42.Platform.Storyteller.Api.Functions.UnitTests.TenantIsolation;

// A call that passed the gate of the API: a member of a service of a handler, or a member of the access service past the
// access check. Arguments are the strings of each argument and of its public properties, by the parameter's name, so a test
// can tell the caller from the tenant; Values are all of them.
internal sealed record TripwireCall(string Service, string Member, IReadOnlyDictionary<string, IReadOnlyList<string>> Arguments)
{
    public IReadOnlyList<string> Values => [.. Arguments.Values.SelectMany(values => values)];

    public IReadOnlyList<string> Of(string parameter) => Arguments.GetValueOrDefault(parameter) ?? [];

    public override string ToString() => $"{Service}.{Member}({string.Join(", ", Values)})";
}

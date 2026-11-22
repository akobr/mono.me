namespace _42.Platform.Storyteller.Api.Functions.UnitTests.TenantIsolation;

// The tenant a request names: the organization, project and view of its route, the access point key of a {key} route
// or of a body.
public sealed record TenantTarget(string Label, string Organization, string Project, string Key)
{
    public string View => TenantWorld.View;

    public override string ToString() => Label;
}

namespace _42.Platform.Storyteller.Api.Functions.UnitTests.TenantIsolation;

// Where the request names its tenant.
public enum TenantAddress
{
    None,

    // {organization}/{project} in the route.
    Route,

    // The access point key of v1/access/points/{key}.
    Key,

    // The access point key or organization in the body.
    Body,
}

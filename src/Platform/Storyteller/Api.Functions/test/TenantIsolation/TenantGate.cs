namespace _42.Platform.Storyteller.Api.Functions.UnitTests.TenantIsolation;

// What decides whether a caller may reach the tenant of a function.
public enum TenantGate
{
    // The API's own checks (CheckAccessTo*, CheckUserAccessTo*, KeyValueAuthorization), before any service of the handler.
    Api,

    // A service decides (the access, invitation, certificate renewal or token exchange service); the API hands it the
    // caller's own identity and the tenant as given.
    Service,

    // Scoped to the caller (its account, access points, projects and invitations, the exhibition): no tenant in the request.
    Caller,

    // Anonymous: no caller.
    Anonymous,
}

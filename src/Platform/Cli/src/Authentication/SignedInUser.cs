namespace _42.Platform.Cli.Authentication;

// OrganizationId: the AuthKit organization of the session; null for a session without one, Entra ID and machines.
public sealed record SignedInUser(string Id, string UserName, string? Name, string? OrganizationId = null);

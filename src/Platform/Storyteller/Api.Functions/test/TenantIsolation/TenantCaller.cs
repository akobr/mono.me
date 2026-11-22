namespace _42.Platform.Storyteller.Api.Functions.UnitTests.TenantIsolation;

// Who calls: a user of organization A, the same user through the MCP actor, and a machine of A's project of every kind.
public enum TenantCaller
{
    User,
    DelegatedUser,
    ApiKeyMachine,
    CertificateMachine,
    ClientCredentialsMachine,
    FederatedMachine,
}

using System.Reflection;

using _42.Platform.Storyteller.Accessing;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests.TenantIsolation;

// The access service of both tenants. Only the members of the gate answer (the role of an account on an access point, the
// machine of a project, an account by id); every other member is past the gate, so it records the call and throws.
internal class TenantAccess : DispatchProxy
{
    private static readonly Dictionary<string, Account> Accounts = new(StringComparer.Ordinal)
    {
        [TenantWorld.UserA] = Member(TenantWorld.UserA, TenantWorld.OrganizationA),
        [TenantWorld.UserB] = Member(TenantWorld.UserB, TenantWorld.OrganizationB),
    };

    private static readonly Dictionary<string, MachineAccess> Machines = new(StringComparer.Ordinal)
    {
        [Key(TenantWorld.OrganizationA, TenantWorld.Project, TenantWorld.ApiKeyMachineId)] = Machine(TenantWorld.ApiKeyMachineId, MachineCredentialKind.ApiKey),
        [Key(TenantWorld.OrganizationA, TenantWorld.Project, TenantWorld.CertificateMachineId)] = Machine(TenantWorld.CertificateMachineId, MachineCredentialKind.Certificate),
        [Key(TenantWorld.OrganizationA, TenantWorld.Project, TenantWorld.ClientCredentialsMachineId)] = Machine(TenantWorld.ClientCredentialsMachineId, MachineCredentialKind.ClientCredentials),
        [Key(TenantWorld.OrganizationA, TenantWorld.Project, TenantWorld.FederatedMachineId)] = Machine(TenantWorld.FederatedMachineId, MachineCredentialKind.Federated),

        // B has machines of its own, with other ids: a machine id is unique across the platform.
        [Key(TenantWorld.OrganizationB, TenantWorld.Project, "8e4a4b63-3d2b-4a4f-8e72-1b7d2a7cab01")] = Machine("8e4a4b63-3d2b-4a4f-8e72-1b7d2a7cab01", MachineCredentialKind.ApiKey),
        [Key(TenantWorld.OrganizationB, TenantWorld.Project, "8e4a4b63-3d2b-4a4f-8e72-1b7d2a7cab04")] = Machine("8e4a4b63-3d2b-4a4f-8e72-1b7d2a7cab04", MachineCredentialKind.Federated),
    };

    public static IAccessService Create()
    {
        return DispatchProxy.Create<IAccessService, TenantAccess>();
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        switch (targetMethod!.Name)
        {
            case nameof(IAccessService.GetAccountRoleAsync):
                var account = Accounts.GetValueOrDefault((string)args![0]!);
                return Task.FromResult(account?.AccessMap.GetValueOrDefault((string)args[1]!, AccountRole.None) ?? AccountRole.None);
            case nameof(IAccessService.GetMachineAccessAsync):
                return Task.FromResult(Machines.GetValueOrDefault(Key((string)args![0]!, (string)args[1]!, (string)args[2]!)));
            case nameof(IAccessService.GetAccountAsync):
                return Task.FromResult(Accounts.GetValueOrDefault((string)args![0]!));
            default:
                throw TripwireRecorder.Trip(nameof(IAccessService), targetMethod, args);
        }
    }

    // The owner of the organization and of its project app: two access points, as the store keeps them.
    private static Account Member(string id, string organization)
    {
        return new Account
        {
            Id = id,
            UserName = $"{id}@{organization}.test",
            Name = id,
            AccessMap = new Dictionary<string, AccountRole>(StringComparer.Ordinal)
            {
                [organization] = AccountRole.Owner,
                [$"{organization}.{TenantWorld.Project}"] = AccountRole.Owner,
            },
        };
    }

    private static MachineAccess Machine(string id, MachineCredentialKind kind)
    {
        return new MachineAccess
        {
            Id = id,
            ObjectId = id,
            AccessKey = "hash",
            Scopes = TenantWorld.AllScopes,
            CredentialKind = kind,
            CertificateThumbprint = kind == MachineCredentialKind.Certificate ? TenantWorld.Thumbprint : null,
        };
    }

    private static string Key(string organization, string project, string id) => $"{organization}/{project}/{id}";
}

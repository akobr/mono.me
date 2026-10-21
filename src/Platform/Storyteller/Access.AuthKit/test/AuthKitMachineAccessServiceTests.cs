using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Access.AuthKit.UnitTests;

public class AuthKitMachineAccessServiceTests
{
    // The recommended slugs, one per scope, and a user permission a machine never gets.
    private static readonly Dictionary<string, string> PermissionMap = new()
    {
        ["storyteller:annotation-read"] = "Annotation.Read",
        ["storyteller:annotation-write"] = "Annotation.Write",
        ["storyteller:configuration-read"] = "Configuration.Read",
        ["storyteller:configuration-write"] = "Configuration.Write",
        ["storyteller:secrets-read"] = "Secrets.Read",
        ["storyteller:secrets-write"] = "Secrets.Write",
        ["storyteller:configuration-transfer"] = "Configuration.Transfer",
        ["storyteller:schema-write"] = "Schema.Write",
        ["storyteller:template-write"] = "Template.Write",
        ["storyteller:annotation-manage"] = "Annotation.Manage",
        ["storyteller:keyvalue-manage"] = "KeyValue.Manage",
        ["storyteller:impersonate"] = "User.Impersonation",
    };

    private readonly FakeWorkOsManagementClient _client = new();

    [Fact]
    public async Task Create_MakesAnM2MApplicationInTheMachineOrganizationWithItsSecret()
    {
        var access = await CreateService().CreateMachineAccessAsync(Model(MachineScope.AnnotationRead, MachineScope.ConfigurationRead));

        _client.Calls.ShouldBe(["create-application", "create-secret:conn_app_01"]);
        var application = _client.CreatedApplication.ShouldNotBeNull();
        application.Name.ShouldMatch("^42\\.sform\\.org1\\.proj1\\.[0-9a-f]{32}$");
        application.Description.ShouldBe("organization=org1|project=proj1|scopes=AnnotationRead,ConfigurationRead");
        application.OrganizationId.ShouldBe("org_machines");
        application.Scopes.ShouldBe(["storyteller:annotation-read", "storyteller:configuration-read"]);
        application.ApplicationType.ShouldBe("m2m");

        access.Id.ShouldBe("client_m2m01");
        access.ObjectId.ShouldBe("conn_app_01");
        access.AccessKey.ShouldBe("plain-secret-1");
        access.Scopes.ShouldBe([MachineScope.AnnotationRead, MachineScope.ConfigurationRead]);
        access.CredentialKind.ShouldBe(MachineCredentialKind.ClientCredentials);
    }

    [Theory]
    [InlineData(MachineScope.AnnotationRead, "storyteller:annotation-read")]
    [InlineData(MachineScope.AnnotationWrite, "storyteller:annotation-write")]
    [InlineData(MachineScope.ConfigurationRead, "storyteller:configuration-read")]
    [InlineData(MachineScope.ConfigurationWrite, "storyteller:configuration-write")]
    [InlineData(MachineScope.SecretsRead, "storyteller:secrets-read")]
    [InlineData(MachineScope.SecretsWrite, "storyteller:secrets-write")]
    [InlineData(MachineScope.ConfigurationTransfer, "storyteller:configuration-transfer")]
    [InlineData(MachineScope.SchemaWrite, "storyteller:schema-write")]
    [InlineData(MachineScope.TemplateWrite, "storyteller:template-write")]
    [InlineData(MachineScope.AnnotationManage, "storyteller:annotation-manage")]
    [InlineData(MachineScope.KeyValueManage, "storyteller:keyvalue-manage")]
    public async Task Create_OneScope_GetsExactlyItsOnePermission(MachineScope scope, string slug)
    {
        var access = await CreateService().CreateMachineAccessAsync(Model(scope));

        _client.CreatedApplication!.Scopes.ShouldBe([slug]);
        access.Scopes.ShouldBe([scope]);
    }

    [Fact]
    public async Task Create_AllScopes_GetOnePermissionEachButNeverUserImpersonation()
    {
        var access = await CreateService().CreateMachineAccessAsync(Model([.. MachineScopes.All]));

        _client.CreatedApplication!.Scopes.ShouldBe(
            [
                "storyteller:annotation-read",
                "storyteller:annotation-write",
                "storyteller:configuration-read",
                "storyteller:configuration-write",
                "storyteller:secrets-read",
                "storyteller:secrets-write",
                "storyteller:configuration-transfer",
                "storyteller:schema-write",
                "storyteller:template-write",
                "storyteller:annotation-manage",
                "storyteller:keyvalue-manage",
            ]);
        access.Scopes.ShouldBe(MachineScopes.All);
    }

    [Fact]
    public async Task Create_WithAnnotation_RecordsItInTheDescription()
    {
        await CreateService().CreateMachineAccessAsync(Model(MachineScope.AnnotationRead) with { AnnotationKey = "sbt.payments" });

        _client.CreatedApplication!.Description.ShouldBe("organization=org1|project=proj1|scopes=AnnotationRead|annotation=sbt.payments");
        _client.CreatedApplication.Scopes.ShouldBe(["storyteller:annotation-read"]);
    }

    [Fact]
    public async Task Create_WithManagementScopes_DescribesAndReturnsTheRequestedScopes()
    {
        MachineScope[] scopes = [MachineScope.AnnotationRead, MachineScope.ConfigurationRead, MachineScope.ConfigurationTransfer, MachineScope.SchemaWrite];

        var access = await CreateService().CreateMachineAccessAsync(Model(scopes) with { AnnotationKey = "sbt.payments" });

        var application = _client.CreatedApplication.ShouldNotBeNull();
        application.Scopes.ShouldBe(
            [
                "storyteller:annotation-read",
                "storyteller:configuration-read",
                "storyteller:configuration-transfer",
                "storyteller:schema-write",
            ]);
        application.Description.ShouldBe(
            "organization=org1|project=proj1|scopes=AnnotationRead,ConfigurationRead,ConfigurationTransfer,SchemaWrite|annotation=sbt.payments");
        access.Scopes.ShouldBe(scopes);
    }

    [Fact]
    public async Task Create_ScopeWithoutPermission_FailsBeforeCallingWorkOs()
    {
        var service = CreateService(permissionMap: PermissionMap
            .Where(entry => entry.Value != "Secrets.Write")
            .ToDictionary());

        var exception = await Should.ThrowAsync<MachineAccessNotSupportedException>(() => service.CreateMachineAccessAsync(
            Model(MachineScope.ConfigurationRead, MachineScope.SecretsRead, MachineScope.SecretsWrite)));

        exception.Message.ShouldBe("No WorkOS permission in Auth:AuthKit:PermissionMap maps to the scope Secrets.Write.");
        _client.Calls.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Annotation.Read Configuration.Read", MachineScope.AnnotationRead, "Annotation.Read")]
    [InlineData("Annotation.ReadWrite", MachineScope.AnnotationWrite, "Annotation.Write")]
    [InlineData("Configuration.Secrets", MachineScope.SecretsRead, "Secrets.Read")]
    [InlineData("AnnotationRead", MachineScope.AnnotationRead, "Annotation.Read")]
    public async Task Create_PermissionThatIsNotExactlyTheScopeString_FailsBeforeCallingWorkOs(string mapped, MachineScope scope, string scopeString)
    {
        // No subset matching and no old strings: only a value equal to the scope string grants the scope.
        var service = CreateService(permissionMap: new Dictionary<string, string> { ["storyteller:other"] = mapped });

        var exception = await Should.ThrowAsync<MachineAccessNotSupportedException>(() => service.CreateMachineAccessAsync(Model(scope)));

        exception.Message.ShouldBe($"No WorkOS permission in Auth:AuthKit:PermissionMap maps to the scope {scopeString}.");
        _client.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Create_PermissionValue_IsComparedTrimmed()
    {
        var service = CreateService(permissionMap: new Dictionary<string, string> { ["storyteller:annotation-read"] = " Annotation.Read " });

        await service.CreateMachineAccessAsync(Model(MachineScope.AnnotationRead));

        _client.CreatedApplication!.Scopes.ShouldBe(["storyteller:annotation-read"]);
    }

    [Fact]
    public async Task Create_UndefinedScope_FailsBeforeCallingWorkOs()
    {
        var exception = await Should.ThrowAsync<MachineAccessNotSupportedException>(() => CreateService().CreateMachineAccessAsync(
            Model(MachineScope.AnnotationRead, (MachineScope)99)));

        exception.Message.ShouldBe("No WorkOS permission in Auth:AuthKit:PermissionMap maps to the scope 99.");
        _client.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Create_SecretFails_DeletesTheApplicationAgain()
    {
        _client.CreateSecretFailure = new WorkOsApiException(System.Net.HttpStatusCode.InternalServerError, "boom");

        await Should.ThrowAsync<WorkOsApiException>(() => CreateService().CreateMachineAccessAsync(Model(MachineScope.AnnotationRead)));

        _client.Calls.ShouldBe(["create-application", "create-secret:conn_app_01", "delete-application:conn_app_01"]);
    }

    [Fact]
    public async Task Create_WithoutMachineOrganization_IsNotSupported()
    {
        var service = CreateService(organizationId: null);

        await Should.ThrowAsync<MachineAccessNotSupportedException>(() => service.CreateMachineAccessAsync(Model(MachineScope.AnnotationRead)));

        _client.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Reset_MintsBeforeRevokingTheOldSecrets()
    {
        _client.Secrets.Add(new WorkOsClientSecret("secret_old1", null, "a", DateTimeOffset.UtcNow.AddDays(-2)));
        _client.Secrets.Add(new WorkOsClientSecret("secret_old2", null, "b", DateTimeOffset.UtcNow.AddDays(-1)));

        var secret = await CreateService().ResetMachineAccessAsync(Existing(), "org1", "proj1");

        secret.ShouldBe("plain-secret-1");
        _client.Calls.ShouldBe(
            [
                "list-secrets:conn_app_01",
                "create-secret:conn_app_01",
                "delete-secret:secret_old1",
                "delete-secret:secret_old2",
            ]);
        _client.Secrets.Select(remaining => remaining.Id).ShouldBe(["secret_new1"]);
    }

    [Fact]
    public async Task Reset_AtTheSecretLimit_FreesTheOldestSlotFirst()
    {
        for (var index = 0; index < AuthKitMachineAccessService.MaxClientSecrets; index++)
        {
            _client.Secrets.Add(new WorkOsClientSecret($"secret_old{index}", null, null, DateTimeOffset.UtcNow.AddDays(index - 10)));
        }

        await CreateService().ResetMachineAccessAsync(Existing(), "org1", "proj1");

        _client.Calls.Take(3).ShouldBe(["list-secrets:conn_app_01", "delete-secret:secret_old0", "create-secret:conn_app_01"]);
        _client.Secrets.Select(remaining => remaining.Id).ShouldBe(["secret_new1"]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Delete_RemovesTheApplicationByItsObjectId(bool existed)
    {
        _client.DeleteApplicationResult = existed;

        var deleted = await CreateService().DeleteMachineAccessAsync(Existing(), "org1", "proj1");

        deleted.ShouldBe(existed);
        _client.Calls.ShouldBe(["delete-application:conn_app_01"]);
    }

    private static MachineAccessCreate Model(params MachineScope[] scopes)
    {
        return new MachineAccessCreate { Organization = "org1", Project = "proj1", Scopes = scopes };
    }

    private static MachineAccess Existing()
    {
        return new MachineAccess
        {
            Id = "client_m2m01",
            ObjectId = "conn_app_01",
            AccessKey = "pla***",
            Scopes = [MachineScope.AnnotationRead, MachineScope.ConfigurationRead],
            CredentialKind = MachineCredentialKind.ClientCredentials,
        };
    }

    private AuthKitMachineAccessService CreateService(Dictionary<string, string>? permissionMap = null, string? organizationId = "org_machines")
    {
        var options = Options.Create(new UserAuthenticationOptions
        {
            Provider = IdentityProviderKind.AuthKit,
            AuthKit = new AuthKitOptions
            {
                ClientId = "client_123",
                ApiKey = "sk_test",
                AuthKitDomain = "https://example.authkit.app",
                MachineOrganizationId = organizationId,
                PermissionMap = permissionMap ?? PermissionMap,
            },
        });

        return new AuthKitMachineAccessService(_client, options, NullLogger<AuthKitMachineAccessService>.Instance);
    }
}

using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Cli.Authentication;
using _42.Platform.Cli.Commands;
using _42.Platform.Cli.Commands.MachineAccess;
using _42.Platform.Cli.Configuration;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Commands;

public class MachineCreateCommandTests
{
    private const string ApiUrl = "https://storyteller.example.com/api";
    private const string PipelineHeader = "To sign in sform in a pipeline, set:";

    private readonly RecordingConsole _console = new();
    private readonly Mock<IAccessApiClient> _accessApi = new();
    private readonly Mock<ICommandContext> _context = new();
    private readonly Mock<IAuthenticationConfigurationResolver> _authentication = new();

    public MachineCreateCommandTests()
    {
        _context.SetupGet(context => context.OrganizationName).Returns("org1");
        _context.SetupGet(context => context.ProjectName).Returns("proj1");
    }

    [Fact]
    public async Task ClientCredentials_WithServerEndpointAndScope_PrintsThemInTheCurlLine()
    {
        Returns(new MachineAccess
        {
            Id = "11111111-2222-3333-4444-555555555555",
            AccessKey = "secret-value",
            CredentialKind = MachineAccessCredentialKind.ClientCredentials,
            TokenEndpoint = "https://login.microsoftonline.com/tenant-1/oauth2/v2.0/token",
            TokenScope = "api://api-client/.default",
        });

        var exitCode = await Execute();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        Line("Token URL:").ShouldEndWith("https://login.microsoftonline.com/tenant-1/oauth2/v2.0/token");
        Line("Token scope:").ShouldEndWith("api://api-client/.default");
        Line("Client ID:").ShouldEndWith("11111111-2222-3333-4444-555555555555");
        Line("Client secret:").ShouldEndWith("secret-value");
        Line("curl").Trim().ShouldBe(
            "curl -s -X POST https://login.microsoftonline.com/tenant-1/oauth2/v2.0/token"
            + " -d grant_type=client_credentials -d client_id=11111111-2222-3333-4444-555555555555"
            + " -d client_secret=$CLIENT_SECRET -d scope=api://api-client/.default");
        _authentication.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ClientCredentials_WithoutServerEndpoint_FallsBackToTheDiscoveredAuthKitDomain()
    {
        Returns(new MachineAccess
        {
            Id = "client_01HXYZ",
            AccessKey = "secret-value",
            CredentialKind = MachineAccessCredentialKind.ClientCredentials,
        });
        _authentication
            .Setup(resolver => resolver.ResolveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedAuthentication
            {
                Provider = AuthenticationProvider.AuthKit,
                ClientId = "client_env",
                AuthKitDomain = "https://auth.example.com/",
            });

        await Execute();

        Line("Token URL:").ShouldEndWith("https://auth.example.com/oauth2/token");
        Line("curl").Trim().ShouldBe(
            "curl -s -X POST https://auth.example.com/oauth2/token"
            + " -d grant_type=client_credentials -d client_id=client_01HXYZ -d client_secret=$CLIENT_SECRET");
        _console.Lines.ShouldNotContain(line => line.StartsWith("Token scope:"));
    }

    [Fact]
    public async Task ClientCredentials_WithoutEndpointAndFailingDiscovery_PrintsAPlaceholder()
    {
        Returns(new MachineAccess
        {
            Id = "client_01HXYZ",
            AccessKey = "secret-value",
            CredentialKind = MachineAccessCredentialKind.ClientCredentials,
        });
        _authentication
            .Setup(resolver => resolver.ResolveAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AuthenticationException(AuthenticationFailureReason.ServiceError, "No provider."));

        var exitCode = await Execute();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        Line("Token URL:").ShouldEndWith("https://<your AuthKit domain>/oauth2/token");
    }

    [Fact]
    public async Task ApiKey_DoesNotPrintTokenInstructions()
    {
        Returns(new MachineAccess
        {
            Id = "11111111-2222-3333-4444-555555555555",
            AccessKey = "api-key",
            CredentialKind = MachineAccessCredentialKind.ApiKey,
        });

        await Execute();

        _console.Lines.ShouldNotContain(line => line.Contains("Token URL:") || line.Contains("curl"));
        _authentication.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task WithoutAKind_SendsNoKindSoTheProjectDefaultApplies()
    {
        MachineAccessCreate? sent = null;
        _accessApi
            .Setup(api => api.CreateMachineAccessAsync("org1", "proj1", It.IsAny<MachineAccessCreate>()))
            .Callback((string _, string _, MachineAccessCreate body) => sent = body)
            .ReturnsAsync(new MachineAccess { Id = "m1", AccessKey = "api-key" });

        await Execute();

        sent!.CredentialKind.ShouldBeNull();
        sent.CertificateLifetimeDays.ShouldBeNull();
        Newtonsoft.Json.JsonConvert.SerializeObject(sent).ShouldNotContain("CredentialKind");
    }

    [Theory]
    [InlineData("certificate", MachineAccessCreateCredentialKind.Certificate)]
    [InlineData("certificate-and-api-key", MachineAccessCreateCredentialKind.CertificateAndApiKey)]
    [InlineData("ClientCredentials", MachineAccessCreateCredentialKind.ClientCredentials)]
    public async Task AKind_IsSentWithTheRequest(string value, MachineAccessCreateCredentialKind expected)
    {
        MachineAccessCreate? sent = null;
        _accessApi
            .Setup(api => api.CreateMachineAccessAsync("org1", "proj1", It.IsAny<MachineAccessCreate>()))
            .Callback((string _, string _, MachineAccessCreate body) => sent = body)
            .ReturnsAsync(new MachineAccess { Id = "m1", AccessKey = string.Empty });

        var exitCode = await Execute(value, lifetimeDays: 30);

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        sent!.CredentialKind.ShouldBe(expected);
        sent.CertificateLifetimeDays.ShouldBe(30);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("certificate-and-api-key")]
    public async Task SharedCertificate_SendsCertificateAndApiKeyWithoutALifetime(string? kind)
    {
        MachineAccessCreate? sent = null;
        _accessApi
            .Setup(api => api.CreateMachineAccessAsync("org1", "proj1", It.IsAny<MachineAccessCreate>()))
            .Callback((string _, string _, MachineAccessCreate body) => sent = body)
            .ReturnsAsync(new MachineAccess
            {
                Id = "m1",
                AccessKey = "api-key",
                CredentialKind = MachineAccessCredentialKind.CertificateAndApiKey,
                UsesSharedCertificate = true,
            });

        var exitCode = await Execute(kind, lifetimeDays: 30, useSharedCertificate: true);

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        sent!.CredentialKind.ShouldBe(MachineAccessCreateCredentialKind.CertificateAndApiKey);
        sent.UseSharedCertificate.ShouldBe(true);
        sent.CertificateLifetimeDays.ShouldBeNull();
        _console.Lines.ShouldContain(line => line.Contains("shared certificate of the project"));
    }

    [Fact]
    public async Task SharedCertificate_WithAnotherKind_FailsWithoutCallingTheServer()
    {
        var exitCode = await Execute("api-key", useSharedCertificate: true);

        exitCode.ShouldBe(ExitCodes.ERROR_INPUT_PARSING);
        _accessApi.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task WithoutSharedCertificate_SendsNoFlag()
    {
        MachineAccessCreate? sent = null;
        _accessApi
            .Setup(api => api.CreateMachineAccessAsync("org1", "proj1", It.IsAny<MachineAccessCreate>()))
            .Callback((string _, string _, MachineAccessCreate body) => sent = body)
            .ReturnsAsync(new MachineAccess { Id = "m1", AccessKey = "api-key" });

        await Execute("certificate-and-api-key");

        sent!.UseSharedCertificate.ShouldBeNull();
    }

    [Fact]
    public async Task UnknownKind_FailsWithoutCallingTheServer()
    {
        var exitCode = await Execute("42");

        exitCode.ShouldBe(ExitCodes.ERROR_INPUT_PARSING);
        _accessApi.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ApiKey_PrintsTheVariablesOfAPipelineWithoutTheKey()
    {
        Returns(new MachineAccess { Id = "m1", AccessKey = "2s.top.secret", CredentialKind = MachineAccessCredentialKind.ApiKey });

        await Execute();

        PipelineVariables().ShouldBe([$"STORYTELLER_API_URL={ApiUrl}", "STORYTELLER_API_KEY=<access key above>"]);
    }

    [Fact]
    public async Task ClientCredentials_PrintTheProjectAndTheTokenRequest()
    {
        Returns(new MachineAccess
        {
            Id = "11111111-2222-3333-4444-555555555555",
            AccessKey = "secret-value",
            CredentialKind = MachineAccessCredentialKind.ClientCredentials,
            TokenEndpoint = "https://login.microsoftonline.com/tenant-1/oauth2/v2.0/token",
            TokenScope = "api://api-client/.default",
        });

        await Execute();

        PipelineVariables().ShouldBe(
        [
            $"STORYTELLER_API_URL={ApiUrl}",
            "STORYTELLER_PROJECT=org1.proj1",
            "STORYTELLER_CLIENT_ID=11111111-2222-3333-4444-555555555555",
            "STORYTELLER_CLIENT_SECRET=<client secret above>",
            "STORYTELLER_TOKEN_URL=https://login.microsoftonline.com/tenant-1/oauth2/v2.0/token",
            "STORYTELLER_TOKEN_SCOPE=api://api-client/.default",
        ]);
    }

    [Fact]
    public async Task ClientCredentials_WithAnUnknownTokenUrl_LeaveItOut()
    {
        Returns(new MachineAccess { Id = "client_01HXYZ", AccessKey = "secret-value", CredentialKind = MachineAccessCredentialKind.ClientCredentials });
        _authentication
            .Setup(resolver => resolver.ResolveAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AuthenticationException(AuthenticationFailureReason.ServiceError, "No provider."));

        await Execute();

        PipelineVariables().ShouldNotContain(variable => variable.StartsWith("STORYTELLER_TOKEN_URL"));
        PipelineVariables().ShouldContain("STORYTELLER_CLIENT_ID=client_01HXYZ");
    }

    [Theory]
    [InlineData(MachineAccessCredentialKind.Certificate, false)]
    [InlineData(MachineAccessCredentialKind.CertificateAndApiKey, true)]
    public async Task Certificate_PointsAtTheWrittenFile(MachineAccessCredentialKind kind, bool hasKey)
    {
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>(), MockUnixSupport.Path(@"C:\work"));
        Returns(new MachineAccess
        {
            Id = "m1",
            AccessKey = hasKey ? "2s.top.secret" : string.Empty,
            CredentialKind = kind,
            Certificate = Convert.ToBase64String([1, 2, 3]),
            CertificatePassword = "pfx-password",
        });

        await Execute(fileSystem: fileSystem);

        var file = MockUnixSupport.Path(@"C:\work\m1.pfx");
        fileSystem.File.Exists(file).ShouldBeTrue();
        PipelineVariables().ShouldBe(
            new[] { $"STORYTELLER_API_URL={ApiUrl}" }
                .Concat(hasKey ? ["STORYTELLER_API_KEY=<access key above>"] : [])
                .Concat([$"STORYTELLER_CERTIFICATE_FILE={file}", "STORYTELLER_CERTIFICATE_PASSWORD=<certificate password above>"]));
        _console.Lines.ShouldNotContain(line => line.StartsWith("  STORYTELLER_") && line.Contains("pfx-password"));
    }

    [Fact]
    public async Task SharedCertificate_PointsAtTheSharedFile()
    {
        Returns(new MachineAccess { Id = "m1", AccessKey = "2s.top.secret", CredentialKind = MachineAccessCredentialKind.CertificateAndApiKey, UsesSharedCertificate = true });

        await Execute(useSharedCertificate: true);

        PipelineVariables().ShouldBe(
        [
            $"STORYTELLER_API_URL={ApiUrl}",
            "STORYTELLER_API_KEY=<access key above>",
            "STORYTELLER_CERTIFICATE_FILE=<shared certificate .pfx>",
            "STORYTELLER_CERTIFICATE_PASSWORD=<its password>",
        ]);
    }

    [Fact]
    public async Task WithoutAScopeOption_SendsTheReadScopes()
    {
        var bodies = CapturesTheBodies();

        var exitCode = await Execute();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        bodies.ShouldHaveSingleItem().Scopes.ShouldBe([MachineScope.AnnotationRead, MachineScope.ConfigurationRead]);
        Newtonsoft.Json.JsonConvert.SerializeObject(bodies[0]).ShouldContain("\"Scopes\":[\"AnnotationRead\",\"ConfigurationRead\"]");
    }

    [Fact]
    public async Task ReadOnly_SendsTheReadScopes()
    {
        var bodies = CapturesTheBodies();

        await Execute(readOnly: true);

        bodies.ShouldHaveSingleItem().Scopes.ShouldBe([MachineScope.AnnotationRead, MachineScope.ConfigurationRead]);
    }

    [Fact]
    public async Task Write_SendsTheReadAndWriteScopesOfAnnotationsAndConfigurations()
    {
        var bodies = CapturesTheBodies();

        await Execute(write: true);

        bodies.ShouldHaveSingleItem().Scopes.ShouldBe(
            [MachineScope.AnnotationRead, MachineScope.AnnotationWrite, MachineScope.ConfigurationRead, MachineScope.ConfigurationWrite]);
    }

    [Theory]
    [InlineData("AnnotationRead", MachineScope.AnnotationRead)]
    [InlineData("annotation.read", MachineScope.AnnotationRead)]
    [InlineData("Annotation.Write", MachineScope.AnnotationWrite)]
    [InlineData("annotation-write", MachineScope.AnnotationWrite)]
    [InlineData("configuration-read", MachineScope.ConfigurationRead)]
    [InlineData("ConfigurationWrite", MachineScope.ConfigurationWrite)]
    [InlineData("SecretsRead", MachineScope.SecretsRead)]
    [InlineData("Secrets.Read", MachineScope.SecretsRead)]
    [InlineData("secrets-read", MachineScope.SecretsRead)]
    [InlineData("SECRETS.READ", MachineScope.SecretsRead)]
    [InlineData(" secretsread ", MachineScope.SecretsRead)]
    [InlineData("secrets.write", MachineScope.SecretsWrite)]
    public async Task RuntimeScope_InEverySpelling_IsSentAloneByItsName(string value, MachineScope expected)
    {
        var bodies = CapturesTheBodies();

        var exitCode = await Execute(scopes: [value]);

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        bodies.ShouldHaveSingleItem().Scopes.ShouldBe([expected]);
        Newtonsoft.Json.JsonConvert.SerializeObject(bodies[0]).ShouldContain($"\"Scopes\":[\"{expected}\"]");
    }

    // Through --scope or -m alike; without a runtime scope the machine reads, as it did before --scope.
    [Theory]
    [InlineData("ConfigurationTransfer", MachineScope.ConfigurationTransfer)]
    [InlineData("Configuration.Transfer", MachineScope.ConfigurationTransfer)]
    [InlineData("configuration-transfer", MachineScope.ConfigurationTransfer)]
    [InlineData("CONFIGURATION.TRANSFER", MachineScope.ConfigurationTransfer)]
    [InlineData(" configurationtransfer ", MachineScope.ConfigurationTransfer)]
    [InlineData("schema.write", MachineScope.SchemaWrite)]
    [InlineData("template-write", MachineScope.TemplateWrite)]
    [InlineData("Annotation.Manage", MachineScope.AnnotationManage)]
    [InlineData("KeyValue.Manage", MachineScope.KeyValueManage)]
    [InlineData("key-value-manage", MachineScope.KeyValueManage)]
    [InlineData("keyvalue-manage", MachineScope.KeyValueManage)]
    public async Task ManagementScope_InEverySpelling_IsSentWithTheReadScopes(string value, MachineScope expected)
    {
        var bodies = CapturesTheBodies();

        (await Execute(scopes: [value])).ShouldBe(ExitCodes.SUCCESS);
        (await Execute(managementScopes: [value])).ShouldBe(ExitCodes.SUCCESS);

        bodies.Count.ShouldBe(2);
        bodies.ShouldAllBe(body => body.Scopes.SequenceEqual(new[] { MachineScope.AnnotationRead, MachineScope.ConfigurationRead, expected }));
    }

    [Fact]
    public async Task Scopes_RepeatedOrCommaSeparated_AreSentOnceInEnumOrder()
    {
        var bodies = CapturesTheBodies();

        var exitCode = await Execute(scopes: ["key-value-manage, Secrets.Write", "ConfigurationRead,,", "secretswrite", "Annotation.Read"]);

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        bodies.ShouldHaveSingleItem().Scopes.ShouldBe(
            [MachineScope.AnnotationRead, MachineScope.ConfigurationRead, MachineScope.SecretsWrite, MachineScope.KeyValueManage]);
    }

    [Fact]
    public async Task RuntimeScope_WithManagementScopes_ReplacesTheReadScopes()
    {
        var bodies = CapturesTheBodies();

        await Execute(scopes: ["configuration-write"], managementScopes: ["schema-write", "TemplateWrite"]);

        // The server adds Configuration.Read for Configuration.Write.
        bodies.ShouldHaveSingleItem().Scopes.ShouldBe([MachineScope.ConfigurationWrite, MachineScope.SchemaWrite, MachineScope.TemplateWrite]);
    }

    [Fact]
    public async Task EveryOption_IsSentAsOneUnionInEnumOrder()
    {
        var bodies = CapturesTheBodies();

        await Execute(readOnly: true, write: true, scopes: ["KeyValueManage", "Secrets.Read", "annotation-read"], managementScopes: ["SchemaWrite", "Configuration.Transfer"]);

        bodies.ShouldHaveSingleItem().Scopes.ShouldBe(
        [
            MachineScope.AnnotationRead,
            MachineScope.AnnotationWrite,
            MachineScope.ConfigurationRead,
            MachineScope.ConfigurationWrite,
            MachineScope.SecretsRead,
            MachineScope.ConfigurationTransfer,
            MachineScope.SchemaWrite,
            MachineScope.KeyValueManage,
        ]);
    }

    [Fact]
    public async Task ScopeOptions_NamingNoScope_SendTheReadScopes()
    {
        var bodies = CapturesTheBodies();

        var exitCode = await Execute(scopes: [",", " "], managementScopes: [","]);

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        bodies.ShouldHaveSingleItem().Scopes.ShouldBe([MachineScope.AnnotationRead, MachineScope.ConfigurationRead]);
    }

    [Theory]
    [InlineData("read", "read")]
    [InlineData("Secrets.Read,Secrets.Delete", "Secrets.Delete")]
    [InlineData("0", "0")]
    [InlineData("AnnotationRead;ConfigurationRead", "AnnotationRead;ConfigurationRead")]
    [InlineData("Default.ReadWrite", "Default.ReadWrite")]
    [InlineData("Annotation.ReadWrite", "Annotation.ReadWrite")]
    [InlineData("Configuration.Secrets", "Configuration.Secrets")]
    [InlineData("User.Impersonation", "User.Impersonation")]
    public async Task UnknownScope_NamesTheScopesAndFailsWithoutCallingTheServer(string value, string unknown)
    {
        var exitCode = await Execute(scopes: [value]);

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _console.Lines.ShouldContain(
            $"! Unknown machine scope '{unknown}'. Use AnnotationRead, AnnotationWrite, ConfigurationRead, ConfigurationWrite, SecretsRead, SecretsWrite,"
            + " ConfigurationTransfer, SchemaWrite, TemplateWrite, AnnotationManage or KeyValueManage.");
        _accessApi.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("transfer", "transfer")]
    [InlineData("SchemaWrite,Schema.Read", "Schema.Read")]
    [InlineData("0", "0")]
    [InlineData("ConfigurationTransfer;SchemaWrite", "ConfigurationTransfer;SchemaWrite")]
    [InlineData("Default.ReadWrite", "Default.ReadWrite")]
    public async Task UnknownManagementScope_NamesTheScopesAndFailsWithoutCallingTheServer(string value, string unknown)
    {
        var exitCode = await Execute(managementScopes: [value]);

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _console.Lines.ShouldContain(
            $"! Unknown management scope '{unknown}'. Use ConfigurationTransfer, SchemaWrite, TemplateWrite, AnnotationManage or KeyValueManage.");
        _accessApi.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("Secrets.Read", "Secrets.Read")]
    [InlineData("annotation-write", "Annotation.Write")]
    [InlineData("SchemaWrite,ConfigurationRead", "Configuration.Read")]
    public async Task RuntimeScopeAsManagementScope_PointsAtScopeAndFailsWithoutCallingTheServer(string value, string scope)
    {
        var exitCode = await Execute(managementScopes: [value]);

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _console.Lines.ShouldContain(
            $"! {scope} is not a management scope; give it with --scope. --manage takes ConfigurationTransfer, SchemaWrite, TemplateWrite, AnnotationManage or KeyValueManage.");
        _accessApi.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ScopeOptions_ParsedFromTheArguments_AreSent()
    {
        var bodies = CapturesTheBodies();
        var app = Application();

        var exitCode = await app.ExecuteAsync(
            ["--scope", "secrets-read,Configuration.Write", "--scope", "AnnotationRead", "-m", "configuration-transfer,Template.Write", "--manage", "AnnotationManage", "-w"]);

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        app.Model.Scope.ShouldBe(["secrets-read,Configuration.Write", "AnnotationRead"]);
        app.Model.ManagementScope.ShouldBe(["configuration-transfer,Template.Write", "AnnotationManage"]);
        app.Model.IsScopeWrite.ShouldBeTrue();
        bodies.ShouldHaveSingleItem().Scopes.ShouldBe(
        [
            MachineScope.AnnotationRead,
            MachineScope.AnnotationWrite,
            MachineScope.ConfigurationRead,
            MachineScope.ConfigurationWrite,
            MachineScope.SecretsRead,
            MachineScope.ConfigurationTransfer,
            MachineScope.TemplateWrite,
            MachineScope.AnnotationManage,
        ]);
    }

    [Theory]
    [InlineData("-r")]
    [InlineData("--read-only")]
    public async Task ReadOnlyOption_ParsedFromTheArguments_AddsTheReadScopes(string option)
    {
        var bodies = CapturesTheBodies();
        var app = Application();

        await app.ExecuteAsync([option, "--scope", "secrets.read"]);

        app.Model.IsScopeReadOnly.ShouldBeTrue();
        bodies.ShouldHaveSingleItem().Scopes.ShouldBe([MachineScope.AnnotationRead, MachineScope.ConfigurationRead, MachineScope.SecretsRead]);
    }

    [Fact]
    public void ScopeOptions_AreMissingByDefault()
    {
        var app = Application();

        app.Parse();

        app.Model.IsScopeReadOnly.ShouldBeFalse();
        app.Model.IsScopeWrite.ShouldBeFalse();
        app.Model.Scope.ShouldBeNull();
        app.Model.ManagementScope.ShouldBeNull();
    }

    [Fact]
    public void ScopeHelp_NamesEveryScopeItsSpellingsAndTheRoles()
    {
        var option = OptionOf(nameof(MachineCreateCommand.Scope));

        option.Template.ShouldBe("--scope");
        option.OptionType.ShouldBe(CommandOptionType.MultipleValue);
        option.Description.ShouldBe(
            "A scope to grant: AnnotationRead, AnnotationWrite, ConfigurationRead, ConfigurationWrite, SecretsRead, SecretsWrite,"
            + " ConfigurationTransfer, SchemaWrite, TemplateWrite, AnnotationManage or KeyValueManage,"
            + " also as the scope string (Secrets.Read) or in kebab case (secrets-read)."
            + " Repeat it or separate the values with commas; it adds to -r and -w. The server adds the read scope of a write scope."
            + " A secrets scope needs the ContributorWithSecrets role, a management scope the Administrator role.");
    }

    [Fact]
    public void ReadOnlyAndWriteHelp_NameTheirScopes()
    {
        var readOnly = OptionOf(nameof(MachineCreateCommand.IsScopeReadOnly));
        var write = OptionOf(nameof(MachineCreateCommand.IsScopeWrite));

        readOnly.Template.ShouldBe("-r|--read-only");
        readOnly.Description.ShouldBe(
            "Read annotations and configurations: AnnotationRead and ConfigurationRead. The default when no other option gives a runtime scope.");
        write.Template.ShouldBe("-w|--write");
        write.Description.ShouldBe(
            "Read and write annotations and configurations: AnnotationRead, AnnotationWrite, ConfigurationRead and ConfigurationWrite.");
    }

    [Fact]
    public void ManagementScopeHelp_NamesTheScopesAndTheAdministratorRole()
    {
        var option = OptionOf(nameof(MachineCreateCommand.ManagementScope));

        option.Template.ShouldBe("-m|--manage");
        option.OptionType.ShouldBe(CommandOptionType.MultipleValue);
        option.Description.ShouldBe(
            "A management scope to grant: ConfigurationTransfer, SchemaWrite, TemplateWrite, AnnotationManage or KeyValueManage."
            + " Repeat it or separate the values with commas. Needs the Administrator role; never part of -r or -w.");
    }

    [Fact]
    public void PrintedMachine_NamesItsScopesAndItsCredentialKind()
    {
        var printed = MachineCreateCommand.Describe(new MachineAccess
        {
            Id = "m1",
            Scopes = [MachineScope.AnnotationRead, MachineScope.SecretsRead, MachineScope.KeyValueManage],
            CredentialKind = MachineAccessCredentialKind.CertificateAndApiKey,
        });

        // Without the SDK's StringEnumConverter, as sform's console prints it.
        var json = Newtonsoft.Json.JsonConvert.SerializeObject(printed);

        json.ShouldContain("\"Scopes\":[\"AnnotationRead\",\"SecretsRead\",\"KeyValueManage\"]");
        json.ShouldContain("\"CredentialKind\":\"CertificateAndApiKey\"");
    }

    [Fact]
    public void PrintedMachine_WithoutScopes_LeavesTheMemberOut()
    {
        var printed = MachineCreateCommand.Describe(new MachineAccess { Id = "m1" });

        // As sform's default settings print it (Startup.ConfigureApplication).
        Newtonsoft.Json.JsonConvert.SerializeObject(printed, new Newtonsoft.Json.JsonSerializerSettings { NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore })
            .ShouldNotContain("Scopes");
    }

    private static OptionAttribute OptionOf(string property)
    {
        return typeof(MachineCreateCommand).GetProperty(property)!.GetCustomAttribute<OptionAttribute>()!;
    }

    private List<string> PipelineVariables()
    {
        return _console.Lines
            .SkipWhile(line => line != PipelineHeader)
            .Skip(1)
            .Select(line => line.Trim())
            .ToList();
    }

    private void Returns(MachineAccess machine)
    {
        _accessApi
            .Setup(api => api.CreateMachineAccessAsync("org1", "proj1", It.IsAny<MachineAccessCreate>()))
            .ReturnsAsync(machine);
    }

    private string Line(string start)
    {
        return _console.Lines.Single(line => line.TrimStart().StartsWith(start));
    }

    // The bodies of the create requests; the server answers with an API key machine.
    private List<MachineAccessCreate> CapturesTheBodies()
    {
        var bodies = new List<MachineAccessCreate>();
        _accessApi
            .Setup(api => api.CreateMachineAccessAsync("org1", "proj1", It.IsAny<MachineAccessCreate>()))
            .Callback((string _, string _, MachineAccessCreate body) => bodies.Add(body))
            .ReturnsAsync(new MachineAccess { Id = "m1", AccessKey = "api-key", CredentialKind = MachineAccessCredentialKind.ApiKey });
        return bodies;
    }

    private Task<int> Execute(
        string? credentialKind = null,
        int? lifetimeDays = null,
        bool useSharedCertificate = false,
        IFileSystem? fileSystem = null,
        string[]? managementScopes = null,
        string[]? scopes = null,
        bool readOnly = false,
        bool write = false)
    {
        var command = new MachineCreateCommand(
            _console.Object,
            _context.Object,
            _accessApi.Object,
            fileSystem ?? Mock.Of<IFileSystem>(),
            _authentication.Object,
            Options.Create(new GeneralOptions { BaseUrl = ApiUrl, UserAgent = "test" }))
        {
            CredentialKind = credentialKind,
            LifetimeDays = lifetimeDays,
            UseSharedCertificate = useSharedCertificate,
            ManagementScope = managementScopes,
            Scope = scopes,
            IsScopeReadOnly = readOnly,
            IsScopeWrite = write,
        };

        return command.OnExecuteAsync();
    }

    // The command as sform builds it, so the options are parsed from the arguments.
    private CommandLineApplication<MachineCreateCommand> Application()
    {
        var services = new ServiceCollection()
            .AddSingleton(_console.Object)
            .AddSingleton(_context.Object)
            .AddSingleton(_accessApi.Object)
            .AddSingleton(Mock.Of<IFileSystem>())
            .AddSingleton(_authentication.Object)
            .AddSingleton(Options.Create(new GeneralOptions { BaseUrl = ApiUrl, UserAgent = "test" }))
            .BuildServiceProvider();
        var app = new CommandLineApplication<MachineCreateCommand>();
        app.Conventions.UseDefaultConventions().UseConstructorInjection(services);
        return app;
    }
}

using System.IO.Abstractions;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Cli.Authentication;
using _42.Platform.Cli.Commands;
using _42.Platform.Cli.Commands.MachineAccess;
using _42.Platform.Storyteller.Sdk;
using Moq;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Commands;

public class MachineCreateCommandTests
{
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

    private Task<int> Execute(string? credentialKind = null, int? lifetimeDays = null, bool useSharedCertificate = false)
    {
        var command = new MachineCreateCommand(
            _console.Object,
            _context.Object,
            _accessApi.Object,
            Mock.Of<IFileSystem>(),
            _authentication.Object)
        {
            CredentialKind = credentialKind,
            LifetimeDays = lifetimeDays,
            UseSharedCertificate = useSharedCertificate,
        };

        return command.OnExecuteAsync();
    }
}

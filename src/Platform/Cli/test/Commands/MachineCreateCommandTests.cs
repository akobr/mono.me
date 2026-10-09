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

    private Task<int> Execute()
    {
        var command = new MachineCreateCommand(
            _console.Object,
            _context.Object,
            _accessApi.Object,
            Mock.Of<IFileSystem>(),
            _authentication.Object);

        return command.OnExecuteAsync();
    }
}

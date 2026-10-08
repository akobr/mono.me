using System.Threading.Tasks;
using _42.Platform.Cli.Commands;
using _42.Platform.Cli.Commands.MachineAccess;
using _42.Platform.Storyteller.Sdk;
using Moq;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Commands;

public class MachineAuthSetCommandTests
{
    private readonly RecordingConsole _console = new();
    private readonly Mock<IAccessApiClient> _accessApi = new();
    private readonly Mock<ICommandContext> _context = new();

    public MachineAuthSetCommandTests()
    {
        _context.SetupGet(context => context.OrganizationName).Returns("org1");
        _context.SetupGet(context => context.ProjectName).Returns("proj1");
        _accessApi
            .Setup(api => api.SetMachineAuthenticationAsync(It.IsAny<string>(), It.IsAny<MachineAuthenticationPolicy>()))
            .ReturnsAsync((string _, MachineAuthenticationPolicy policy) => policy);
    }

    [Theory]
    [InlineData("client-credentials")]
    [InlineData("ClientCredentials")]
    [InlineData("clientcredentials")]
    public async Task ClientCredentialsSpellings_SetTheClientCredentialsPolicy(string value)
    {
        var exitCode = await Execute(value);

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _accessApi.Verify(
            api => api.SetMachineAuthenticationAsync(
                "org1.proj1",
                It.Is<MachineAuthenticationPolicy>(policy => policy.CredentialKind == MachineAuthenticationPolicyCredentialKind.ClientCredentials)),
            Times.Once);
    }

    [Fact]
    public async Task KebabCaseOfAnotherKind_IsParsedToo()
    {
        await Execute("certificate-and-api-key");

        _accessApi.Verify(
            api => api.SetMachineAuthenticationAsync(
                "org1.proj1",
                It.Is<MachineAuthenticationPolicy>(policy => policy.CredentialKind == MachineAuthenticationPolicyCredentialKind.CertificateAndApiKey)),
            Times.Once);
    }

    // Enum.TryParse accepts any number; only the named kinds may reach the server.
    [Theory]
    [InlineData("7")]
    [InlineData("42")]
    [InlineData("oauth")]
    public async Task UnknownKind_FailsWithoutCallingTheServer(string value)
    {
        var exitCode = await Execute(value);

        exitCode.ShouldBe(ExitCodes.ERROR_INPUT_PARSING);
        _accessApi.VerifyNoOtherCalls();
    }

    private Task<int> Execute(string credentialKind)
    {
        var command = new MachineAuthSetCommand(_console.Object, _context.Object, _accessApi.Object)
        {
            CredentialKind = credentialKind,
        };

        return command.OnExecuteAsync();
    }
}

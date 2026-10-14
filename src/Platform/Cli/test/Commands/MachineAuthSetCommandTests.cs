using System.Linq;
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
    private MachineAuthenticationPolicy? _sent;

    public MachineAuthSetCommandTests()
    {
        _context.SetupGet(context => context.OrganizationName).Returns("org1");
        _context.SetupGet(context => context.ProjectName).Returns("proj1");
        _accessApi
            .Setup(api => api.SetMachineAuthenticationAsync(It.IsAny<string>(), It.IsAny<MachineAuthenticationPolicy>()))
            .Callback((string _, MachineAuthenticationPolicy policy) => _sent = policy)
            .ReturnsAsync((string _, MachineAuthenticationPolicy policy) => policy);
    }

    [Theory]
    [InlineData("client-credentials")]
    [InlineData("ClientCredentials")]
    [InlineData("clientcredentials")]
    public async Task ClientCredentialsSpellings_EnableTheClientCredentialsKind(string value)
    {
        var exitCode = await Execute([value]);

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _accessApi.Verify(api => api.SetMachineAuthenticationAsync("org1.proj1", It.IsAny<MachineAuthenticationPolicy>()), Times.Once);
        _sent!.EnabledCredentialKinds.ShouldBe([MachineCredentialKind.ClientCredentials]);
        _sent.CredentialKind.ShouldBe(MachineAuthenticationPolicyCredentialKind.ClientCredentials);
    }

    [Fact]
    public async Task KebabCaseOfAnotherKind_IsParsedToo()
    {
        await Execute(["certificate-and-api-key"]);

        _sent!.EnabledCredentialKinds.ShouldBe([MachineCredentialKind.CertificateAndApiKey]);
    }

    [Fact]
    public async Task SeveralKinds_AreEnabledAndTheFirstIsTheDefault()
    {
        await Execute(["certificate", "api-key,client-credentials", "certificate"], lifetimeDays: 90);

        _sent!.EnabledCredentialKinds.ShouldBe([MachineCredentialKind.Certificate, MachineCredentialKind.ApiKey, MachineCredentialKind.ClientCredentials]);
        _sent.CredentialKind.ShouldBe(MachineAuthenticationPolicyCredentialKind.Certificate);
        _sent.CertificateLifetimeDays.ShouldBe(90);
        Line("Enabled kinds:").ShouldEndWith("Certificate, ApiKey, ClientCredentials");
        Line("Certificate lifetime:").ShouldEndWith("90 days");
    }

    [Fact]
    public async Task DefaultKind_PicksTheDefaultAmongTheEnabledKinds()
    {
        await Execute(["api-key", "certificate"], defaultKind: "certificate");

        _sent!.CredentialKind.ShouldBe(MachineAuthenticationPolicyCredentialKind.Certificate);
        _sent.CertificateLifetimeDays.ShouldBeNull();
    }

    [Theory]
    [InlineData("client-credentials")]
    [InlineData("oauth")]
    public async Task DefaultKindThatIsNotEnabled_FailsWithoutCallingTheServer(string defaultKind)
    {
        var exitCode = await Execute(["api-key"], defaultKind: defaultKind);

        exitCode.ShouldBe(ExitCodes.ERROR_INPUT_PARSING);
        _accessApi.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DefaultKindOrLifetimeWithoutKinds_FailsWithoutCallingTheServer()
    {
        (await Execute(null, lifetimeDays: 30)).ShouldBe(ExitCodes.ERROR_INPUT_PARSING);
        (await Execute(null, defaultKind: "api-key")).ShouldBe(ExitCodes.ERROR_INPUT_PARSING);
        _accessApi.VerifyNoOtherCalls();
    }

    // Enum.TryParse accepts any number; only the named kinds may reach the server.
    [Theory]
    [InlineData("7")]
    [InlineData("42")]
    [InlineData("oauth")]
    public async Task UnknownKind_FailsWithoutCallingTheServer(string value)
    {
        var exitCode = await Execute(["api-key", value]);

        exitCode.ShouldBe(ExitCodes.ERROR_INPUT_PARSING);
        _accessApi.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task WithoutOptions_PrintsTheCurrentPolicy()
    {
        _accessApi
            .Setup(api => api.GetMachineAuthenticationAsync("org1.proj1"))
            .ReturnsAsync(new MachineAuthenticationPolicy
            {
                EnabledCredentialKinds = [MachineCredentialKind.ApiKey, MachineCredentialKind.Certificate],
                CredentialKind = MachineAuthenticationPolicyCredentialKind.ApiKey,
                CertificateLifetimeDays = 365,
            });

        var exitCode = await Execute(null);

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        Line("Enabled kinds:").ShouldEndWith("ApiKey, Certificate");
        Line("Default kind:").ShouldEndWith("ApiKey");
        Line("Certificate lifetime:").ShouldEndWith("365 days");
        _accessApi.Verify(api => api.SetMachineAuthenticationAsync(It.IsAny<string>(), It.IsAny<MachineAuthenticationPolicy>()), Times.Never);
    }

    private string Line(string start)
    {
        return _console.Lines.Single(line => line.TrimStart().StartsWith(start));
    }

    private Task<int> Execute(string[]? credentialKinds, string? defaultKind = null, int? lifetimeDays = null)
    {
        var command = new MachineAuthSetCommand(_console.Object, _context.Object, _accessApi.Object)
        {
            CredentialKind = credentialKinds,
            DefaultKind = defaultKind,
            CertificateLifetimeDays = lifetimeDays,
        };

        return command.OnExecuteAsync();
    }
}

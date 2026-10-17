using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Cli.Authentication;
using _42.Platform.Cli.Commands;
using _42.Platform.Cli.Commands.MachineAccess;
using _42.Platform.Cli.Configuration;
using _42.Platform.Storyteller.Sdk;
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

    private Task<int> Execute(string? credentialKind = null, int? lifetimeDays = null, bool useSharedCertificate = false, IFileSystem? fileSystem = null)
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
        };

        return command.OnExecuteAsync();
    }
}

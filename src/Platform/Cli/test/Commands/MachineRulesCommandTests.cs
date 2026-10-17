using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using _42.Platform.Cli.Commands;
using _42.Platform.Cli.Commands.MachineAccess;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;
using Moq;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Commands;

public class MachineRulesCommandTests
{
    private const string MachineId = "3f1c2a9e-5b7d-4c11-9e0a-2d4f6b8c1a07";
    private const string TokenEndpoint = "https://api.example.com/api/v1/org1/proj1/access/token";

    private static readonly string RulesPath = MockUnixSupport.Path(@"C:\work\rules.json");

    private readonly RecordingConsole _console = new();
    private readonly Mock<IAccessApiClient> _accessApi = new();
    private readonly Mock<ICommandContext> _context = new();
    private readonly MockFileSystem _fileSystem = new(new Dictionary<string, MockFileData>(), MockUnixSupport.Path(@"C:\work"));
    private List<FederationRule>? _sent;

    public MachineRulesCommandTests()
    {
        _context.SetupGet(context => context.OrganizationName).Returns("org1");
        _context.SetupGet(context => context.ProjectName).Returns("proj1");
        _accessApi
            .Setup(api => api.SetMachineFederationRulesAsync("org1", "proj1", MachineId, It.IsAny<IEnumerable<FederationRule>>()))
            .Callback((string _, string _, string _, IEnumerable<FederationRule> rules) => _sent = rules.ToList())
            .ReturnsAsync((string _, string _, string _, IEnumerable<FederationRule> rules) => Federated(rules.ToList()));
    }

    [Fact]
    public async Task RuleOptions_ReplaceTheRulesWithOneRule()
    {
        var exitCode = await Execute(issuer: "github", audience: TokenEndpoint, subject: "repo:acme/app:environment:production", claims: ["repository_id=74"]);

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        var rule = _sent.ShouldHaveSingleItem();
        rule.Issuer.ShouldBe("github");
        rule.Audience.ShouldBe(TokenEndpoint);
        rule.Subject.ShouldBe("repo:acme/app:environment:production");
        rule.Claims["repository_id"].ShouldBe(["74"]);
        _console.Lines.ShouldContain($"! The federation rules of {MachineId} have been replaced; they apply to new token exchanges.");
        Line("Token endpoint:").ShouldEndWith(TokenEndpoint);
        Line("Claim:").Trim().ShouldBe("Claim:    repository_id=74");
    }

    [Fact]
    public async Task RulesFile_ReplacesTheRulesWithTheFile()
    {
        _fileSystem.AddFile(RulesPath, new MockFileData(
            """
            [ { "Issuer": "aks-prod", "Audience": "aud-1", "Subject": "system:serviceaccount:billing:*", "Claims": { "/kubernetes.io/namespace": [ "billing" ] } },
              { "Issuer": "vercel", "Audience": "aud-2", "Subject": "owner:acme:project:web:environment:production", "Claims": { "owner_id": [ "team_1" ] } } ]
            """));

        var exitCode = await Execute(rulesFile: RulesPath);

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _sent!.Select(rule => rule.Issuer).ShouldBe(["aks-prod", "vercel"]);
        Line("Rule 2:").ShouldBe("Rule 2:");
    }

    [Fact]
    public async Task RulesFile_WithAMisspelledProperty_FailsWithoutCallingTheServer()
    {
        _fileSystem.AddFile(RulesPath, new MockFileData(
            """[ { "Issuer": "aks-prod", "Audience": "aud-1", "Subject": "system:serviceaccount:prod:app", "Claim": { "/kubernetes.io/serviceaccount/uid": [ "9f1c" ] } } ]"""));

        var exitCode = await Execute(rulesFile: RulesPath);

        exitCode.ShouldBe(ExitCodes.ERROR_INPUT_PARSING);
        _console.Lines.Single(line => line.StartsWith("! ")).ShouldContain("Could not find member 'Claim' on object of type 'FederationRule'.");
        _accessApi.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RulesFile_WithAClaimValueThatIsNoString_FailsWithoutCallingTheServer()
    {
        _fileSystem.AddFile(RulesPath, new MockFileData(
            """[ { "Issuer": "github", "Audience": "aud-1", "Subject": "repo:acme/app:*", "Claims": { "some_flag": [ true ] } } ]"""));

        var exitCode = await Execute(rulesFile: RulesPath);

        exitCode.ShouldBe(ExitCodes.ERROR_INPUT_PARSING);
        _console.Lines.ShouldContain($"! Rule 1 of the rules file {RulesPath}: the claim some_flag takes an array of strings, for example [ \"74\" ], not [true].");
        _accessApi.VerifyNoOtherCalls();
    }

    [Fact]
    public void AudienceHelp_GivesTheAzureAudienceOfBothTokenVersions()
    {
        var description = typeof(MachineRulesCommand)
            .GetProperty(nameof(MachineRulesCommand.FederationAudience))!
            .GetCustomAttribute<OptionAttribute>()!
            .Description!;

        description.ShouldContain("For Azure, the aud of tokens for the Storyteller app registration: <appId> with requestedAccessTokenVersion 2, api://<appId> with 1.");
        description.ShouldNotContain("app registration's URI");
    }

    [Fact]
    public async Task WithoutRuleOptions_ShowsTheCurrentRules()
    {
        _accessApi
            .Setup(api => api.GetMachineAccessAsync("org1", "proj1", MachineId))
            .ReturnsAsync(Federated([new FederationRule { Issuer = "github", Audience = TokenEndpoint, Subject = "repo:acme/app:*", Claims = Claims("repository_id", "74") }]));

        var exitCode = await Execute();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        Line("Machine ID:").ShouldEndWith(MachineId);
        Line("Subject:").Trim().ShouldBe("Subject:  repo:acme/app:*");
        _accessApi.Verify(api => api.SetMachineFederationRulesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<FederationRule>>()), Times.Never);
    }

    [Fact]
    public async Task WithoutRuleOptions_OfAnotherKind_SaysOnlyFederatedMachinesHaveRules()
    {
        _accessApi
            .Setup(api => api.GetMachineAccessAsync("org1", "proj1", MachineId))
            .ReturnsAsync(new MachineAccess { Id = MachineId, CredentialKind = MachineAccessCredentialKind.ApiKey });

        var exitCode = await Execute();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _console.Lines.ShouldContain($"! The machine {MachineId} is ApiKey; only federated machines have federation rules.");
    }

    [Fact]
    public async Task UnknownMachine_IsWrongInput()
    {
        _accessApi
            .Setup(api => api.SetMachineFederationRulesAsync("org1", "proj1", MachineId, It.IsAny<IEnumerable<FederationRule>>()))
            .ThrowsAsync(AnnotationCommandFixture.Error(404));

        var exitCode = await Execute(issuer: "github", audience: TokenEndpoint, subject: "repo:acme/app:*");

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _console.Lines.ShouldContain($"! The machine access {MachineId} doesn't exist in org1.proj1.");
    }

    [Fact]
    public async Task MachineOfAnotherKind_PrintsTheServerMessage()
    {
        // The rules PUT declares its 400, so the client hands over an ErrorResponse instead of the body text.
        _accessApi
            .Setup(api => api.SetMachineFederationRulesAsync("org1", "proj1", MachineId, It.IsAny<IEnumerable<FederationRule>>()))
            .ThrowsAsync(AnnotationCommandFixture.Error(400, "MachineNotFederated", "Only Federated machines have federation rules."));

        var exitCode = await Execute(issuer: "github", audience: TokenEndpoint, subject: "repo:acme/app:*");

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _console.Lines.ShouldContain("! Only Federated machines have federation rules.");
        _console.Lines.ShouldContain("Error code: MachineNotFederated");
    }

    [Fact]
    public async Task IncompleteRule_FailsWithoutCallingTheServer()
    {
        var exitCode = await Execute(issuer: "github", subject: "repo:acme/app:*");

        exitCode.ShouldBe(ExitCodes.ERROR_INPUT_PARSING);
        _console.Lines.Single(line => line.StartsWith("! ")).ShouldEndWith("missing --audience.");
        _accessApi.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task MissingMachineId_FailsWithoutCallingTheServer()
    {
        var exitCode = await Execute(machineId: " ", issuer: "github", audience: TokenEndpoint, subject: "repo:acme/app:*");

        exitCode.ShouldBe(ExitCodes.ERROR_INPUT_PARSING);
        _accessApi.VerifyNoOtherCalls();
    }

    private static MachineAccess Federated(List<FederationRule> rules)
    {
        return new MachineAccess
        {
            Id = MachineId,
            ObjectId = MachineId,
            AccessKey = string.Empty,
            CredentialKind = MachineAccessCredentialKind.Federated,
            TokenEndpoint = TokenEndpoint,
            FederationRules = rules,
        };
    }

    private static Dictionary<string, IReadOnlyList_String> Claims(string key, params string[] values)
    {
        var accepted = new IReadOnlyList_String();

        foreach (var value in values)
        {
            accepted.Add(value);
        }

        return new Dictionary<string, IReadOnlyList_String> { [key] = accepted };
    }

    private string Line(string start)
    {
        return _console.Lines.Single(line => line.TrimStart().StartsWith(start));
    }

    private Task<int> Execute(
        string machineId = MachineId,
        string? issuer = null,
        string? audience = null,
        string? subject = null,
        string[]? claims = null,
        string? rulesFile = null)
    {
        var command = new MachineRulesCommand(_console.Object, _context.Object, _accessApi.Object, _fileSystem)
        {
            MachineId = machineId,
            FederationIssuer = issuer,
            FederationAudience = audience,
            FederationSubject = subject,
            FederationClaims = claims,
            RulesFile = rulesFile,
        };

        return command.OnExecuteAsync();
    }
}

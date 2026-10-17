using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using _42.Platform.Cli.Authentication;
using _42.Platform.Cli.Commands;
using _42.Platform.Cli.Commands.MachineAccess;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;
using Moq;
using Newtonsoft.Json;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Commands;

public class MachineCreateFederatedTests
{
    private const string MachineId = "3f1c2a9e-5b7d-4c11-9e0a-2d4f6b8c1a07";
    private const string TokenEndpoint = "https://api.example.com/api/v1/org1/proj1/access/token";

    private static readonly string RulesPath = MockUnixSupport.Path(@"C:\work\rules.json");

    private readonly RecordingConsole _console = new();
    private readonly Mock<IAccessApiClient> _accessApi = new();
    private readonly Mock<ICommandContext> _context = new();
    private readonly MockFileSystem _fileSystem = new(new Dictionary<string, MockFileData>(), MockUnixSupport.Path(@"C:\work"));
    private MachineAccessCreate? _sent;

    public MachineCreateFederatedTests()
    {
        _context.SetupGet(context => context.OrganizationName).Returns("org1");
        _context.SetupGet(context => context.ProjectName).Returns("proj1");
        _accessApi
            .Setup(api => api.CreateMachineAccessAsync("org1", "proj1", It.IsAny<MachineAccessCreate>()))
            .Callback((string _, string _, MachineAccessCreate body) => _sent = body)
            .ReturnsAsync((string _, string _, MachineAccessCreate body) => new MachineAccess
            {
                Id = MachineId,
                ObjectId = MachineId,
                AccessKey = string.Empty,
                CredentialKind = MachineAccessCredentialKind.Federated,
                TokenEndpoint = TokenEndpoint,
                FederationRules = body.FederationRules,
            });
    }

    [Fact]
    public async Task RuleOptions_WithoutAKind_CreateAFederatedMachineWithOneRule()
    {
        var exitCode = await Execute(
            issuer: "github",
            audience: TokenEndpoint,
            subject: "repo:acme/app:environment:production",
            claims: ["repository_id=74", "environment=production", "environment=staging", "environment=production", "/kubernetes.io/namespace=billing"]);

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _sent!.CredentialKind.ShouldBe(MachineAccessCreateCredentialKind.Federated);
        _sent.CertificateLifetimeDays.ShouldBeNull();
        _sent.UseSharedCertificate.ShouldBeNull();

        var rule = _sent.FederationRules.ShouldHaveSingleItem();
        rule.Issuer.ShouldBe("github");
        rule.Audience.ShouldBe(TokenEndpoint);
        rule.Subject.ShouldBe("repo:acme/app:environment:production");
        rule.Claims.Keys.ShouldBe(["repository_id", "environment", "/kubernetes.io/namespace"]);
        rule.Claims["repository_id"].ShouldBe(["74"]);
        rule.Claims["environment"].ShouldBe(["production", "staging"]);
        rule.Claims["/kubernetes.io/namespace"].ShouldBe(["billing"]);
    }

    [Fact]
    public async Task RequestBody_CarriesTheRulesInTheApiShape()
    {
        await Execute("federated", issuer: "github", audience: TokenEndpoint, subject: "repo:acme/app:ref:refs/heads/main", claims: ["repository_id=74"]);

        JsonConvert.SerializeObject(_sent).ShouldContain(
            "\"FederationRules\":[{\"Issuer\":\"github\",\"Audience\":\"" + TokenEndpoint + "\",\"Subject\":\"repo:acme/app:ref:refs/heads/main\",\"Claims\":{\"repository_id\":[\"74\"]}}]");
        JsonConvert.SerializeObject(_sent).ShouldContain("\"CredentialKind\":\"Federated\"");
    }

    [Fact]
    public async Task RuleWithoutClaims_SendsNoClaims()
    {
        await Execute(issuer: "google", audience: TokenEndpoint, subject: "107234789");

        _sent!.FederationRules.ShouldHaveSingleItem().Claims.ShouldBeNull();
        JsonConvert.SerializeObject(_sent).ShouldNotContain("Claims");
    }

    [Theory]
    [InlineData("federated")]
    [InlineData("Federated")]
    [InlineData("FEDERATED")]
    public async Task FederatedSpellings_AreParsed(string value)
    {
        var exitCode = await Execute(value, issuer: "github", audience: TokenEndpoint, subject: "repo:acme/app:*", claims: ["repository_id=74"]);

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _sent!.CredentialKind.ShouldBe(MachineAccessCreateCredentialKind.Federated);
    }

    [Fact]
    public async Task ClaimValue_MaySplitOnlyAtTheFirstEqualsSign()
    {
        await Execute(issuer: "aws", audience: TokenEndpoint, subject: "arn:aws:iam::123456789012:role/ci", claims: ["/https:~1~1sts.amazonaws.com~1/aws_account=a=b"]);

        _sent!.FederationRules.Single().Claims["/https:~1~1sts.amazonaws.com~1/aws_account"].ShouldBe(["a=b"]);
    }

    [Fact]
    public async Task RulesFile_SendsEveryRuleOfTheFile()
    {
        _fileSystem.AddFile(RulesPath, new MockFileData(
            """
            [
              { "Issuer": "aks-prod", "Audience": "https://api.example.com/api/v1/org1/proj1/access/token",
                "Subject": "system:serviceaccount:billing:*", "Claims": { "/kubernetes.io/namespace": [ "billing" ] } },
              { "issuer": "github", "audience": "https://api.example.com/api/v1/org1/proj1/access/token",
                "subject": "repo:acme/billing:environment:production", "claims": { "repository_id": [ "74", "75" ] } }
            ]
            """));

        var exitCode = await Execute("federated", rulesFile: RulesPath);

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _sent!.FederationRules.Count.ShouldBe(2);
        _sent.FederationRules.Select(rule => rule.Issuer).ShouldBe(["aks-prod", "github"]);
        _sent.FederationRules.First().Subject.ShouldBe("system:serviceaccount:billing:*");
        _sent.FederationRules.First().Claims["/kubernetes.io/namespace"].ShouldBe(["billing"]);
        _sent.FederationRules.Last().Claims["repository_id"].ShouldBe(["74", "75"]);
    }

    [Fact]
    public async Task Output_PrintsTheIdTheEndpointTheRulesAndAnExchange_AndNoSecret()
    {
        var exitCode = await Execute(issuer: "github", audience: TokenEndpoint, subject: "repo:acme/app:environment:production", claims: ["repository_id=74"]);

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        Line("Machine ID:").ShouldEndWith(MachineId);
        Line("Token endpoint:").ShouldEndWith(TokenEndpoint);
        Line("Rule 1:").ShouldBe("Rule 1:");
        Line("Issuer:").Trim().ShouldBe("Issuer:   github");
        Line("Audience:").Trim().ShouldBe($"Audience: {TokenEndpoint}");
        Line("Subject:").Trim().ShouldBe("Subject:  repo:acme/app:environment:production");
        Line("Claim:").Trim().ShouldBe("Claim:    repository_id=74");
        Line("curl").Trim().ShouldBe(
            $"curl -s -X POST {TokenEndpoint}"
            + " -d grant_type=urn:ietf:params:oauth:grant-type:token-exchange"
            + $" -d client_id={MachineId}"
            + " -d subject_token_type=urn:ietf:params:oauth:token-type:jwt"
            + " --data-urlencode subject_token=$PLATFORM_TOKEN");
        _console.Lines.ShouldContain(line => line.Contains("The machine has no secret"));
        _console.Lines.ShouldNotContain(line => line.Contains("copy the access key") || line.Contains("Client secret"));
    }

    [Fact]
    public async Task LifetimeAndOutput_AreIgnoredForAFederatedMachine()
    {
        var exitCode = await Execute(issuer: "github", audience: TokenEndpoint, subject: "repo:acme/app:*", claims: ["repository_id=74"], lifetimeDays: 30);

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _sent!.CertificateLifetimeDays.ShouldBeNull();
        _console.Lines.ShouldContain("A federated machine gets no certificate, so --lifetime-days and --output are ignored.");
    }

    [Fact]
    public async Task Federated_WithoutRules_FailsWithoutCallingTheServer()
    {
        var exitCode = await Execute("federated");

        exitCode.ShouldBe(ExitCodes.ERROR_INPUT_PARSING);
        Important().ShouldContain("A federated machine needs federation rules");
        _accessApi.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("api-key")]
    [InlineData("certificate")]
    [InlineData("client-credentials")]
    public async Task RuleOptions_WithAnotherKind_FailWithoutCallingTheServer(string kind)
    {
        var exitCode = await Execute(kind, issuer: "github", audience: TokenEndpoint, subject: "repo:acme/app:*");

        exitCode.ShouldBe(ExitCodes.ERROR_INPUT_PARSING);
        Important().ShouldContain("are only for federated machines");
        _accessApi.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RulesFile_WithAnotherKind_FailsWithoutCallingTheServer()
    {
        // A usable rule, so only the kind can refuse it.
        _fileSystem.AddFile(RulesPath, new MockFileData(
            """[ { "Issuer": "github", "Audience": "https://api.example.com/api/v1/org1/proj1/access/token", "Subject": "repo:acme/app:*" } ]"""));

        var exitCode = await Execute("api-key", rulesFile: RulesPath);

        exitCode.ShouldBe(ExitCodes.ERROR_INPUT_PARSING);
        Important().ShouldBe("! Federation rules (--issuer, --audience, --subject, --claim, --rules-file) are only for federated machines, not ApiKey.");
        _accessApi.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RuleOptions_WithASharedCertificate_FailWithoutCallingTheServer()
    {
        var exitCode = await Execute(issuer: "github", audience: TokenEndpoint, subject: "repo:acme/app:*", useSharedCertificate: true);

        exitCode.ShouldBe(ExitCodes.ERROR_INPUT_PARSING);
        Important().ShouldContain("not Federated");
        _accessApi.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("github", null, null, "missing --audience, --subject")]
    [InlineData(null, "aud", "sub", "missing --issuer")]
    [InlineData("github", "aud", " ", "missing --subject")]
    public async Task IncompleteRule_NamesTheMissingOptions(string? issuer, string? audience, string? subject, string expected)
    {
        var exitCode = await Execute(issuer: issuer, audience: audience, subject: subject);

        exitCode.ShouldBe(ExitCodes.ERROR_INPUT_PARSING);
        Important().ShouldEndWith(expected + ".");
        _accessApi.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ClaimAlone_IsAnIncompleteRule()
    {
        var exitCode = await Execute(claims: ["repository_id=74"]);

        exitCode.ShouldBe(ExitCodes.ERROR_INPUT_PARSING);
        Important().ShouldContain("missing --issuer, --audience, --subject");
        _accessApi.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("repository_id")]
    [InlineData("=74")]
    [InlineData("repository_id=")]
    [InlineData(" = ")]
    public async Task MalformedClaim_FailsWithoutCallingTheServer(string claim)
    {
        var exitCode = await Execute(issuer: "github", audience: TokenEndpoint, subject: "repo:acme/app:*", claims: [claim]);

        exitCode.ShouldBe(ExitCodes.ERROR_INPUT_PARSING);
        Important().ShouldContain($"--claim takes key=value, for example repository_id=74 or /kubernetes.io/namespace=billing, not '{claim}'.");
        _accessApi.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RulesFileAndRuleOptions_FailTogether()
    {
        _fileSystem.AddFile(RulesPath, new MockFileData("[]"));

        var exitCode = await Execute(issuer: "github", rulesFile: RulesPath);

        exitCode.ShouldBe(ExitCodes.ERROR_INPUT_PARSING);
        Important().ShouldContain("Give either --rules-file or --issuer");
        _accessApi.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("[]", "holds no rule")]
    [InlineData("{ \"Issuer\": \"github\" }", "has to hold a JSON array of rules")]
    [InlineData("[ \"github\" ]", "has to be a JSON object")]
    [InlineData("[ { \"Claims\": { \"repository_id\": \"74\" } } ]", "is not a JSON array of rules")]
    [InlineData("not json", "is not a JSON array of rules")]
    [InlineData("[ { \"Issuer\": \"github\" } ] x", "is not a JSON array of rules")]
    public async Task UnusableRulesFile_FailsWithoutCallingTheServer(string content, string expected)
    {
        _fileSystem.AddFile(RulesPath, new MockFileData(content));

        var exitCode = await Execute(rulesFile: RulesPath);

        exitCode.ShouldBe(ExitCodes.ERROR_INPUT_PARSING);
        Important().ShouldContain(expected);
        _accessApi.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("Claim")]
    [InlineData("Clams")]
    [InlineData("RequiredClaims")]
    public async Task RulesFile_WithAMisspelledProperty_FailsWithoutCallingTheServer(string property)
    {
        // The server would ignore the property, and the rule would lose its pin.
        _fileSystem.AddFile(RulesPath, new MockFileData(
            $$"""
            [ { "Issuer": "aks-prod", "Audience": "https://api.example.com/api/v1/org1/proj1/access/token",
                "Subject": "system:serviceaccount:prod:app", "{{property}}": { "/kubernetes.io/serviceaccount/uid": [ "9f1c" ] } } ]
            """));

        var exitCode = await Execute(rulesFile: RulesPath);

        exitCode.ShouldBe(ExitCodes.ERROR_INPUT_PARSING);
        Important().ShouldContain($"The rules file {RulesPath} is not a JSON array of rules: Could not find member '{property}' on object of type 'FederationRule'.");
        _accessApi.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("[ true ]", "the claim some_flag takes an array of strings, for example [ \"74\" ], not [true].")]
    [InlineData("[ 1.0 ]", "the claim some_flag takes an array of strings, for example [ \"74\" ], not [1.0].")]
    [InlineData("[ 74 ]", "the claim some_flag takes an array of strings, for example [ \"74\" ], not [74].")]
    [InlineData("[ \"74\", null ]", "the claim some_flag takes an array of strings, for example [ \"74\" ], not [\"74\",null].")]
    [InlineData("null", "the claim some_flag takes an array of strings, for example [ \"74\" ], not null.")]
    public async Task RulesFile_WithAClaimValueThatIsNoString_FailsWithoutCallingTheServer(string values, string expected)
    {
        // Newtonsoft would send true as "True" and 1.0 as "1", which never match the token's JSON text.
        _fileSystem.AddFile(RulesPath, new MockFileData(
            $$"""
            [ { "Issuer": "github", "Audience": "https://api.example.com/api/v1/org1/proj1/access/token", "Subject": "repo:acme/app:*",
                "Claims": { "repository_id": [ "74" ] } },
              { "Issuer": "github", "Audience": "https://api.example.com/api/v1/org1/proj1/access/token", "Subject": "repo:acme/app:*",
                "Claims": { "repository_id": [ "74" ], "some_flag": {{values}} } } ]
            """));

        var exitCode = await Execute(rulesFile: RulesPath);

        exitCode.ShouldBe(ExitCodes.ERROR_INPUT_PARSING);
        Important().ShouldBe($"! Rule 2 of the rules file {RulesPath}: {expected}");
        _accessApi.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("{ \"Issuer\": \"google\", \"Audience\": \"aud\", \"Subject\": 107234789 }", "Subject has to be a string, not 107234789.")]
    [InlineData("{ \"issuer\": \"google\", \"audience\": \"aud\", \"subject\": true }", "subject has to be a string, not true.")]
    [InlineData("{ \"Issuer\": \"google\", \"Audience\": 1.0, \"Subject\": \"107234789\" }", "Audience has to be a string, not 1.0.")]
    public async Task RulesFile_WithARuleValueThatIsNoString_FailsWithoutCallingTheServer(string rule, string expected)
    {
        _fileSystem.AddFile(RulesPath, new MockFileData($"[ {rule} ]"));

        var exitCode = await Execute(rulesFile: RulesPath);

        exitCode.ShouldBe(ExitCodes.ERROR_INPUT_PARSING);
        Important().ShouldBe($"! Rule 1 of the rules file {RulesPath}: {expected}");
        _accessApi.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RulesFile_KeepsDateLikeStringsAsWritten()
    {
        // By default Newtonsoft reads an ISO date as a DateTime and writes it back in the current culture's format.
        _fileSystem.AddFile(RulesPath, new MockFileData(
            """
            [ { "Issuer": "github", "Audience": "https://api.example.com/api/v1/org1/proj1/access/token",
                "Subject": "2026-10-17T00:00:00Z", "Claims": { "issued_on": [ "2026-10-17T00:00:00Z", "2026-10-17" ] } } ]
            """));

        var exitCode = await Execute(rulesFile: RulesPath);

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        var rule = _sent!.FederationRules.ShouldHaveSingleItem();
        rule.Subject.ShouldBe("2026-10-17T00:00:00Z");
        rule.Claims["issued_on"].ShouldBe(["2026-10-17T00:00:00Z", "2026-10-17"]);
        JsonConvert.SerializeObject(_sent).ShouldContain("\"Subject\":\"2026-10-17T00:00:00Z\",\"Claims\":{\"issued_on\":[\"2026-10-17T00:00:00Z\",\"2026-10-17\"]}");
    }

    [Fact]
    public void AudienceHelp_GivesTheAzureAudienceOfBothTokenVersions()
    {
        var description = typeof(MachineCreateCommand)
            .GetProperty(nameof(MachineCreateCommand.FederationAudience))!
            .GetCustomAttribute<OptionAttribute>()!
            .Description!;

        description.ShouldContain("For Azure, the aud of tokens for the Storyteller app registration: <appId> with requestedAccessTokenVersion 2, api://<appId> with 1.");
        description.ShouldNotContain("app registration's URI");
    }

    [Fact]
    public async Task MissingRulesFile_FailsWithoutCallingTheServer()
    {
        var exitCode = await Execute(rulesFile: RulesPath);

        exitCode.ShouldBe(ExitCodes.ERROR_INPUT_PARSING);
        Important().ShouldContain($"The rules file {RulesPath} does not exist.");
        _accessApi.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RuleTheServerRefuses_PrintsTheServerMessage()
    {
        // CreateMachineAccess declares no 400, so the body arrives as text.
        _accessApi
            .Setup(api => api.CreateMachineAccessAsync("org1", "proj1", It.IsAny<MachineAccessCreate>()))
            .ThrowsAsync(new ApiException(
                "The HTTP status code of the response was not expected (400).",
                400,
                """{"Message":"Rule 1: the audience sts.amazonaws.com belongs to another service.","ErrorCode":"InvalidFederation"}""",
                new Dictionary<string, IEnumerable<string>>(),
                null));

        var exitCode = await Execute(issuer: "aws", audience: "sts.amazonaws.com", subject: "arn:aws:iam::123456789012:role/ci");

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        Important().ShouldBe("! Rule 1: the audience sts.amazonaws.com belongs to another service.");
        _console.Lines.ShouldContain("Error code: InvalidFederation");
    }

    private string Line(string start)
    {
        return _console.Lines.Single(line => line.TrimStart().StartsWith(start));
    }

    private string Important()
    {
        return _console.Lines.Single(line => line.StartsWith("! "));
    }

    private Task<int> Execute(
        string? credentialKind = null,
        string? issuer = null,
        string? audience = null,
        string? subject = null,
        string[]? claims = null,
        string? rulesFile = null,
        int? lifetimeDays = null,
        bool useSharedCertificate = false)
    {
        var command = new MachineCreateCommand(
            _console.Object,
            _context.Object,
            _accessApi.Object,
            _fileSystem,
            Mock.Of<IAuthenticationConfigurationResolver>())
        {
            CredentialKind = credentialKind,
            FederationIssuer = issuer,
            FederationAudience = audience,
            FederationSubject = subject,
            FederationClaims = claims,
            RulesFile = rulesFile,
            LifetimeDays = lifetimeDays,
            UseSharedCertificate = useSharedCertificate,
        };

        return command.OnExecuteAsync();
    }
}

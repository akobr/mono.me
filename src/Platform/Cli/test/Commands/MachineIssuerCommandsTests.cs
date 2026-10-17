using System;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Commands;
using _42.Platform.Cli.Commands.MachineAccess;
using _42.Platform.Storyteller.Sdk;
using Alba.CsConsoleFormat;
using Moq;
using Newtonsoft.Json;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Commands;

public class MachineIssuerCommandsTests
{
    private const string GitHub = "https://token.actions.githubusercontent.com";

    private static readonly string JwksPath = MockUnixSupport.Path(@"C:\work\jwks.json");

    private readonly RecordingConsole _console = new();
    private readonly Mock<IAccessApiClient> _accessApi = new();
    private readonly Mock<ICommandContext> _context = new();
    private readonly MockFileSystem _fileSystem = new(new Dictionary<string, MockFileData>(), MockUnixSupport.Path(@"C:\work"));
    private FederatedIssuer? _sent;

    public MachineIssuerCommandsTests()
    {
        _context.SetupGet(context => context.OrganizationName).Returns("org1");
        _context.SetupGet(context => context.ProjectName).Returns("proj1");
        _accessApi
            .Setup(api => api.SetFederatedIssuerAsync("org1", "proj1", It.IsAny<string>(), It.IsAny<FederatedIssuer>()))
            .Callback((string _, string _, string _, FederatedIssuer issuer) => _sent = issuer)
            .ReturnsAsync((string _, string _, string _, FederatedIssuer issuer) => issuer);
    }

    [Fact]
    public async Task Set_WithoutKeyOptions_SendsADiscoveryIssuer()
    {
        var exitCode = await Set(
            "github",
            GitHub,
            requiredClaims: ["repository_owner_id=65", "repository_owner_id=66"],
            maxLifetime: 7200,
            description: "GitHub Actions of acme");

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _accessApi.Verify(api => api.SetFederatedIssuerAsync("org1", "proj1", "github", It.IsAny<FederatedIssuer>()), Times.Once);
        _sent!.Name.ShouldBe("github");
        _sent.Issuer.ShouldBe(GitHub);
        _sent.KeySource.ShouldBe(FederatedIssuerKeySource.Discovery);
        _sent.JwksUri.ShouldBeNull();
        _sent.Jwks.ShouldBeNull();
        _sent.MaxTokenLifetimeSeconds.ShouldBe(7200);
        _sent.RequiredClaims["repository_owner_id"].ShouldBe(["65", "66"]);
        _sent.Description.ShouldBe("GitHub Actions of acme");

        var body = JsonConvert.SerializeObject(_sent);
        body.ShouldContain("\"KeySource\":\"Discovery\"");
        body.ShouldNotContain("Jwks");

        _console.Lines.ShouldContain("! Trusted issuer github of org1.proj1:");
        Line("Issuer:").Trim().ShouldBe($"Issuer:         {GitHub}");
        Line("Keys:").Trim().ShouldBe("Keys:           Discovery");
        Line("Max lifetime:").Trim().ShouldBe("Max lifetime:   7200 s");
        _console.Lines.Where(line => line.TrimStart().StartsWith("Required claim:")).Select(line => line.Trim())
            .ShouldBe(["Required claim: repository_owner_id=65", "Required claim: repository_owner_id=66"]);
        Line("Description:").Trim().ShouldBe("Description:    GitHub Actions of acme");
    }

    [Fact]
    public async Task Set_WithAJwksUri_SendsTheJwksUriKeySource()
    {
        var exitCode = await Set("aks-prod", "https://oidc.example.com/cluster/", jwksUri: "https://keys.example.com/openid/v1/jwks");

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _sent!.KeySource.ShouldBe(FederatedIssuerKeySource.JwksUri);
        _sent.JwksUri.ShouldBe("https://keys.example.com/openid/v1/jwks");
        _sent.Jwks.ShouldBeNull();
        _sent.RequiredClaims.ShouldBeNull();
        _sent.MaxTokenLifetimeSeconds.ShouldBeNull();
        Line("Keys:").Trim().ShouldBe("Keys:           JwksUri https://keys.example.com/openid/v1/jwks");
        Line("Max lifetime:").Trim().ShouldBe("Max lifetime:   3600 s (default)");
    }

    [Fact]
    public async Task Set_WithAJwksFile_SendsItsContentAsTheJwks()
    {
        const string jwks = """{ "keys": [ { "kty": "RSA", "kid": "k1", "n": "AQAB", "e": "AQAB" } ] }""";
        _fileSystem.AddFile(JwksPath, new MockFileData(jwks));

        var exitCode = await Set("cluster", "https://kubernetes.default.svc.cluster.local", jwksFile: JwksPath);

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _sent!.KeySource.ShouldBe(FederatedIssuerKeySource.Jwks);
        _sent.Jwks.ShouldBe(jwks);
        _sent.JwksUri.ShouldBeNull();
        Line("Keys:").Trim().ShouldBe("Keys:           Jwks (stored key set)");
    }

    [Fact]
    public async Task Set_WithBothKeyOptions_FailsWithoutCallingTheServer()
    {
        _fileSystem.AddFile(JwksPath, new MockFileData("{}"));

        var exitCode = await Set("cluster", "https://issuer.example.com", jwksUri: "https://keys.example.com/jwks", jwksFile: JwksPath);

        exitCode.ShouldBe(ExitCodes.ERROR_INPUT_PARSING);
        _console.Lines.ShouldContain("! Give either --jwks-uri or --jwks-file, not both.");
        _accessApi.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Set_WithAMissingJwksFile_FailsWithoutCallingTheServer()
    {
        var exitCode = await Set("cluster", "https://issuer.example.com", jwksFile: JwksPath);

        exitCode.ShouldBe(ExitCodes.ERROR_INPUT_PARSING);
        _console.Lines.ShouldContain($"! The JWKS file {JwksPath} does not exist.");
        _accessApi.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("", GitHub)]
    [InlineData("github", null)]
    [InlineData("github", " ")]
    public async Task Set_WithoutNameOrIssuer_FailsWithoutCallingTheServer(string name, string? issuer)
    {
        var exitCode = await Set(name, issuer);

        exitCode.ShouldBe(ExitCodes.ERROR_INPUT_PARSING);
        _accessApi.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Set_WithAMalformedRequiredClaim_FailsWithoutCallingTheServer()
    {
        var exitCode = await Set("github", GitHub, requiredClaims: ["repository_owner_id"]);

        exitCode.ShouldBe(ExitCodes.ERROR_INPUT_PARSING);
        _console.Lines.Single(line => line.StartsWith("! ")).ShouldStartWith("! --require-claim takes key=value");
        _accessApi.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Set_OfANameRulesStillReference_PrintsTheServerMessage()
    {
        _accessApi
            .Setup(api => api.SetFederatedIssuerAsync("org1", "proj1", "github", It.IsAny<FederatedIssuer>()))
            .ThrowsAsync(AnnotationCommandFixture.Error(409, "FederatedIssuerInUse", "Federation rules of the machines m1, m2 still reference the trusted issuer github."));

        var exitCode = await Set("github", GitHub);

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _console.Lines.ShouldContain("! Federation rules of the machines m1, m2 still reference the trusted issuer github.");
        _console.Lines.ShouldContain("Error code: FederatedIssuerInUse");
    }

    [Fact]
    public async Task List_WritesATableOfTheIssuers()
    {
        _accessApi
            .Setup(api => api.GetFederatedIssuersAsync("org1", "proj1"))
            .ReturnsAsync(new List<FederatedIssuer>
            {
                new FederatedIssuer
                {
                    Name = "github",
                    Issuer = GitHub,
                    RequiredClaims = new Dictionary<string, IReadOnlyList_String> { ["repository_owner_id"] = ["65"] },
                },
                new FederatedIssuer
                {
                    Name = "aks-prod",
                    Issuer = "https://oidc.example.com/cluster/",
                    KeySource = FederatedIssuerKeySource.JwksUri,
                    JwksUri = "https://keys.example.com/jwks",
                    MaxTokenLifetimeSeconds = 7200,
                },
            });
        var rows = RecordTable<FederatedIssuer>();

        var exitCode = await new MachineIssuerListCommand(_console.Object, _context.Object, _accessApi.Object).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _console.Lines.ShouldContain("# Trusted issuers");
        rows.Count.ShouldBe(2);
        rows[0].ShouldBe(["github", GitHub, "Discovery", "3600 s (default)", "repository_owner_id=65"]);
        rows[1].ShouldBe(["aks-prod", "https://oidc.example.com/cluster/", "JwksUri https://keys.example.com/jwks", "7200 s", string.Empty]);
    }

    [Fact]
    public async Task IssuerGroup_WithoutASubcommand_ListsTheIssuers()
    {
        _accessApi.Setup(api => api.GetFederatedIssuersAsync("org1", "proj1")).ReturnsAsync(new List<FederatedIssuer>());

        var exitCode = await new MachineIssuerCommand(_console.Object, _context.Object, _accessApi.Object).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _console.Lines.ShouldContain("No trusted issuers, yet.");
    }

    [Fact]
    public async Task List_AsAMachineOrWithoutTheRole_PrintsTheServerMessage()
    {
        _accessApi
            .Setup(api => api.GetFederatedIssuersAsync("org1", "proj1"))
            .ThrowsAsync(AnnotationCommandFixture.Error(403, "Forbidden", "The caller's role on org1.proj1 does not allow the operation."));

        var exitCode = await new MachineIssuerListCommand(_console.Object, _context.Object, _accessApi.Object).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.WARNING_UNAUTHORIZED_ACCESS);
        _console.Lines.ShouldContain("! The caller's role on org1.proj1 does not allow the operation.");
    }

    [Fact]
    public async Task Delete_DeletesTheIssuer()
    {
        var exitCode = await Delete("github");

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _accessApi.Verify(api => api.DeleteFederatedIssuerAsync("org1", "proj1", "github"), Times.Once);
        _console.Lines.ShouldContain("! The trusted issuer github has been deleted.");
    }

    [Fact]
    public async Task Delete_OfAnUnknownIssuer_IsWrongInput()
    {
        // The 404 of the issuer DELETE has no body.
        _accessApi
            .Setup(api => api.DeleteFederatedIssuerAsync("org1", "proj1", "github"))
            .ThrowsAsync(AnnotationCommandFixture.Error(404));

        var exitCode = await Delete("github");

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _console.Lines.ShouldContain("! The project org1.proj1 has no trusted issuer github.");
    }

    [Fact]
    public async Task Delete_WithoutAName_FailsWithoutCallingTheServer()
    {
        var exitCode = await Delete(string.Empty);

        exitCode.ShouldBe(ExitCodes.ERROR_INPUT_PARSING);
        _accessApi.VerifyNoOtherCalls();
    }

    // The text of every cell of the table the command writes.
    private List<string[]> RecordTable<T>()
    {
        var rows = new List<string[]>();
        _console.Mock
            .Setup(console => console.WriteTable(It.IsAny<IEnumerable<T>>(), It.IsAny<Func<T, IEnumerable<Cell>>>(), It.IsAny<IEnumerable<IHeaderColumn>>()))
            .Callback((IEnumerable<T> items, Func<T, IEnumerable<Cell>> render, IEnumerable<IHeaderColumn>? _) =>
                rows.AddRange(items.Select(item => render(item).Select(cell => RecordingConsole.ToText(cell)).ToArray())));
        return rows;
    }

    private string Line(string start)
    {
        return _console.Lines.Single(line => line.TrimStart().StartsWith(start));
    }

    private Task<int> Set(
        string name,
        string? issuer,
        string? jwksUri = null,
        string? jwksFile = null,
        int? maxLifetime = null,
        string[]? requiredClaims = null,
        string? description = null)
    {
        var command = new MachineIssuerSetCommand(_console.Object, _context.Object, _accessApi.Object, _fileSystem)
        {
            Name = name,
            Issuer = issuer,
            JwksUri = jwksUri,
            JwksFile = jwksFile,
            MaxLifetimeSeconds = maxLifetime,
            RequiredClaims = requiredClaims,
            IssuerDescription = description,
        };

        return command.OnExecuteAsync();
    }

    private Task<int> Delete(string name)
    {
        return new MachineIssuerDeleteCommand(_console.Object, _context.Object, _accessApi.Object) { Name = name }.OnExecuteAsync();
    }
}

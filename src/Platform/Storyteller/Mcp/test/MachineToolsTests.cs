using System.Text.Json;

using _42.Platform.Storyteller.Mcp.Tools;
using _42.Platform.Storyteller.Mcp.UnitTests.Support;

using Moq;

namespace _42.Platform.Storyteller.Mcp.UnitTests;

public class MachineToolsTests
{
    private readonly Mock<Sdk.IAccessApiClient> _access = new(MockBehavior.Strict);

    public static TheoryData<Sdk.MachineAccess, string?> NoticePerKind() => new()
    {
        {
            new Sdk.MachineAccess { Id = "m", CredentialKind = Sdk.MachineAccessCredentialKind.ApiKey, AccessKey = "2s.key" },
            "The API stores no copy of the API key in AccessKey. Copy it from this result now; it cannot be read again."
        },
        {
            new Sdk.MachineAccess { Id = "m", CredentialKind = Sdk.MachineAccessCredentialKind.Certificate, AccessKey = string.Empty, Certificate = "MIIC", CertificatePassword = "pw" },
            "The API stores no copy of the certificate (base64 PKCS#12) in Certificate and its password in CertificatePassword. Copy them from this result now; they cannot be read again."
        },
        {
            new Sdk.MachineAccess { Id = "m", CredentialKind = Sdk.MachineAccessCredentialKind.CertificateAndApiKey, AccessKey = "2s.key", Certificate = "MIIC", CertificatePassword = "pw" },
            "The API stores no copy of the API key in AccessKey, the certificate (base64 PKCS#12) in Certificate and its password in CertificatePassword. Copy them from this result now; they cannot be read again."
        },
        {
            new Sdk.MachineAccess { Id = "m", CredentialKind = Sdk.MachineAccessCredentialKind.CertificateAndApiKey, AccessKey = "2s.key", UsesSharedCertificate = true },
            "The API stores no copy of the API key in AccessKey. Copy it from this result now; it cannot be read again."
        },
        {
            new Sdk.MachineAccess { Id = "m", CredentialKind = Sdk.MachineAccessCredentialKind.ClientCredentials, AccessKey = "secret", TokenEndpoint = "https://login/token", TokenScope = "api://x/.default" },
            "The API stores no copy of the client secret in AccessKey, the token URL in TokenEndpoint and the token scope in TokenScope. Copy them from this result now; they cannot be read again."
        },
        {
            new Sdk.MachineAccess { Id = "m", CredentialKind = Sdk.MachineAccessCredentialKind.Federated, AccessKey = string.Empty, TokenEndpoint = "https://api/v1/house/main/access/token" },
            null
        },
    };

    [Fact]
    public async Task CreateMachine_FillsTheBody()
    {
        Sdk.MachineAccessCreate? sent = null;
        _access.Setup(api => api.CreateMachineAccessAsync("house", "main", It.IsAny<Sdk.MachineAccessCreate>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, Sdk.MachineAccessCreate, CancellationToken>((_, _, body, _) => sent = body)
            .ReturnsAsync(new Sdk.MachineAccess { Id = "m-1", CredentialKind = Sdk.MachineAccessCredentialKind.Certificate });

        await Tools().CreateMachine(
            "house",
            "main",
            ["ConfigurationRead", "Secrets.Read", "annotation-write", "configurationread"],
            annotationKey: "  sbt.northwind ",
            credentialKind: "certificate",
            certificateLifetimeDays: 90,
            useSharedCertificate: false);

        sent.ShouldNotBeNull();
        sent.Organization.ShouldBe("house");
        sent.Project.ShouldBe("main");
        sent.Scopes.ShouldBe([Sdk.MachineScope.ConfigurationRead, Sdk.MachineScope.SecretsRead, Sdk.MachineScope.AnnotationWrite]);
        sent.AnnotationKey.ShouldBe("sbt.northwind");
        sent.CredentialKind.ShouldBe(Sdk.MachineAccessCreateCredentialKind.Certificate);
        sent.CertificateLifetimeDays.ShouldBe(90);
        sent.UseSharedCertificate.ShouldBe(false);
        sent.FederationRules.ShouldBeNull();
    }

    [Theory]
    [InlineData("Bogus")]
    [InlineData("99")]
    [InlineData("AnnotationRead, ConfigurationRead")]
    [InlineData("")]
    public async Task CreateMachine_UnknownScope_IsAnInputErrorBeforeAnyCall(string scope)
    {
        var exception = await Should.ThrowAsync<ToolInputException>(() => Tools().CreateMachine("house", "main", ["AnnotationRead", scope]));

        exception.Message.ShouldContain("'scopes' has the unknown value");
        exception.Message.ShouldContain(MachineScopeNames.Runtime.Split(", ")[0]);
        _access.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateMachine_NoScope_IsAnInputError()
    {
        var exception = await Should.ThrowAsync<ToolInputException>(() => Tools().CreateMachine("house", "main", []));

        exception.Message.ShouldContain("at least one scope");
        _access.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateMachine_UnknownCredentialKind_IsAnInputError()
    {
        await Should.ThrowAsync<ToolInputException>(() => Tools().CreateMachine("house", "main", ["AnnotationRead"], credentialKind: "Password"));

        _access.VerifyNoOtherCalls();
    }

    [Theory]
    [MemberData(nameof(NoticePerKind))]
    public async Task CreateMachine_AddsTheNoticeOfWhatTheResultHolds(Sdk.MachineAccess created, string? notice)
    {
        _access.Setup(api => api.CreateMachineAccessAsync("house", "main", It.IsAny<Sdk.MachineAccessCreate>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(created);

        var result = await Tools().CreateMachine("house", "main", ["AnnotationRead"]);

        result.IsError.ShouldNotBe(true);
        var structured = result.StructuredContent.ShouldNotBeNull();
        structured.GetProperty("Id").GetString().ShouldBe("m");

        if (notice is null)
        {
            structured.TryGetProperty(MachineCredentialNotice.PropertyName, out _).ShouldBeFalse();
            result.Text().ShouldStartWith("{");
        }
        else
        {
            structured.GetProperty(MachineCredentialNotice.PropertyName).GetString().ShouldBe(notice);
            structured.EnumerateObject().First().Name.ShouldBe(MachineCredentialNotice.PropertyName);
            result.Text().Split('\n')[0].ShouldBe(notice);
        }
    }

    [Fact]
    public async Task CreateMachine_FederationRules_AreConvertedAndImplyFederated()
    {
        Sdk.MachineAccessCreate? sent = null;
        _access.Setup(api => api.CreateMachineAccessAsync("house", "main", It.IsAny<Sdk.MachineAccessCreate>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, Sdk.MachineAccessCreate, CancellationToken>((_, _, body, _) => sent = body)
            .ReturnsAsync(new Sdk.MachineAccess { Id = "m", CredentialKind = Sdk.MachineAccessCredentialKind.Federated });

        await Tools().CreateMachine(
            "house",
            "main",
            ["AnnotationRead"],
            federationRules: Json("""[{"Issuer":"github","audience":"https://api/token","Subject":"repo:acme/billing:*","Claims":{"repository_id":["74"],"/kubernetes.io/namespace":"billing"}}]"""));

        sent.ShouldNotBeNull();
        sent.CredentialKind.ShouldBe(Sdk.MachineAccessCreateCredentialKind.Federated);
        var rule = sent.FederationRules.ShouldHaveSingleItem();
        rule.Issuer.ShouldBe("github");
        rule.Audience.ShouldBe("https://api/token");
        rule.Subject.ShouldBe("repo:acme/billing:*");
        rule.Claims["repository_id"].ShouldBe(["74"]);
        rule.Claims["/kubernetes.io/namespace"].ShouldBe(["billing"]);
    }

    [Theory]
    [InlineData("""[{"Issuer":"github","Audience":"a","Subject":"s","Claim":{"repository_id":["74"]}}]""", "unknown member 'Claim'")]
    [InlineData("""[{"Issuer":"github","Audience":"a","Subject":"s","Claims":{"repository_owner_id":[65]}}]""", "must hold strings")]
    [InlineData("""[{"Issuer":7,"Audience":"a","Subject":"s"}]""", "'federationRules[0].Issuer' must be a JSON string")]
    [InlineData("""["github"]""", "'federationRules[0]' must be a JSON object")]
    [InlineData("""{"Issuer":"github"}""", "'federationRules' must be a JSON array")]
    public async Task CreateMachine_MalformedFederationRules_AreInputErrors(string rules, string message)
    {
        var exception = await Should.ThrowAsync<ToolInputException>(() => Tools().CreateMachine("house", "main", ["AnnotationRead"], federationRules: Json(rules)));

        exception.Message.ShouldContain(message);
        _access.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateMachine_EmptyFederationRules_AreNoRulesAndKeepTheProjectDefault()
    {
        Sdk.MachineAccessCreate? sent = null;
        _access.Setup(api => api.CreateMachineAccessAsync("house", "main", It.IsAny<Sdk.MachineAccessCreate>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, Sdk.MachineAccessCreate, CancellationToken>((_, _, body, _) => sent = body)
            .ReturnsAsync(new Sdk.MachineAccess { Id = "m", CredentialKind = Sdk.MachineAccessCredentialKind.ApiKey, AccessKey = "2s.key" });

        await Tools().CreateMachine("house", "main", ["AnnotationRead"], federationRules: Json("[]"));

        sent.ShouldNotBeNull();
        sent.CredentialKind.ShouldBeNull();
        sent.FederationRules.ShouldBeNull();
    }

    [Fact]
    public async Task ResetMachine_CertificateMachine_IsRefusedWithoutAReset()
    {
        _access.Setup(api => api.GetMachineAccessAsync("house", "main", "m-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sdk.MachineAccess { Id = "m-1", CredentialKind = Sdk.MachineAccessCredentialKind.Certificate, AccessKey = string.Empty });

        var exception = await Should.ThrowAsync<ToolInputException>(() => Tools().ResetMachine("house", "main", "m-1", CancellationToken.None));

        exception.Message.ShouldBe(MachineText.CertificateReset);
        _access.Verify(api => api.ResetMachineAccessAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void ResetMachine_DescriptionSaysACertificateMachineCannotBeReset()
    {
        var description = typeof(MachineTools).GetMethod(nameof(MachineTools.ResetMachine))!
            .GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false)
            .Cast<System.ComponentModel.DescriptionAttribute>()
            .Single()
            .Description;

        description.ShouldContain(MachineText.CertificateReset);
    }

    [Fact]
    public async Task ResetMachine_NamesTheNewSecret()
    {
        _access.Setup(api => api.GetMachineAccessAsync("house", "main", "m-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sdk.MachineAccess { Id = "m-1", CredentialKind = Sdk.MachineAccessCredentialKind.ClientCredentials, AccessKey = "new***" });
        _access.Setup(api => api.ResetMachineAccessAsync("house", "main", "m-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sdk.MachineAccess { Id = "m-1", CredentialKind = Sdk.MachineAccessCredentialKind.ClientCredentials, AccessKey = "new-secret" });

        var result = await Tools().ResetMachine("house", "main", "m-1", CancellationToken.None);

        var notice = result.StructuredContent!.Value.GetProperty(MachineCredentialNotice.PropertyName).GetString();
        notice.ShouldBe("The API stores no copy of the new client secret in AccessKey. Copy it from this result now; it cannot be read again.");
        result.Text().ShouldStartWith(notice + "\n");
    }

    [Fact]
    public async Task SetMachineFederationRules_SendsTheRules()
    {
        IEnumerable<Sdk.FederationRule>? sent = null;
        _access.Setup(api => api.SetMachineFederationRulesAsync("house", "main", "m-1", It.IsAny<IEnumerable<Sdk.FederationRule>>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, IEnumerable<Sdk.FederationRule>, CancellationToken>((_, _, _, rules, _) => sent = rules)
            .ReturnsAsync(new Sdk.MachineAccess { Id = "m-1" });

        await Tools().SetMachineFederationRules("house", "main", "m-1", Json("""[{"Issuer":"aks","Audience":"a","Subject":"system:serviceaccount:billing:*"}]"""), CancellationToken.None);

        sent.ShouldNotBeNull().ShouldHaveSingleItem().Subject.ShouldBe("system:serviceaccount:billing:*");
    }

    [Fact]
    public async Task MachineAuthenticationPolicy_UsesTheProjectKey()
    {
        Sdk.MachineAuthenticationPolicy? sent = null;
        _access.Setup(api => api.GetMachineAuthenticationAsync("house.main", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Sdk.MachineAuthenticationPolicy { EnabledCredentialKinds = [Sdk.MachineCredentialKind.ApiKey] });
        _access.Setup(api => api.SetMachineAuthenticationAsync("house.main", It.IsAny<Sdk.MachineAuthenticationPolicy>(), It.IsAny<CancellationToken>()))
            .Callback<string, Sdk.MachineAuthenticationPolicy, CancellationToken>((_, body, _) => sent = body)
            .ReturnsAsync(new Sdk.MachineAuthenticationPolicy());

        var read = await Tools().GetMachineAuthenticationPolicy("house", "main", CancellationToken.None);
        await Tools().SetMachineAuthenticationPolicy("house", "main", ["api-key", "Federated", "ApiKey"], credentialKind: "federated", certificateLifetimeDays: 30);

        read.Text().ShouldContain("\"EnabledCredentialKinds\":[\"ApiKey\"]");
        sent.ShouldNotBeNull();
        sent.EnabledCredentialKinds.ShouldBe([Sdk.MachineCredentialKind.ApiKey, Sdk.MachineCredentialKind.Federated]);
        sent.CredentialKind.ShouldBe(Sdk.MachineAuthenticationPolicyCredentialKind.Federated);
        sent.CertificateLifetimeDays.ShouldBe(30);
    }

    [Fact]
    public async Task SetMachineAuthenticationPolicy_WithoutKinds_IsAnInputError()
    {
        await Should.ThrowAsync<ToolInputException>(() => Tools().SetMachineAuthenticationPolicy("house", "main", []));

        _access.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ListMachines_PagesInTheHost()
    {
        var machines = Enumerable.Range(0, 3).Select(index => new Sdk.MachineAccess { Id = $"m-{index}", AccessKey = "2s.***" }).ToList();
        _access.Setup(api => api.GetMachineAccessesAsync("house", "main", It.IsAny<CancellationToken>())).ReturnsAsync(machines);

        var first = await Tools().ListMachines("house", "main", limit: 2);
        var token = first.StructuredContent!.Value.GetProperty("ContinuationToken").GetString();
        var second = await Tools().ListMachines("house", "main", limit: 2, continuationToken: token);

        first.StructuredContent!.Value.GetProperty("Count").GetInt32().ShouldBe(2);
        second.StructuredContent!.Value.GetProperty("Items").EnumerateArray().Single().GetProperty("Id").GetString().ShouldBe("m-2");
        second.StructuredContent!.Value.TryGetProperty("ContinuationToken", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task DeleteMachine_DeletesIt()
    {
        _access.Setup(api => api.DeleteMachineAccessAsync("house", "main", "m-1", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var result = await Tools().DeleteMachine("house", "main", " m-1 ", CancellationToken.None);

        result.StructuredContent!.Value.GetProperty("Deleted").GetBoolean().ShouldBeTrue();
        _access.Verify(api => api.DeleteMachineAccessAsync("house", "main", "m-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void ScopeAndKindLists_NameEveryEnumValue()
    {
        var listed = $"{MachineScopeNames.Runtime}, {MachineScopeNames.Management}".Split(", ");

        listed.ShouldBe(Enum.GetNames<Sdk.MachineScope>(), ignoreOrder: true);
        MachineScopeNames.CredentialKinds.ShouldBe(EnumNames.List<Sdk.MachineCredentialKind>());
        EnumNames.List<Sdk.MachineAccessCreateCredentialKind>().ShouldBe(MachineScopeNames.CredentialKinds);
    }

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private MachineTools Tools() => new(_access.Object);
}

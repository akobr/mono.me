using System.Security.Claims;

using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Federation;

namespace _42.Platform.Storyteller.Backend.Core.UnitTests;

public class UserSubjectTokenRulesTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void CheckRequest_WithoutAToken_Refuses(string? token)
    {
        var result = UserSubjectTokenRules.CheckRequest(token, ["https://mcp.example.com/mcp"]);

        result.ShouldNotBeNull().IsAccepted.ShouldBeFalse();
        result.RefusalReason.ShouldBe("The subject token is empty.");
    }

    // The audience binding cannot hold without an audience.
    [Fact]
    public void CheckRequest_WithoutAnAudience_Refuses()
    {
        UserSubjectTokenRules.CheckRequest("token", []).ShouldNotBeNull().RefusalReason.ShouldBe("The actor names no subject audience.");
        UserSubjectTokenRules.CheckRequest("token", [" "]).ShouldNotBeNull().IsAccepted.ShouldBeFalse();
        UserSubjectTokenRules.CheckRequest("token", [" https://mcp.example.com/mcp"]).ShouldBeNull();
    }

    [Fact]
    public void GetAudiences_DropsBlankEntriesAndTrims()
    {
        UserSubjectTokenRules.GetAudiences([" https://mcp.example.com/mcp ", string.Empty, "client-id"])
            .ShouldBe(["https://mcp.example.com/mcp", "client-id"]);
    }

    [Fact]
    public void HasActor_FindsTheActClaim()
    {
        UserSubjectTokenRules.HasActor([new Claim("act", """{"sub":"mcp"}""", "JSON")]).ShouldBeTrue();
        UserSubjectTokenRules.HasActor([new Claim("sub", "user_01"), new Claim("may_act", """{"sub":"mcp"}""")]).ShouldBeFalse();
    }

    [Fact]
    public void GetClientId_ReadsAzpThenAppIdThenClientId()
    {
        UserSubjectTokenRules.GetClientId([new Claim("client_id", "c"), new Claim("appid", "b"), new Claim("azp", "a")]).ShouldBe("a");
        UserSubjectTokenRules.GetClientId([new Claim("client_id", "c"), new Claim("appid", "b")]).ShouldBe("b");
        UserSubjectTokenRules.GetClientId([new Claim("azp", " "), new Claim("client_id", "c")]).ShouldBe("c");
        UserSubjectTokenRules.GetClientId([new Claim("sub", "user_01")]).ShouldBeNull();
    }

    // The ids of every provider: Entra ID GUIDs, AuthKit DCR ids, CIMD URLs and Keycloak names; and no client at all.
    [Theory]
    [InlineData(null)]
    [InlineData("6c0f1a2e-1f43-4b0e-9a4d-2f5e8f0c9b11")]
    [InlineData("client_01JDQ8Z7Y4K3M2N1P0R9S8T7V6")]
    [InlineData("https://claude.ai/oauth/mcp-oauth-client-metadata")]
    [InlineData("vscode")]
    [InlineData("!~")]
    public void IsRecordableClientId_TakesPrintableAscii(string? clientId)
    {
        UserSubjectTokenRules.IsRecordableClientId(clientId).ShouldBeTrue();
    }

    // Anything that could look like another id, or flood a stamp or a log line, is refused rather than cut or cleaned.
    [Theory]
    [InlineData("")]
    [InlineData(" vscode")]
    [InlineData("vscode ")]
    [InlineData("vs code")]
    [InlineData("vscode\n")]
    [InlineData("vs\u0000code")]
    [InlineData("vscode‮")]
    [InlineData("vs​code")]
    [InlineData("vscоde")]
    [InlineData("vs code")]
    [InlineData("vscodé")]
    public void IsRecordableClientId_RefusesEverythingElse(string clientId)
    {
        UserSubjectTokenRules.IsRecordableClientId(clientId).ShouldBeFalse();
    }

    [Fact]
    public void IsRecordableClientId_TakesAtMost256Characters()
    {
        UserSubjectTokenRules.MaxClientIdLength.ShouldBe(256);
        UserSubjectTokenRules.IsRecordableClientId(new string('a', 256)).ShouldBeTrue();
        UserSubjectTokenRules.IsRecordableClientId(new string('a', 257)).ShouldBeFalse();
    }

    [Fact]
    public void IsAllowedClient_AnEmptyListAllowsAnyClient()
    {
        UserSubjectTokenRules.IsAllowedClient(null, []).ShouldBeTrue();
        UserSubjectTokenRules.IsAllowedClient("vscode", [" "]).ShouldBeTrue();
    }

    [Fact]
    public void IsAllowedClient_AListNamesTheClients()
    {
        UserSubjectTokenRules.IsAllowedClient("vscode", ["claude", " vscode "]).ShouldBeTrue();
        UserSubjectTokenRules.IsAllowedClient("VSCode", ["vscode"]).ShouldBeFalse();
        UserSubjectTokenRules.IsAllowedClient("VSCode", ["vscode"], StringComparison.OrdinalIgnoreCase).ShouldBeTrue();
        UserSubjectTokenRules.IsAllowedClient(null, ["vscode"]).ShouldBeFalse();
    }

    [Fact]
    public void GetTokenId_ReadsJtiThenUti()
    {
        UserSubjectTokenRules.GetTokenId([new Claim("uti", "u"), new Claim("jti", "j")]).ShouldBe("j");
        UserSubjectTokenRules.GetTokenId([new Claim("uti", "u")]).ShouldBe("u");
        UserSubjectTokenRules.GetTokenId([]).ShouldBeNull();
    }

    [Fact]
    public void GetAuthTime_ReadsUnixSeconds()
    {
        UserSubjectTokenRules.GetAuthTime([new Claim("auth_time", "1793000000")]).ShouldBe(DateTimeOffset.FromUnixTimeSeconds(1793000000));
        UserSubjectTokenRules.GetAuthTime([new Claim("auth_time", "soon")]).ShouldBeNull();
        UserSubjectTokenRules.GetAuthTime([new Claim("auth_time", "-1")]).ShouldBeNull();
        UserSubjectTokenRules.GetAuthTime([]).ShouldBeNull();
    }

    [Fact]
    public void Accepted_CarriesThePerson()
    {
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
        Claim[] claims = [new Claim("sub", "user_01")];

        var result = UserSubjectTokenResult.Accepted("user_01", "vscode", expiresAt, null, "jti-1", claims);

        result.IsAccepted.ShouldBeTrue();
        result.AccountId.ShouldBe("user_01");
        result.ClientId.ShouldBe("vscode");
        result.ExpiresAt.ShouldBe(expiresAt);
        result.AuthTime.ShouldBeNull();
        result.TokenId.ShouldBe("jti-1");
        result.Claims.ShouldBeSameAs(claims);
        result.RefusalReason.ShouldBeNull();
    }

    [Fact]
    public void Refused_CarriesTheReasonOnly()
    {
        var result = UserSubjectTokenResult.Refused("The token is expired.");

        result.IsAccepted.ShouldBeFalse();
        result.AccountId.ShouldBeNull();
        result.Claims.ShouldBeEmpty();
        result.RefusalReason.ShouldBe("The token is expired.");
    }

    // Handler messages can quote the token when PII logging is on; only the type is kept.
    [Fact]
    public void NotValid_KeepsTheExceptionTypeOnly()
    {
        var result = UserSubjectTokenResult.NotValid(new InvalidOperationException("eyJhbGciOiJSUzI1NiJ9.secret"));

        result.RefusalReason.ShouldBe("The token is not valid (InvalidOperationException).");
        UserSubjectTokenResult.NotValid(null).RefusalReason.ShouldBe("The token is not valid (no reason given).");
    }

    [Fact]
    public void ActorRestrictions_AreAClosedList()
    {
        ActorRestrictions.All.ShouldBe([ActorRestrictions.NoSecretReveal, ActorRestrictions.ReadOnly]);
        ActorRestrictions.NoSecretReveal.ShouldBe("NoSecretReveal");
        ActorRestrictions.ReadOnly.ShouldBe("ReadOnly");
        ActorRestrictions.IsKnown("NoSecretReveal").ShouldBeTrue();
        ActorRestrictions.IsKnown("ReadOnly").ShouldBeTrue();
        ActorRestrictions.IsKnown("nosecretreveal").ShouldBeFalse();
        ActorRestrictions.IsKnown("readonly").ShouldBeFalse();
        ActorRestrictions.IsKnown(null).ShouldBeFalse();
    }

    [Fact]
    public void Constants_AreTheWireValues()
    {
        UserScopes.Impersonation.ShouldBe("User.Impersonation");
        OAuthTokenExchange.Errors.InvalidClient.ShouldBe("invalid_client");
        OAuthTokenExchange.Errors.InvalidGrant.ShouldBe("invalid_grant");
    }
}

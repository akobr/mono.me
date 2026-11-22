using System.Security.Claims;

using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Api;
using _42.Platform.Storyteller.Api.Security;

using Microsoft.Extensions.DependencyInjection;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

public class HttpRequestDataExtensionsTests
{
    [Fact]
    public void GetClaims_CachedClaims_IgnoresTheBearerHeader()
    {
        var cached = new List<Claim> { new("sub", "cached-user") };
        using var services = new ServiceCollection().BuildServiceProvider();
        var (context, request) = FunctionTestDoubles.CreateRequest(
            services,
            new Dictionary<string, string> { ["Authorization"] = "Bearer something" });
        context.Items[FunctionContextItemKeys.CachedClaims] = cached;

        var claims = request.GetClaims();

        claims.ShouldBeSameAs(cached);
    }

    [Fact]
    public void GetClaims_AuthenticatedIdentity_IsCached()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var (_, request) = FunctionTestDoubles.CreateRequest(services);
        var identity = new ClaimsIdentity([new Claim("sub", "identity-user")], "Test");
        request.IdentityList.Add(identity);

        var claims = request.GetClaims();

        claims.Count.ShouldBe(1);
        claims[0].Value.ShouldBe("identity-user");
        request.FunctionContext.Items[FunctionContextItemKeys.CachedClaims].ShouldBeSameAs(claims);
    }

    [Fact]
    public void GetClaims_BearerWithoutCachedClaims_DoesNotParseTheToken()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var (context, request) = FunctionTestDoubles.CreateRequest(
            services,
            new Dictionary<string, string> { ["Authorization"] = "Bearer header.payload.sig" });

        var claims = request.GetClaims();

        claims.ShouldBeEmpty();
        context.Items.ContainsKey(FunctionContextItemKeys.CachedClaims).ShouldBeFalse();
    }

    // The scopes the credentials carry, from every claim a provider puts them in: scp (Entra delegated, AuthKit), roles
    // (Entra app roles, machine credentials, Storyteller tokens), ClaimTypes.Role and the */scope claims, one value or
    // several separated by spaces. The App. prefix of an app role is dropped; nothing else is changed.
    [Theory]
    [InlineData("scp", "User.Impersonation", new[] { "User.Impersonation" })]
    [InlineData("scp", "Annotation.Read Configuration.Write", new[] { "Annotation.Read", "Configuration.Write" })]
    [InlineData("roles", "Secrets.Read", new[] { "Secrets.Read" })]
    [InlineData(ClaimTypes.Role, "Schema.Write", new[] { "Schema.Write" })]
    [InlineData("http://schemas.microsoft.com/identity/claims/scope", "User.Impersonation Annotation.Read", new[] { "User.Impersonation", "Annotation.Read" })]
    [InlineData("roles", "App.Annotation.Write", new[] { "Annotation.Write" })]
    [InlineData("roles", "app.KeyValue.Manage", new[] { "KeyValue.Manage" })]
    [InlineData("scp", "annotation.read", new[] { "annotation.read" })]
    [InlineData("name", "Annotation.Read", new string[0])]
    [InlineData("permissions", "storyteller:annotation-read", new string[0])]
    public void GetScopes_ReadsTheScopeClaims(string claimType, string value, string[] expected)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var (context, request) = FunctionTestDoubles.CreateRequest(services);
        context.Items[FunctionContextItemKeys.CachedClaims] = new List<Claim> { new("sub", "caller"), new(claimType, value) };

        request.GetScopes().ShouldBe(expected, ignoreOrder: true);
    }

    [Fact]
    public void GetScopes_SeveralClaims_AreOneSet()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var (context, request) = FunctionTestDoubles.CreateRequest(services);
        context.Items[FunctionContextItemKeys.CachedClaims] = new List<Claim>
        {
            new("scp", "Annotation.Read Configuration.Read"),
            new("roles", "App.Annotation.Read"),
            new("roles", "Configuration.Transfer"),
            new(ClaimTypes.Role, "Secrets.Write"),
        };

        request.GetScopes().ShouldBe(["Annotation.Read", "Configuration.Read", "Configuration.Transfer", "Secrets.Write"], ignoreOrder: true);
    }

    [Fact]
    public void GetScopes_WithoutAnIdentity_IsEmpty()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var (_, request) = FunctionTestDoubles.CreateRequest(services);

        request.GetScopes().ShouldBeEmpty();
    }

    [Fact]
    public void TryGetApplicationIdentity_MachineIdentity_ReturnsTheStoredId()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var (context, request) = FunctionTestDoubles.CreateRequest(services);
        context.Items[FunctionContextItemKeys.MachineIdentity] = "machine-app";

        request.IsApplicationIdentity().ShouldBeTrue();
        request.TryGetApplicationIdentity(out var appId).ShouldBeTrue();
        appId.ShouldBe("machine-app");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void TryGetApplicationIdentity_MissingMachineIdentity_ReturnsFalse(string? machineId)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var (context, request) = FunctionTestDoubles.CreateRequest(services);

        if (machineId is not null)
        {
            context.Items[FunctionContextItemKeys.MachineIdentity] = machineId;
        }

        request.IsApplicationIdentity().ShouldBeFalse();
        request.TryGetApplicationIdentity(out var appId).ShouldBeFalse();
        appId.ShouldBeNull();
    }

    // A user acting through an actor: the actor and its restrictions come from the items the bearer middleware stored.
    [Fact]
    public void TryGetDelegatedActor_UserThroughAnActor_ReturnsTheActorAndItsRestrictions()
    {
        var request = TestRequests.DelegatedUser([ActorRestrictions.NoSecretReveal]);

        request.TryGetDelegatedActor(out var actorId).ShouldBeTrue();
        actorId.ShouldBe(TestRequests.ActorId);
        request.HasActorRestriction(ActorRestrictions.NoSecretReveal).ShouldBeTrue();
        request.HasActorRestriction("NoWrites").ShouldBeFalse();
        request.IsApplicationIdentity().ShouldBeFalse();
    }

    [Fact]
    public void TryGetDelegatedActor_ActorWithoutRestrictions_HasNone()
    {
        var request = TestRequests.DelegatedUser([]);

        request.TryGetDelegatedActor(out _).ShouldBeTrue();
        request.HasActorRestriction(ActorRestrictions.NoSecretReveal).ShouldBeFalse();
    }

    // Claims never name an actor: only the items do.
    [Fact]
    public void TryGetDelegatedActor_DirectUser_HasNoActor()
    {
        var request = TestRequests.User();
        var claims = (List<Claim>)request.FunctionContext.Items[FunctionContextItemKeys.CachedClaims];
        claims.Add(new Claim("act", """{"sub":"mcp"}""", "JSON"));
        claims.Add(new Claim("restrictions", ActorRestrictions.NoSecretReveal));

        request.TryGetDelegatedActor(out var actorId).ShouldBeFalse();
        actorId.ShouldBeNull();
        request.HasActorRestriction(ActorRestrictions.NoSecretReveal).ShouldBeFalse();
        request.GetAuditActor().Via.ShouldBeNull();
    }

    [Fact]
    public void GetAuditActor_UserThroughAnActor_IsTheAccountViaTheActor()
    {
        var actor = TestRequests.DelegatedUser().GetAuditActor();

        actor.ShouldBe(AuditActor.Account(TestRequests.UserId, "Ada") with { Via = TestRequests.ActorId });
        actor.Kind.ShouldBe(AuditActorKind.Account);
    }

    // The client app the person used with the actor goes with Via.
    [Fact]
    public void GetAuditActor_UserThroughAnActorWithAClient_IsTheAccountViaTheActorAndTheClient()
    {
        var request = TestRequests.DelegatedUser(client: TestRequests.ClientId);

        request.TryGetDelegatedClient(out var clientId).ShouldBeTrue();
        clientId.ShouldBe(TestRequests.ClientId);
        request.GetAuditActor().ShouldBe(AuditActor.Account(TestRequests.UserId, "Ada") with { Via = TestRequests.ActorId, ViaClient = TestRequests.ClientId });
    }

    // A token whose subject token named no client (or one issued before the claim): Via alone.
    [Fact]
    public void GetAuditActor_UserThroughAnActorWithoutAClient_HasNoViaClient()
    {
        var request = TestRequests.DelegatedUser();

        request.TryGetDelegatedClient(out var clientId).ShouldBeFalse();
        clientId.ShouldBeNull();
        request.GetAuditActor().ViaClient.ShouldBeNull();
    }

    // Only the delegation gives a client: an item without an actor is not one, and a direct call has none.
    [Fact]
    public void GetAuditActor_ClientWithoutAnActor_HasNoViaClient()
    {
        var request = TestRequests.User();
        request.FunctionContext.Items[FunctionContextItemKeys.DelegatedClient] = TestRequests.ClientId;

        request.GetAuditActor().ShouldBe(AuditActor.Account(TestRequests.UserId, "Ada"));
    }

    [Fact]
    public void GetAuditActor_DirectUser_HasNoVia()
    {
        TestRequests.User().GetAuditActor().ShouldBe(AuditActor.Account(TestRequests.UserId, "Ada"));
        TestRequests.User().TryGetDelegatedClient(out _).ShouldBeFalse();
    }

    // The caller of an access change: the audit actor, and the invocation id as the correlation id of its record.
    [Fact]
    public void GetAccessCaller_DirectUser_IsTheAccountWithTheInvocationId()
    {
        TestRequests.User().GetAccessCaller().ShouldBe(new AccessCaller(AuditActor.Account(TestRequests.UserId, "Ada")) { CorrelationId = "invocation" });
    }

    [Fact]
    public void GetAccessCaller_UserThroughAnActor_IsTheAccountViaTheActor()
    {
        var caller = TestRequests.DelegatedUser().GetAccessCaller();

        caller.Actor.ShouldBe(AuditActor.Account(TestRequests.UserId, "Ada") with { Via = TestRequests.ActorId });
        caller.CorrelationId.ShouldBe("invocation");
    }

    [Fact]
    public void GetAccessCaller_UserThroughAnActorWithAClient_CarriesTheClient()
    {
        var caller = TestRequests.DelegatedUser(client: TestRequests.ClientId).GetAccessCaller();

        caller.Actor.ShouldBe(AuditActor.Account(TestRequests.UserId, "Ada") with { Via = TestRequests.ActorId, ViaClient = TestRequests.ClientId });
        caller.CorrelationId.ShouldBe("invocation");
    }

    [Fact]
    public void GetAccessCaller_Machine_IsTheMachine()
    {
        TestRequests.Machine().GetAccessCaller().ShouldBe(new AccessCaller(AuditActor.Machine(TestRequests.MachineId)) { CorrelationId = "invocation" });
    }

    // The key/value caller carries the audit actor, so key/value audit events record Via too.
    [Fact]
    public async Task GetSecretRights_UserThroughAnActor_CallerIsTheAccountViaTheActor()
    {
        var rights = await TestRequests.DelegatedUser().GetSecretRightsAsync(new RoleAccess(AccountRole.ContributorWithSecrets), new FixedKeyValuePolicies(), "acme", "web");

        rights.Caller.Actor.ShouldBe(AuditActor.Account(TestRequests.UserId, "Ada") with { Via = TestRequests.ActorId });
        rights.Caller.Channel.ShouldBe(KeyValueChannel.User);
    }

    // The key/value audit and its binding reads record the client too.
    [Fact]
    public async Task GetSecretRights_UserThroughAnActorWithAClient_CallerCarriesTheClient()
    {
        var rights = await TestRequests.DelegatedUser(client: TestRequests.ClientId).GetSecretRightsAsync(new RoleAccess(AccountRole.ContributorWithSecrets), new FixedKeyValuePolicies(), "acme", "web");

        rights.Caller.Actor.ViaClient.ShouldBe(TestRequests.ClientId);
    }

    // The token of a user acting through an actor carries User.Impersonation (scp), so the user checks pass in every build.
    [Fact]
    public void EnsureUser_UserThroughAnActor_Passes()
    {
        Should.NotThrow(() => TestRequests.DelegatedUser().EnsureUser());
    }

    [Theory]
    [InlineData(AccountRole.None, AccountRole.Reader)]
    [InlineData(AccountRole.Reader, AccountRole.Contributor)]
    [InlineData(AccountRole.ContributorWithSecrets, AccountRole.Administrator)]
    public void EnsureRole_BelowMinimal_ThrowsAccessDenied(AccountRole actual, AccountRole minimal)
    {
        var exception = Should.Throw<AccessDeniedException>(() => HttpRequestDataExtensions.EnsureRole(actual, minimal, "org.project"));

        exception.ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
        exception.Message.ShouldContain("org.project");
    }

    [Theory]
    [InlineData(AccountRole.Reader, AccountRole.Reader)]
    [InlineData(AccountRole.Owner, AccountRole.Administrator)]
    public void EnsureRole_SufficientRole_DoesNotThrow(AccountRole actual, AccountRole minimal)
    {
        Should.NotThrow(() => HttpRequestDataExtensions.EnsureRole(actual, minimal, "org.project"));
    }
}

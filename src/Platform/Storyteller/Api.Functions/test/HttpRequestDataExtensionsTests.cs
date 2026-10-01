using System.Security.Claims;

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
}

using System.Security.Claims;

using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Api;
using _42.Platform.Storyteller.Api.Security;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

public class IdentityProfileTests
{
    [Fact]
    public async Task GetIdentityProfile_TokenHasBoth_DoesNotCallTheResolver()
    {
        var resolver = new RecordingProfileResolver(new UserProfile("other@example.com", "Other"));
        var request = Request(new Claim("sub", "user_01"), new Claim("preferred_username", " ada@example.com "), new Claim("name", "Ada"));

        var (userName, name) = await request.GetIdentityProfileAsync(resolver);

        userName.ShouldBe("ada@example.com");
        name.ShouldBe("Ada");
        resolver.Subjects.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetIdentityProfile_MissingClaims_AreFilledByTheResolver()
    {
        var resolver = new RecordingProfileResolver(new UserProfile("ada@example.com", "Ada Lovelace"));
        var request = Request(new Claim("sub", "user_01"));

        var (userName, name) = await request.GetIdentityProfileAsync(resolver);

        userName.ShouldBe("ada@example.com");
        name.ShouldBe("Ada Lovelace");
        resolver.Subjects.ShouldBe(["user_01"]);
    }

    [Fact]
    public async Task GetIdentityProfile_OnlyTheMissingClaimIsTakenFromTheResolver()
    {
        var resolver = new RecordingProfileResolver(new UserProfile("other@example.com", "Ada Lovelace"));
        var request = Request(new Claim("sub", "user_01"), new Claim("preferred_username", "ada@example.com"));

        var (userName, name) = await request.GetIdentityProfileAsync(resolver);

        userName.ShouldBe("ada@example.com");
        name.ShouldBe("Ada Lovelace");
    }

    [Fact]
    public async Task GetIdentityProfile_NoResolver_ReportsTheMissingClaimAsBefore()
    {
        var request = Request(new Claim("sub", "user-1"), new Claim("name", "Ada"));

        var exception = await Should.ThrowAsync<SecurityTokenException>(() => request.GetIdentityProfileAsync(null));

        exception.Message.ShouldBe("Missing preferred_username claim.");
    }

    [Fact]
    public async Task GetIdentityProfile_ResolverWithoutProfile_ReportsTheMissingClaim()
    {
        var resolver = new RecordingProfileResolver(null);
        var request = Request(new Claim("sub", "user_01"), new Claim("preferred_username", "ada@example.com"));

        var exception = await Should.ThrowAsync<SecurityTokenException>(() => request.GetIdentityProfileAsync(resolver));

        exception.Message.ShouldBe("Missing name claim.");
    }

    [Fact]
    public async Task GetIdentityProfile_UpnFallback_StillCounts()
    {
        var resolver = new RecordingProfileResolver(null);
        var request = Request(new Claim("sub", "user-1"), new Claim(ClaimTypes.Upn, "ada@contoso.com"), new Claim("name", "Ada"));

        var (userName, _) = await request.GetIdentityProfileAsync(resolver);

        userName.ShouldBe("ada@contoso.com");
        resolver.Subjects.ShouldBeEmpty();
    }

    private static TestHttpRequestData Request(params Claim[] claims)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var (context, request) = FunctionTestDoubles.CreateRequest(services);
        context.Items[FunctionContextItemKeys.CachedClaims] = claims.ToList();
        return request;
    }

    private sealed class RecordingProfileResolver : IUserProfileResolver
    {
        private readonly UserProfile? _profile;

        public RecordingProfileResolver(UserProfile? profile)
        {
            _profile = profile;
        }

        public List<string> Subjects { get; } = [];

        public Task<UserProfile?> ResolveAsync(string subject, CancellationToken cancellationToken = default)
        {
            Subjects.Add(subject);
            return Task.FromResult(_profile);
        }
    }
}

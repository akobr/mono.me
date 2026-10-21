using System.Security.Claims;

using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Api.Security;

using Microsoft.Extensions.DependencyInjection;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// Requests as the authentication middlewares leave them. A user token acts for a user (User.Impersonation) and carries
// no data scope: the role decides. A machine's credentials carry scope strings, one roles claim each, as
// MachineAuthenticationMiddleware emits them; the stored machine decides what they grant. A body goes as JSON, with its
// content type.
internal static class TestRequests
{
    public const string UserId = "user-1";

    public const string MachineId = "machine-1";

    public static TestHttpRequestData User(string method = "GET", string? body = null, IServiceProvider? services = null, string userId = UserId)
    {
        return UserCarrying([Scopes.User.Impersonation], method, body, services, userId);
    }

    // A user token whose scp carries the given strings as they are: none, or data scopes next to User.Impersonation.
    public static TestHttpRequestData UserCarrying(
        IEnumerable<string> scopes,
        string method = "GET",
        string? body = null,
        IServiceProvider? services = null,
        string userId = UserId)
    {
        var (context, request) = Create(services, method, body);
        var claims = new List<Claim>
        {
            new("sub", userId),
            new("preferred_username", "ada@example.com"),
            new("name", "Ada"),
        };
        var scp = string.Join(' ', scopes);

        if (scp.Length > 0)
        {
            claims.Add(new Claim("scp", scp));
        }

        context.Items[FunctionContextItemKeys.CachedClaims] = claims;
        return request;
    }

    // Credentials that carry the given scopes; every scope by default, so the stored machine alone decides.
    public static TestHttpRequestData Machine(
        IEnumerable<MachineScope>? scopes = null,
        string method = "GET",
        string? body = null,
        IServiceProvider? services = null,
        string machineId = MachineId,
        MachineCredentialKind? credentialKind = null,
        bool mtls = false)
    {
        return MachineCarrying(MachineScopes.ToScopeStrings(scopes ?? MachineScopes.All), method, body, services, machineId, credentialKind, mtls);
    }

    // Credentials that carry the given strings as they are, also ones no machine scope names (an App. prefix, a stale
    // preset, User.Impersonation).
    public static TestHttpRequestData MachineCarrying(
        IEnumerable<string> roles,
        string method = "GET",
        string? body = null,
        IServiceProvider? services = null,
        string machineId = MachineId,
        MachineCredentialKind? credentialKind = null,
        bool mtls = false)
    {
        var (context, request) = Create(services, method, body);
        var claims = new List<Claim> { new("sub", machineId) };
        claims.AddRange(roles.Select(role => new Claim("roles", role)));
        context.Items[FunctionContextItemKeys.CachedClaims] = claims;
        context.Items[FunctionContextItemKeys.MachineIdentity] = machineId;

        if (credentialKind is not null)
        {
            context.Items[FunctionContextItemKeys.MachineCredentialKind] = credentialKind.Value;
        }

        if (mtls)
        {
            context.Items[FunctionContextItemKeys.PresentingCertificateThumbprint] = "ABCD";
        }

        return request;
    }

    private static (TestFunctionContext Context, TestHttpRequestData Request) Create(IServiceProvider? services, string method, string? body)
    {
        var headers = body is null ? null : FunctionTestDoubles.JsonContentType();
        return FunctionTestDoubles.CreateRequest(services ?? new ServiceCollection().BuildServiceProvider(), headers, method, body);
    }
}

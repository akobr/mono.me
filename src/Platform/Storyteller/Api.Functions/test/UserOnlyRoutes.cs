using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Api.Security;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// The guard of the routes for users only (spec 7.4): a machine is refused with 403 AccessDenied in every build, whatever
// its credentials carry, before anything is read; a token that does not act for a user is 401 in Release builds.
internal static class UserOnlyRoutes
{
    public const string MachineMessage = "Only users may call this operation; machines cannot.";

    public const string NotActingForAUserMessage = "The token does not act for a user: the scope User.Impersonation is missing.";

    // The most a machine can carry: every machine scope and User.Impersonation.
    public static TestHttpRequestData Machine(string method = "GET", string? body = null, IServiceProvider? services = null)
    {
        return TestRequests.MachineCarrying([Scopes.User.Impersonation, .. MachineScopes.ToScopeStrings(MachineScopes.All)], method, body, services);
    }

    // A user token with every data scope but without User.Impersonation, which the data scopes do not stand in for.
    public static TestHttpRequestData NotActingForAUser(string method = "GET", string? body = null, IServiceProvider? services = null)
    {
        return TestRequests.UserCarrying(MachineScopes.ToScopeStrings(MachineScopes.All), method, body, services);
    }

    public static async Task ShouldRefuseMachineAsync(Func<Task> call, string message = MachineMessage)
    {
        var exception = await Should.ThrowAsync<AccessDeniedException>(call);

        exception.ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
        exception.Message.ShouldBe(message);
    }
}

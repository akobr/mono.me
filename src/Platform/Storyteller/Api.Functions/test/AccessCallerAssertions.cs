using _42.Platform.Storyteller.Accessing;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// The access caller a route hands to the backend: request.GetAccessCaller(), the account of the token's sub (with Via
// through an actor) and the invocation id of TestFunctionContext as the correlation id of the access audit record.
internal static class AccessCallerAssertions
{
    public const string InvocationId = "invocation";

    public static void ShouldBeUserCaller(this AccessCaller? caller, string userId = TestRequests.UserId, string? via = null)
    {
        caller.ShouldNotBeNull();
        caller.Actor.Kind.ShouldBe(AuditActorKind.Account);
        caller.Actor.Id.ShouldBe(userId);
        caller.Actor.Via.ShouldBe(via);
        caller.CorrelationId.ShouldBe(InvocationId);
    }
}

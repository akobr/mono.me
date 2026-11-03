using _42.Platform.Storyteller.Api.Security;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// The role check of a user on the access point runs in every Release build, and in a Debug build (DEV_AUTH) only in the
// async flow of a test that turns it on, so tests in parallel keep the Debug behaviour (424-164).
public class UserRoleChecksTests
{
    private static bool DevAuth => HttpRequestDataExtensions.IsDevelopmentAuthentication;

    [Fact]
    public void AreSkipped_WithoutEnforce_OnlyInADebugBuild()
    {
        UserRoleChecks.AreSkipped.ShouldBe(DevAuth);
    }

    [Fact]
    public void Enforce_UntilItsScopeIsDisposed_ChecksTheRoles()
    {
        using (UserRoleChecks.Enforce())
        {
            UserRoleChecks.AreSkipped.ShouldBeFalse();

            using (UserRoleChecks.Enforce())
            {
                UserRoleChecks.AreSkipped.ShouldBeFalse();
            }

            UserRoleChecks.AreSkipped.ShouldBeFalse();
        }

        UserRoleChecks.AreSkipped.ShouldBe(DevAuth);
    }

    [Fact]
    public async Task Enforce_InOneFlow_ReachesItsChildrenButNotAnotherFlow()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var enforced = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Started before the enforcement, as a test running in parallel.
        var other = Task.Run(
            async () =>
            {
                started.SetResult();
                await enforced.Task;
                return UserRoleChecks.AreSkipped;
            },
            cancellationToken);
        await started.Task;

        using (UserRoleChecks.Enforce())
        {
            enforced.SetResult();

            (await other).ShouldBe(DevAuth);
            (await Task.Run(() => UserRoleChecks.AreSkipped, cancellationToken)).ShouldBeFalse();
        }
    }
}

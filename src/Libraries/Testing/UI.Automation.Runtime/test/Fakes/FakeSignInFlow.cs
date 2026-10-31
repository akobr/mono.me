using _42.Testing.UI.Automation.Runtime.Access;
using _42.Testing.UI.Automation.Runtime.Users;
using Microsoft.Playwright;

namespace _42.Testing.UI.Automation.Runtime.UnitTests.Fakes;

// A sign-in that succeeds at once, unless Fail makes it throw (e.g. what Playwright throws for a fill that timed out) or
// Hold keeps it waiting; SignedOut says which pages show the user signed out. It counts its sign-ins.
internal sealed class FakeSignInFlow(string flowName = "AuthKit", bool requiresPassword = true, string? signInItemPrefix = null) : ISignInFlow
{
    private int signIns;

    public string Name { get; } = flowName;

    public bool RequiresPassword { get; } = requiresPassword;

    public Func<TestUser, Exception>? Fail { get; set; }

    public TaskCompletionSource? Hold { get; set; }

    public Func<IPage, bool>? SignedOut { get; set; }

    public int SignIns => Volatile.Read(ref signIns);

    public async Task SignInAsync(IPage page, TestUser user, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref signIns);
        if (Hold is { } hold)
        {
            await hold.Task.WaitAsync(cancellationToken);
        }

        if (Fail?.Invoke(user) is { } failure)
        {
            throw failure;
        }
    }

    public ValueTask<bool> IsSignedOutAsync(IPage page, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(SignedOut?.Invoke(page) ?? false);
    }

    public bool IsSignInStorageItem(string origin, string name)
    {
        return signInItemPrefix is not null && name.StartsWith(signInItemPrefix, StringComparison.Ordinal);
    }
}

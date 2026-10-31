using _42.Testing.UI.Automation.Runtime.Access;
using _42.Testing.UI.Automation.Runtime.Users;
using Microsoft.Playwright;

namespace _42.Testing.UI.Automation.Runtime.UnitTests.Fakes;

internal sealed class FakeSignInFlow(string flowName = "AuthKit", bool requiresPassword = true, string? signInItemPrefix = null) : ISignInFlow
{
    public string Name { get; } = flowName;

    public bool RequiresPassword { get; } = requiresPassword;

    public Task SignInAsync(IPage page, TestUser user, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public bool IsSignInStorageItem(string origin, string name)
    {
        return signInItemPrefix is not null && name.StartsWith(signInItemPrefix, StringComparison.Ordinal);
    }
}

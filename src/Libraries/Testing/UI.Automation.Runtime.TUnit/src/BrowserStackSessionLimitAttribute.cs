using _42.Testing.UI.Automation.Runtime.Settings;
using TUnit.Core;
using TUnit.Core.Interfaces;

namespace _42.Testing.UI.Automation.Runtime.TUnit;

// Limits the tests that run at once to BrowserStack:MaxParallelSessions when Browser:Provider is BrowserStack, on top
// of Execution:MaxParallelTests; with the Local provider it does nothing. Applied once with
// [assembly: BrowserStackSessionLimit].
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class BrowserStackSessionLimitAttribute : Attribute, ITestRegisteredEventReceiver
{
    public int Order => 0;

    public ValueTask OnTestRegistered(TestRegisteredContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (UiAutomationSession.LoadedSettings is { IsConfigured: true, IsReplay: false, Browser.Provider: BrowserProviderKind.BrowserStack })
        {
            context.SetParallelLimiter(SessionLimit.Instance);
        }

        return default;
    }

    // TUnit keys its semaphore by this type and reads Limit once, when it creates the semaphore.
    private sealed class SessionLimit : IParallelLimit
    {
        public static readonly SessionLimit Instance = new();

        public int Limit => Math.Max(1, UiAutomationSession.LoadedSettings?.BrowserStack.MaxParallelSessions ?? 1);
    }
}

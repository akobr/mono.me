using TUnit.Core;
using TUnit.Core.Interfaces;

namespace _42.Testing.UI.Automation.Runtime.TUnit;

// Reports the tests that do not report themselves: tests that do not derive from AutomationTest (or UiTest), e.g. plain
// API checks in the same assembly, and tests an attribute skipped, which run no hooks. Applied once with
// [assembly: ReportResults]. It runs after the After(Test) hooks, so an AutomationTest has reported already and is not
// reported twice; a failed attempt that TUnit runs again is not reported at all.
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class ReportResultsAttribute : Attribute, ITestEndEventReceiver
{
    public int Order => 0;

    public ValueTask OnTestEnd(TestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return UiAutomationSession.ReportFromEngineAsync(context);
    }
}

using TUnit.Core;
using TUnit.Core.Interfaces;

namespace _42.Testing.UI.Automation.Runtime.TUnit;

// Runs a failed test again up to Execution:Retries times, never for a skip or a DataConflictException. Applied once
// with [assembly: ConfiguredRetry]; a [Retry(n)] anywhere is replaced, because this attribute shares the retry scope
// and sorts before it. The count is read at discovery, after the settings loaded.
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class ConfiguredRetryAttribute : Attribute, ITestDiscoveryEventReceiver, IScopedAttribute
{
    public Type ScopeType => typeof(RetryAttribute);

    // Below RetryAttribute's 0: receivers are sorted by Order before the first of each scope is kept.
    public int Order => -1;

    public ValueTask OnTestDiscovered(DiscoveredTestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var settings = UiAutomationSession.LoadedSettings;
        var retries = settings is { IsConfigured: true, IsReplay: false } ? settings.Execution.Retries : 0;
        context.SetRetryLimit(retries, static (_, exception, _) => Task.FromResult(RetryPolicy.ShouldRetry(exception)));
        return default;
    }
}

using TUnit.Core;
using TUnit.Core.Interfaces;

namespace _42.Testing.UI.Automation.Runtime.TUnit;

// A test that must run alone, e.g. one that changes a project setting and restores it. TUnit runs a parallel group
// with a keyed not-in-parallel constraint after every unconstrained test, one test at a time and in Order; a keyed
// [NotInParallel] alone would run interleaved with the parallel tests. The category Serial lets a filter leave the
// tests out (--treenode-filter "/**[Category!=Serial]") or run only them.
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class SerialAttribute : Attribute, ITestDiscoveryEventReceiver
{
    // The parallel group and the not-in-parallel key of every serial test.
    public const string Key = "ui-automation-serial";

    // The position among the serial tests, lowest first.
    public int Order { get; init; }

    // The order of the receiver, not of the test.
    int IEventReceiver.Order => 0;

    public ValueTask OnTestDiscovered(DiscoveredTestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.AddCategory(AutomationCategories.Serial);
        context.AddParallelConstraint(new ParallelGroupConstraint(Key, 0));
        context.AddParallelConstraint(new NotInParallelConstraint([Key]) { Order = Order });
        return default;
    }
}

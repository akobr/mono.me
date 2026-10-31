namespace _42.Testing.UI.Automation.Runtime.TUnit;

// Names the consumer's IUiAutomationSetup: [assembly: UiAutomationSetup<PlatformAutomationSetup>] in the test assembly,
// or [assembly: UiAutomationSetup(typeof(PlatformAutomationSetup))]. The session hooks look for it on the entry
// assembly (the test executable) before discovery and create the setup with its parameterless constructor. Without it
// the suite still runs, with no sign-in flow, no reporter and a skip reason that points nowhere.
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public class UiAutomationSetupAttribute : Attribute
{
    public UiAutomationSetupAttribute(Type setupType)
    {
        ArgumentNullException.ThrowIfNull(setupType);
        SetupType = setupType;
    }

    // A public class that implements IUiAutomationSetup and has a parameterless constructor.
    public Type SetupType { get; }

    internal IUiAutomationSetup CreateSetup()
    {
        if (!typeof(IUiAutomationSetup).IsAssignableFrom(SetupType) || SetupType.IsAbstract)
        {
            throw new InvalidOperationException($"[UiAutomationSetup] names {SetupType.FullName}, which is not a class implementing {nameof(IUiAutomationSetup)}.");
        }

        return Activator.CreateInstance(SetupType) as IUiAutomationSetup
            ?? throw new InvalidOperationException($"[UiAutomationSetup] could not create {SetupType.FullName}.");
    }
}

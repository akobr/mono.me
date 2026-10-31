namespace _42.Testing.UI.Automation.Runtime.TUnit;

// The generic form of [UiAutomationSetup]: [assembly: UiAutomationSetup<PlatformAutomationSetup>].
public sealed class UiAutomationSetupAttribute<TSetup>() : UiAutomationSetupAttribute(typeof(TSetup))
    where TSetup : class, IUiAutomationSetup, new();

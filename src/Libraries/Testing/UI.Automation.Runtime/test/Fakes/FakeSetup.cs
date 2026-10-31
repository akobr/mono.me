using _42.Testing.UI.Automation.Runtime.Access;
using _42.Testing.UI.Automation.Runtime.Settings;

namespace _42.Testing.UI.Automation.Runtime.UnitTests.Fakes;

internal sealed class FakeSetup : IUiAutomationSetup
{
    public string ReadmeHint => "src/Sample/README.md";

    public IReadOnlyList<ISignInFlow> SignInFlows { get; init; } = [new FakeSignInFlow()];

    public IReadOnlyList<ITestRunReporterProvider> ReporterProviders { get; init; } = [];

    // Added to the errors of Validate when set.
    public string? ValidationError { get; init; }

    public void Validate(UiAutomationSettings settings, ICollection<string> errors)
    {
        if (ValidationError is not null)
        {
            errors.Add(ValidationError);
        }
    }
}

using TUnit.Core;

// In the root namespace like AreaAttribute, so a test writes [Terminal] without a using.
namespace _42.Platform.Cli.AutomationTests;

// A test that answers a prompt in a pseudo terminal (mode T, CliTest.StartSformInTerminal): the category Terminal, which
// CliTest.GetSkipReason skips before the lease where no pseudo terminal is supported (TerminalSupport: Windows ConPTY,
// Linux util-linux script; not macOS).
public sealed class TerminalAttribute() : CategoryAttribute(CliCategories.Terminal);

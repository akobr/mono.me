using _42.Platform.Storyteller.Accessing;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller;

// Stops the startup when the deployment turns off its own default credential kind, or names a kind that doesn't exist.
public sealed class MachineAuthenticationOptionsValidator : IValidateOptions<MachineAuthenticationOptions>
{
    public ValidateOptionsResult Validate(string? name, MachineAuthenticationOptions options)
    {
        return MachineAuthenticationPolicies.ValidateOptions(options) is { } failure
            ? ValidateOptionsResult.Fail(failure)
            : ValidateOptionsResult.Success;
    }
}

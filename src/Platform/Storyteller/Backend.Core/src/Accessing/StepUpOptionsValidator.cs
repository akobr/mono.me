using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Accessing;

public sealed class StepUpOptionsValidator : IValidateOptions<StepUpOptions>
{
    private const string Section = StepUpOptions.SectionName;

    public ValidateOptionsResult Validate(string? name, StepUpOptions options)
    {
        var maxAge = options.MaxAge;

        if (maxAge < StepUpOptions.MinMaxAge || maxAge > StepUpOptions.MaxMaxAge)
        {
            return ValidateOptionsResult.Fail($"{Section}:MaxAge must be between {StepUpOptions.MinMaxAge:c} and {StepUpOptions.MaxMaxAge:c}, written as [d.]hh:mm:ss (00:10:00 for 10 minutes, 1.00:00:00 for 24 hours; a bare 600 is 600 days and 24:00:00 is 24 days); it is {maxAge:c}.");
        }

        // The challenge names it in whole seconds (max_age).
        return maxAge.Ticks % TimeSpan.TicksPerSecond == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail($"{Section}:MaxAge must be whole seconds; it is {maxAge:c}.");
    }
}

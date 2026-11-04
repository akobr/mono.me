using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Sdk.Hosting.Subscriptions;

// Fails the options of every name other than the default one that the configurator did not bind to a subscription, so
// a worker never runs a tenant on default options by mistake.
internal sealed class SubjectOptionsValidator<TOptions> : IValidateOptions<TOptions>
    where TOptions : class
{
    private readonly SubscriptionsService _subscriptions;
    private readonly SubjectOptionsConfigurator<TOptions> _configurator;

    public SubjectOptionsValidator(SubscriptionsService subscriptions, SubjectOptionsConfigurator<TOptions> configurator)
    {
        ArgumentNullException.ThrowIfNull(subscriptions);
        ArgumentNullException.ThrowIfNull(configurator);

        _subscriptions = subscriptions;
        _configurator = configurator;
    }

    public ValidateOptionsResult Validate(string? name, TOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrEmpty(name))
        {
            return ValidateOptionsResult.Skip;
        }

        return _configurator.IsBound(name, options)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"There are no {typeof(TOptions).Name} of the subject {name}: no subscription of the subject {name} in {_subscriptions.Responsibility}/{_subscriptions.Context} (not ready yet, pending, or not subscribed).");
    }
}

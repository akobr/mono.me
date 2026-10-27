using System.Runtime.CompilerServices;
using _42.Platform.Storyteller.Sdk.Configuring;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Sdk.Hosting.Subscriptions;

// Binds the options of a name that is a subscribed subject to the document of its subscription, or a section of it,
// with ConfigurationBinder; other names are left alone. It remembers the instances it bound and for which subject, so
// the validator fails an instance whose subject had no subscription when it was configured, also when one arrived
// before the validation.
internal sealed class SubjectOptionsConfigurator<TOptions> : IConfigureNamedOptions<TOptions>
    where TOptions : class
{
    private readonly SubscriptionsService _subscriptions;
    private readonly string? _section;
    private readonly ConditionalWeakTable<TOptions, string> _bound = new();

    // section: the section of the documents to bind; null binds the whole document.
    public SubjectOptionsConfigurator(SubscriptionsService subscriptions, string? section)
    {
        ArgumentNullException.ThrowIfNull(subscriptions);

        _subscriptions = subscriptions;
        _section = section;
    }

    public void Configure(TOptions options)
    {
        Configure(Options.DefaultName, options);
    }

    // Throws StorytellerConfigurationException when a value does not convert; the message names the key, the type and
    // the section, not the value, which may be a secret.
    public void Configure(string? name, TOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrEmpty(name) || _subscriptions.Find(name) is not { } subscription)
        {
            return;
        }

        var document = subscription.Document;
        var target = _section is null ? document.Configuration : document.Configuration.GetSection(_section);

        try
        {
            target.Bind(options);
        }
        catch (InvalidOperationException exception)
        {
            // The binder's message quotes the value that did not convert.
            var at = _section is null ? string.Empty : $" at {_section}";
            throw new StorytellerConfigurationException(
                $"The configuration {document.Key} of the subject {name} does not bind to {typeof(TOptions).Name}{at}: the binder threw {exception.GetType().Name}.",
                document.Key,
                null,
                null,
                false);
        }

        _bound.AddOrUpdate(options, name);
    }

    // Whether this instance was bound for the subject.
    public bool IsBound(string name, TOptions options)
    {
        return _bound.TryGetValue(options, out var subject) && string.Equals(subject, name, StringComparison.Ordinal);
    }
}

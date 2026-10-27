using _42.Platform.Storyteller.Sdk.Configuring;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Sdk.Hosting.Subscriptions;

// IOptionsMonitor<TOptions> for options bound per subject: the framework's OptionsMonitor<TOptions> over the same
// factory, change token sources and cache, and one change notification per applied document. On Added and Changed of
// a subject it drops the subject's options from the cache and raises OnChange with (Get(subject), subject); on Removed
// it drops them and raises nothing, as Get(subject) would fail validation. Other subjects keep their cached instance.
internal sealed class SubjectOptionsMonitor<TOptions> : IOptionsMonitor<TOptions>, IDisposable
    where TOptions : class
{
    private readonly OptionsMonitor<TOptions> _inner;
    private readonly IOptionsMonitorCache<TOptions> _cache;
    private readonly ILogger _logger;
    private readonly IDisposable _listening;
    private readonly Lock _lock = new();
    private readonly List<Registration> _registrations = [];

    public SubjectOptionsMonitor(
        IOptionsFactory<TOptions> factory,
        IEnumerable<IOptionsChangeTokenSource<TOptions>> sources,
        IOptionsMonitorCache<TOptions> cache,
        SubscriptionsService subscriptions,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(subscriptions);
        ArgumentNullException.ThrowIfNull(logger);

        _inner = new OptionsMonitor<TOptions>(factory, sources, cache);
        _cache = cache;
        _logger = logger;
        _listening = subscriptions.Listen(OnSubscriptionChanged);
    }

    public TOptions CurrentValue => _inner.CurrentValue;

    public TOptions Get(string? name)
    {
        return _inner.Get(name);
    }

    // The listener hears the subjects from here, and the configuration sources of every name from the framework's
    // monitor.
    public IDisposable? OnChange(Action<TOptions, string?> listener)
    {
        ArgumentNullException.ThrowIfNull(listener);

        var registration = new Registration(this, listener, _inner.OnChange(listener));

        lock (_lock)
        {
            _registrations.Add(registration);
        }

        return registration;
    }

    public void Dispose()
    {
        _listening.Dispose();
        _inner.Dispose();
    }

    // On the loop of the subscriptions, after the view changed, so Get(subject) binds the new document.
    private void OnSubscriptionChanged(StorytellerSubscriptionChange change)
    {
        var subject = change.Subscription.Subject;
        _cache.TryRemove(subject);

        if (change.Kind == StorytellerChangeKind.Removed)
        {
            return;
        }

        Registration[] registrations;

        lock (_lock)
        {
            registrations = [.. _registrations];
        }

        if (registrations.Length == 0)
        {
            return;
        }

        TOptions options;

        try
        {
            options = Get(subject);
        }
        catch (Exception exception) when (exception is StorytellerConfigurationException or OptionsValidationException)
        {
            // Hosting's messages name keys and types only; a validator's message is not ours to vouch for.
            _logger.LogWarning(
                "The {Options} of the subject {Subject} failed after {Kind} with {Type}, so OnChange was not raised. {Error}",
                typeof(TOptions).Name,
                subject,
                change.Kind,
                exception.GetType().Name,
                exception is StorytellerConfigurationException ? exception.Message : string.Empty);
            return;
        }

        foreach (var registration in registrations)
        {
            registration.Invoke(options, subject);
        }
    }

    private void Remove(Registration registration)
    {
        lock (_lock)
        {
            _registrations.Remove(registration);
        }
    }

    private sealed class Registration : IDisposable
    {
        private readonly SubjectOptionsMonitor<TOptions> _owner;
        private readonly Action<TOptions, string?> _listener;
        private readonly IDisposable? _inner;

        public Registration(SubjectOptionsMonitor<TOptions> owner, Action<TOptions, string?> listener, IDisposable? inner)
        {
            _owner = owner;
            _listener = listener;
            _inner = inner;
        }

        // A listener that throws must not keep the others from hearing the change.
        public void Invoke(TOptions options, string subject)
        {
            try
            {
                _listener(options, subject);
            }
            catch (Exception exception)
            {
                _owner._logger.LogWarning(
                    "A listener of the {Options} of the subject {Subject} failed with {Type}.",
                    typeof(TOptions).Name,
                    subject,
                    exception.GetType().Name);
            }
        }

        public void Dispose()
        {
            _owner.Remove(this);
            _inner?.Dispose();
        }
    }
}

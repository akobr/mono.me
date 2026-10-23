using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace _42.Platform.Storyteller.Sdk.Configuring.Logging;

// The provider starts before the container and its logger factory exist, and the host disposes that factory before
// the configuration. Loggers created here write to whatever factory is current: the options' factory, or the null one
// until the container's is handed over, and the null one again after the host stopped.
internal sealed class SwitchableLoggerFactory : ILoggerFactory
{
    private ILoggerFactory _current;

    public SwitchableLoggerFactory(ILoggerFactory? given)
    {
        _current = given ?? NullLoggerFactory.Instance;
        IsGiven = given is not null;
    }

    // The options set a factory: the container's is not handed over then.
    public bool IsGiven { get; }

    public ILoggerFactory Current => Volatile.Read(ref _current);

    public void Use(ILoggerFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        Volatile.Write(ref _current, factory);
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new SwitchableLogger(this, categoryName);
    }

    public void AddProvider(ILoggerProvider provider)
    {
        Current.AddProvider(provider);
    }

    // The factories belong to the options or the container.
    public void Dispose()
    {
    }

    private sealed class SwitchableLogger : ILogger
    {
        private readonly SwitchableLoggerFactory _owner;
        private readonly string _categoryName;
        private Binding? _binding;

        public SwitchableLogger(SwitchableLoggerFactory owner, string categoryName)
        {
            _owner = owner;
            _categoryName = categoryName;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return Resolve().BeginScope(state);
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            try
            {
                return Resolve().IsEnabled(logLevel);
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            // A factory disposed before it was handed back must not fail a refresh.
            try
            {
                Resolve().Log(logLevel, eventId, state, exception, formatter);
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private ILogger Resolve()
        {
            var factory = _owner.Current;
            var binding = Volatile.Read(ref _binding);

            if (binding is not null && ReferenceEquals(binding.Factory, factory))
            {
                return binding.Logger;
            }

            var logger = factory.CreateLogger(_categoryName);
            Volatile.Write(ref _binding, new Binding(factory, logger));
            return logger;
        }

        private sealed record Binding(ILoggerFactory Factory, ILogger Logger);
    }
}

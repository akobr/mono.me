using _42.Platform.Storyteller.Sdk.Configuring.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace _42.Platform.Storyteller.Sdk.Configuring.UnitTests.Logging;

public class SwitchableLoggerFactoryTests
{
    [Fact]
    public void ALogger_FollowsTheCurrentFactory()
    {
        var factory = new SwitchableLoggerFactory(null);
        var logger = factory.CreateLogger("engine");
        var container = new RecordingLoggerFactory();

        logger.LogWarning("Before the container.");
        factory.Use(container);
        logger.LogWarning("With the container.");
        factory.Use(NullLoggerFactory.Instance);
        logger.LogWarning("After the host stopped.");

        factory.IsGiven.ShouldBeFalse();
        var entry = container.Entries.ShouldHaveSingleItem();
        entry.Message.ShouldBe("With the container.");
        entry.Category.ShouldBe("engine");
    }

    [Fact]
    public void AGivenFactory_IsUsedFromTheStart()
    {
        var given = new RecordingLoggerFactory();
        var factory = new SwitchableLoggerFactory(given);

        factory.CreateLogger("engine").LogInformation("Started.");

        factory.IsGiven.ShouldBeTrue();
        given.Entries.ShouldHaveSingleItem();
    }

    [Fact]
    public void ADisposedFactory_DoesNotFailTheCaller()
    {
        var factory = new SwitchableLoggerFactory(new DisposedLoggerFactory());

        var logger = factory.CreateLogger("engine");

        Should.NotThrow(() => logger.LogWarning("After disposal."));
        logger.IsEnabled(LogLevel.Warning).ShouldBeFalse();
    }

    private sealed class DisposedLoggerFactory : ILoggerFactory
    {
        public ILogger CreateLogger(string categoryName)
        {
            throw new ObjectDisposedException("LoggerFactory");
        }

        public void AddProvider(ILoggerProvider provider)
        {
            throw new ObjectDisposedException("LoggerFactory");
        }

        public void Dispose()
        {
        }
    }
}

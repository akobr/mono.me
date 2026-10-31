using System.Text.Json;
using Microsoft.Playwright;
using Moq;

namespace _42.Testing.UI.Automation.Runtime.UnitTests.Fakes;

// A connected browser of the fake grid: contexts and pages that record the evaluate calls, a close that disconnects.
// FailEvaluate makes every evaluate call throw it.
internal sealed class FakeRemoteBrowser
{
    private readonly object gate = new();
    private readonly List<IBrowserContext> contexts = [];
    private readonly List<(string Expression, object? Argument)> evaluated = [];
    private bool connected = true;
    private int closes;

    public FakeRemoteBrowser()
    {
        var browser = new Mock<IBrowser>();
        browser.SetupGet(mock => mock.IsConnected).Returns(() => connected);
        browser.SetupGet(mock => mock.Version).Returns("142.0.7444.10");
        browser.SetupGet(mock => mock.Contexts).Returns(() =>
        {
            lock (gate)
            {
                return [.. contexts];
            }
        });
        browser.Setup(mock => mock.NewContextAsync(It.IsAny<BrowserNewContextOptions?>())).ReturnsAsync(() => NewContext());
        browser.Setup(mock => mock.CloseAsync(It.IsAny<BrowserCloseOptions?>())).Returns(() =>
        {
            Interlocked.Increment(ref closes);
            connected = false;
            return Task.CompletedTask;
        });
        Object = browser.Object;
    }

    public IBrowser Object { get; }

    public Exception? FailEvaluate { get; set; }

    public int Closes => Volatile.Read(ref closes);

    public int ContextsCreated
    {
        get
        {
            lock (gate)
            {
                return contexts.Count;
            }
        }
    }

    public IReadOnlyList<(string Expression, object? Argument)> Evaluated
    {
        get
        {
            lock (gate)
            {
                return [.. evaluated];
            }
        }
    }

    // BrowserStack ended the session.
    public void Disconnect()
    {
        connected = false;
    }

    private IBrowserContext NewContext()
    {
        var pages = new List<IPage>();
        var context = new Mock<IBrowserContext>();
        context.SetupGet(mock => mock.Pages).Returns(() =>
        {
            lock (gate)
            {
                return [.. pages];
            }
        });
        context.Setup(mock => mock.NewPageAsync()).ReturnsAsync(() =>
        {
            var page = NewPage();
            lock (gate)
            {
                pages.Add(page);
            }

            return page;
        });

        lock (gate)
        {
            contexts.Add(context.Object);
        }

        return context.Object;
    }

    private IPage NewPage()
    {
        var page = new Mock<IPage>();
        page.SetupGet(mock => mock.IsClosed).Returns(false);
        page.Setup(mock => mock.EvaluateAsync(It.IsAny<string>(), It.IsAny<object?>())).Returns((string expression, object? argument) =>
        {
            lock (gate)
            {
                evaluated.Add((expression, argument));
            }

            return FailEvaluate is { } failure ? Task.FromException<JsonElement?>(failure) : Task.FromResult<JsonElement?>(null);
        });
        return page.Object;
    }
}

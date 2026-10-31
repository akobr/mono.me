using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using _42.Testing.UI.Automation.Runtime.Access;
using _42.Testing.UI.Automation.Runtime.Artifacts;
using _42.Testing.UI.Automation.Runtime.UnitTests.Fakes;
using _42.Testing.UI.Automation.Runtime.Users;
using Microsoft.Playwright;
using Moq;

namespace _42.Testing.UI.Automation.Runtime.UnitTests.Access;

public sealed class StorageStateCacheTests
{
    private const string SignedIn = """
        { "cookies": [], "origins": [ { "origin": "https://admin.example.test", "localStorage": [
          { "name": "workos:refresh-token:client", "value": "R1" }, { "name": "2s-admin:preferences", "value": "default" } ] } ] }
        """;

    // Characters that a JSON document and a URL encode differently.
    private const string Password = "pa\"ss w0rd&+";

    private readonly TestUser user = new(0, "ada@example.test", "secret");
    private readonly MockFileSystem files = new();
    private readonly FakeSignInFlow flow = new(signInItemPrefix: "workos:");
    private readonly List<Mock<IBrowserContext>> contexts = [];
    private readonly List<BrowserNewContextOptions?> contextOptions = [];
    private readonly Mock<IBrowser> browser = new();
    private readonly StorageStateCache cache;

    public StorageStateCacheTests()
    {
        cache = new StorageStateCache(flow);
        browser.Setup(current => current.NewContextAsync(It.IsAny<BrowserNewContextOptions>())).ReturnsAsync((BrowserNewContextOptions? options) =>
        {
            var context = Context(SignedIn);
            lock (contexts)
            {
                contexts.Add(context);
                contextOptions.Add(options);
            }

            return context.Object;
        });
    }

    [Fact]
    public async Task RefreshAsync_GivesTheNextTestTheRotatedToken_AndThePreferencesOfTheSignIn()
    {
        await cache.GetAsync(user, browser.Object, new BrowserNewContextOptions());

        await cache.RefreshAsync(user, Context("""
            { "cookies": [], "origins": [ { "origin": "https://admin.example.test", "localStorage": [
              { "name": "workos:refresh-token:client", "value": "R2" }, { "name": "2s-admin:preferences", "value": "changed" } ] } ] }
            """).Object);
        var next = await cache.GetAsync(user, browser.Object, new BrowserNewContextOptions());

        next.ShouldContain("\"R2\"");
        next.ShouldNotContain("\"R1\"");
        next.ShouldContain("\"default\"");
        next.ShouldNotContain("\"changed\"");
        cache.SignInCount.ShouldBe(1);
    }

    [Fact]
    public async Task RefreshAsync_DropsTheState_OfAContextThatLostTheSignIn()
    {
        await cache.GetAsync(user, browser.Object, new BrowserNewContextOptions());

        await cache.RefreshAsync(user, Context("""
            { "cookies": [], "origins": [ { "origin": "https://admin.example.test", "localStorage": [ { "name": "2s-admin:preferences", "value": "default" } ] } ] }
            """).Object);

        cache.TryGet(user, out _).ShouldBeFalse();
        await cache.GetAsync(user, browser.Object, new BrowserNewContextOptions());
        cache.SignInCount.ShouldBe(2);
    }

    [Fact]
    public async Task RefreshAsync_KeepsADroppedStateDropped()
    {
        await cache.GetAsync(user, browser.Object, new BrowserNewContextOptions());
        cache.Invalidate(user);
        var context = Context(SignedIn);

        await cache.RefreshAsync(user, context.Object);

        cache.TryGet(user, out _).ShouldBeFalse();
        context.Verify(current => current.StorageStateAsync(It.IsAny<BrowserContextStorageStateOptions?>()), Times.Never);
    }

    [Fact]
    public async Task GetAsync_SignsTheUserInOnce_WhenTwoScopesAskAtTheSameTime()
    {
        flow.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var asking = new[]
        {
            cache.GetAsync(user, browser.Object, new BrowserNewContextOptions()),
            cache.GetAsync(user, browser.Object, new BrowserNewContextOptions()),
        };
        await Eventually.SettleAsync();
        flow.Hold.SetResult();
        var states = await Task.WhenAll(asking);

        states[0].ShouldBe(SignedIn);
        states[1].ShouldBe(SignedIn);
        flow.SignIns.ShouldBe(1);
        cache.SignInCount.ShouldBe(1);
        contexts.ShouldHaveSingleItem().Verify(context => context.CloseAsync(It.IsAny<BrowserContextCloseOptions?>()), Times.Once);
    }

    [Fact]
    public async Task GetAsync_SignsInInAThrowAwayContext_WithoutVideoOrStorageState()
    {
        var options = new BrowserNewContextOptions
        {
            BaseURL = "https://admin.example.test",
            Locale = "en-US",
            RecordVideoDir = "/videos",
            RecordVideoSize = new RecordVideoSize { Width = 1280, Height = 720 },
            StorageState = "{}",
            StorageStatePath = "/state.json",
        };

        await cache.GetAsync(user, browser.Object, options);

        var used = contextOptions.ShouldHaveSingleItem().ShouldNotBeNull();
        used.BaseURL.ShouldBe("https://admin.example.test");
        used.Locale.ShouldBe("en-US");
        used.RecordVideoDir.ShouldBeNull();
        used.RecordVideoSize.ShouldBeNull();
        used.StorageState.ShouldBeNull();
        used.StorageStatePath.ShouldBeNull();
        options.RecordVideoDir.ShouldBe("/videos");
        contexts.ShouldHaveSingleItem().Verify(context => context.CloseAsync(It.IsAny<BrowserContextCloseOptions?>()), Times.Once);
    }

    // Playwright's call log of a fill that resolved its element keeps the typed value, and the .NET client puts the call
    // log into the exception's message.
    [Fact]
    public async Task GetAsync_ThrowsASignInException_WithoutTheEmailOrThePassword_AndKeepsAScreenshot()
    {
        var pooled = new TestUser(3, "ada.lovelace@example.test", Password);
        flow.Fail = failed => new PlaywrightException(
            $"Timeout 10000ms exceeded.\nCall log:\n  - waiting for GetByRole(Textbox, new() {{ Name = \"Email\" }})\n  -   fill(\"{failed.Email}\")\n"
            + $"  - waiting for GetByLabel(\"Password\")\n  -   fill(\"{failed.Password}\")\n  - escaped {JsonEncodedText.Encode(failed.Password!)} and {Uri.EscapeDataString(failed.Password!)}");
        var artifacts = new ArtifactCollector(files, files.Path.Combine(files.Path.GetTempPath(), "run"), TestData.Identity("SignIn"), 1);

        var exception = await Should.ThrowAsync<SignInException>(() => cache.GetAsync(pooled, browser.Object, new BrowserNewContextOptions(), artifacts));

        exception.Message.ShouldStartWith("The sign-in flow AuthKit failed for Users:Pool:3 (u-");
        exception.Message.ShouldContain("): PlaywrightException: Timeout 10000ms exceeded.");
        exception.Message.ShouldContain("fill(\"***\")");
        exception.InnerException.ShouldBeNull();
        foreach (var text in new[] { exception.Message, exception.ToString(), TestFailure.FromException(exception).StackTrace! })
        {
            text.ShouldNotContain("ada.lovelace");
            text.ShouldNotContain("pa\"ss");
            text.ShouldNotContain("w0rd");
        }

        artifacts.Artifacts.ShouldHaveSingleItem().Name.ShouldBe("sign-in-failure.png");
        contexts.ShouldHaveSingleItem().Verify(context => context.CloseAsync(It.IsAny<BrowserContextCloseOptions?>()), Times.Once);
        cache.TryGet(pooled, out _).ShouldBeFalse();
        cache.SignInCount.ShouldBe(0);
    }

    private Mock<IBrowserContext> Context(string storageState)
    {
        var page = new Mock<IPage>();
        page.Setup(current => current.ScreenshotAsync(It.IsAny<PageScreenshotOptions?>())).Returns((PageScreenshotOptions? options) =>
        {
            // The artifact exists once the file does.
            if (options?.Path is { } path)
            {
                files.AddFile(path, new MockFileData([0x89, 0x50, 0x4e, 0x47]));
            }

            return Task.FromResult(Array.Empty<byte>());
        });

        var context = new Mock<IBrowserContext>();
        context.Setup(current => current.NewPageAsync()).ReturnsAsync(page.Object);
        context.Setup(current => current.StorageStateAsync(It.IsAny<BrowserContextStorageStateOptions?>())).ReturnsAsync(storageState);
        context.Setup(current => current.CloseAsync(It.IsAny<BrowserContextCloseOptions?>())).Returns(Task.CompletedTask);
        return context;
    }
}

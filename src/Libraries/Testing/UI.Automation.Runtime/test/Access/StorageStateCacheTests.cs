using _42.Testing.UI.Automation.Runtime.Access;
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

    private readonly TestUser user = new(0, "ada@example.test", "secret");
    private readonly StorageStateCache cache = new(new FakeSignInFlow(signInItemPrefix: "workos:"));
    private readonly Mock<IBrowser> browser = new();

    public StorageStateCacheTests()
    {
        browser.Setup(current => current.NewContextAsync(It.IsAny<BrowserNewContextOptions>())).ReturnsAsync(() => Context(SignedIn).Object);
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

    private static Mock<IBrowserContext> Context(string storageState)
    {
        var context = new Mock<IBrowserContext>();
        context.Setup(current => current.NewPageAsync()).ReturnsAsync(Mock.Of<IPage>());
        context.Setup(current => current.StorageStateAsync(It.IsAny<BrowserContextStorageStateOptions?>())).ReturnsAsync(storageState);
        context.Setup(current => current.CloseAsync(It.IsAny<BrowserContextCloseOptions?>())).Returns(Task.CompletedTask);
        return context;
    }
}

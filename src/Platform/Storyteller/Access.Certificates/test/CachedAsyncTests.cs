using _42.Utils.Async;
using Shouldly;

namespace _42.Platform.Storyteller.Access.Certificates.UnitTests;

public class CachedAsyncTests
{
    [Fact]
    public async Task GetValueAsync_FirstCall_InvokesFactory()
    {
        var callCount = 0;
        var cache = new CachedAsync<string>(
            () => { callCount++; return Task.FromResult("value"); },
            TimeSpan.FromHours(1));

        var result = await cache.GetValueAsync();

        result.ShouldBe("value");
        callCount.ShouldBe(1);
    }

    [Fact]
    public async Task GetValueAsync_WithinTtl_ReturnsCachedValue()
    {
        var callCount = 0;
        var cache = new CachedAsync<string>(
            () => { callCount++; return Task.FromResult($"value-{callCount}"); },
            TimeSpan.FromHours(1));

        var result1 = await cache.GetValueAsync();
        var result2 = await cache.GetValueAsync();

        result1.ShouldBe("value-1");
        result2.ShouldBe("value-1"); // same cached value
        callCount.ShouldBe(1);
    }

    [Fact]
    public async Task GetValueAsync_AfterInvalidate_RefreshesValue()
    {
        var callCount = 0;
        var cache = new CachedAsync<string>(
            () => { callCount++; return Task.FromResult($"value-{callCount}"); },
            TimeSpan.FromHours(1));

        await cache.GetValueAsync();
        cache.Invalidate();
        var result = await cache.GetValueAsync();

        result.ShouldBe("value-2");
        callCount.ShouldBe(2);
    }

    [Fact]
    public async Task GetValueAsync_FactoryThrows_WithExistingValue_ReturnsStale()
    {
        var callCount = 0;
        Exception? reportedError = null;
        var cache = new CachedAsync<string>(
            () =>
            {
                callCount++;
                if (callCount > 1) throw new InvalidOperationException("refresh failed");
                return Task.FromResult("stale-value");
            },
            TimeSpan.FromMilliseconds(1), // expire quickly
            ex => reportedError = ex);

        var first = await cache.GetValueAsync();
        first.ShouldBe("stale-value");

        // Wait for TTL to expire.
        await Task.Delay(10);

        var second = await cache.GetValueAsync();
        second.ShouldBe("stale-value"); // stale-on-error
        reportedError.ShouldNotBeNull();
        reportedError.ShouldBeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task GetValueAsync_FactoryThrows_WithNoExistingValue_Throws()
    {
        var cache = new CachedAsync<string>(
            () => throw new InvalidOperationException("factory failed"),
            TimeSpan.FromHours(1));

        await Should.ThrowAsync<InvalidOperationException>(
            async () => await cache.GetValueAsync());
    }
}

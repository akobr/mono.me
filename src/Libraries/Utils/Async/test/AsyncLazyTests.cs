using System;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace _42.Utils.Async.UnitTests;

public class AsyncLazyTests
{
    [Fact]
    public void AsyncLazy_Instantiate_IsLazy()
    {
        // Act
        var lazy = new AsyncLazy<int>(() => 42);

        // Assert
        lazy.Should().NotBeAssignableTo<Lazy<int>>();
    }

    [Fact]
    public void AsyncLazy_Instantiate_HasNoValue()
    {
        // Act
        var lazy = new AsyncLazy<int>(() => 42);

        // Assert
        lazy.IsValueCreated.Should().BeFalse();
    }

    [Fact]
    public void AsyncLazy_Instantiate_ValueIsTask()
    {
        // Act
        var lazy = new AsyncLazy<int>(() => 42);

        // Assert
        lazy.Value.Should().BeOfType<Task<int>>();
    }

    [Fact]
    public void AsyncLazy_ValueCanBeRetrieved()
    {
        // Arrange
        var lazy = new AsyncLazy<int>(() => 42);

        // Act
        var result = lazy.Value.Result;

        // Assert
        result.Should().Be(42);
    }

    [Fact]
    public async Task AsyncLazy_CanBeAwaited()
    {
        // Arrange
        var lazy = new AsyncLazy<int>(() => 42);

        // Act
        var result = await lazy;

        // Assert
        result.Should().Be(42);
    }
}

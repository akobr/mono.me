using System.Linq;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Binding;
using FluentAssertions;
using Xunit;

namespace _42.Platform.Storyteller.Binding.Language.UnitTests;

public class BindingDependenciesTests
{
    [Fact]
    public void NewCollector_IsVolatileUntilAScopeCarriesIt()
    {
        var dependencies = new BindingDependencies();

        dependencies.IsVolatile.Should().BeTrue("nothing guarantees that a resolve recorded into it");
        dependencies.VolatileReason.Should().NotBeNullOrEmpty();

        _ = new BindingScope { Dependencies = dependencies };

        dependencies.IsVolatile.Should().BeFalse();
        dependencies.VolatileReason.Should().BeNull();
    }

    [Fact]
    public void Record_TheSameMarkerAgain_ChangesNothing()
    {
        var dependencies = Attached();

        dependencies.Record(new BindingDependency("kv", "default", "db.host", "\"1\""));
        dependencies.Record(new BindingDependency("kv", "default", "db.host", "\"1\""));

        dependencies.IsVolatile.Should().BeFalse();
        dependencies.ToList().Should().ContainSingle().Which.Should().Be(new BindingDependency("kv", "default", "db.host", "\"1\""));
    }

    [Fact]
    public void Record_AnotherMarkerOfTheSameInput_MakesItVolatile()
    {
        var dependencies = Attached();

        dependencies.Record(new BindingDependency("kv", "default", "db.host", "\"1\""));
        dependencies.Record(new BindingDependency("kv", "default", "db.host", "\"2\""));

        dependencies.IsVolatile.Should().BeTrue();
        dependencies.VolatileReason.Should().Contain("db.host");
        dependencies.ToList().Should().ContainSingle().Which.Marker.Should().Be("\"1\"");
    }

    [Fact]
    public void Record_TheSameKeyOfAnotherKindOrSource_IsAnotherInput()
    {
        var dependencies = Attached();

        dependencies.Record(new BindingDependency("kv", "default", "db.host", "\"1\""));
        dependencies.Record(new BindingDependency("kv", "staging", "db.host", "\"2\""));
        dependencies.Record(new BindingDependency("annotation", "default", "db.host", BindingDependency.Absent));

        dependencies.IsVolatile.Should().BeFalse();
        dependencies.ToList().Should().HaveCount(3);
    }

    [Fact]
    public void MarkVolatile_KeepsTheFirstReason()
    {
        var dependencies = Attached();

        dependencies.MarkVolatile("first");
        dependencies.MarkVolatile("second");

        dependencies.IsVolatile.Should().BeTrue();
        dependencies.VolatileReason.Should().Be("first");
    }

    [Fact]
    public void ToList_IsSortedByKindSourceAndKey()
    {
        var dependencies = Attached();

        dependencies.Record(new BindingDependency("kv", "default", "b", "\"1\""));
        dependencies.Record(new BindingDependency("annotation", "default", "unt.a", "\"2\""));
        dependencies.Record(new BindingDependency("kv", "default", "a", "\"3\""));
        dependencies.Record(new BindingDependency("kv", "Default", "c", "\"4\""));

        dependencies.ToList().Select(dependency => $"{dependency.Kind}/{dependency.Source}/{dependency.Key}").Should().Equal(
            "annotation/default/unt.a",
            "kv/Default/c",
            "kv/default/a",
            "kv/default/b");
    }

    [Fact]
    public async Task Record_FromManyThreads_KeepsEveryInput()
    {
        var dependencies = Attached();

        await Task.WhenAll(Enumerable.Range(0, 200).Select(index => Task.Run(() =>
        {
            dependencies.Record(new BindingDependency("kv", "default", $"key{index % 100}", $"\"{index % 100}\""));
        })));

        dependencies.IsVolatile.Should().BeFalse();
        dependencies.ToList().Should().HaveCount(100);
    }

    private static BindingDependencies Attached()
    {
        var dependencies = new BindingDependencies();
        _ = new BindingScope { Dependencies = dependencies };
        return dependencies;
    }
}

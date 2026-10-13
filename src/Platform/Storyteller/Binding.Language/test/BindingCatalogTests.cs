using System.Linq;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Binding.Language;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace _42.Platform.Storyteller.Binding.Language.UnitTests;

public class BindingCatalogTests
{
    [Fact]
    public void GetCatalog_NothingRegistered_IsEmpty()
    {
        var catalog = new BindingExecutor().GetCatalog();

        catalog.Functions.Should().BeEmpty();
        catalog.Sources.Should().BeEmpty();
    }

    [Fact]
    public void GetCatalog_ListsFunctionsAndSourcesSortedByName()
    {
        var executor = new BindingExecutor();
        executor.RegisterFunction("zeta", new PlainFunction());
        executor.RegisterFunction("config", new ConfigBindingFunction());
        executor.RegisterFunction("Alpha", new PlainFunction());
        executor.RegisterSource("vault", new DescribedSource());
        executor.RegisterSource(BindingExecutor.DefaultSourceKey, new PlainSource());

        var catalog = executor.GetCatalog();

        catalog.Functions.Select(function => function.Name).Should().Equal("Alpha", "config", "zeta");
        catalog.Sources.Select(source => source.Name).Should().Equal("default", "vault");
    }

    [Fact]
    public void GetCatalog_UndescribedBinding_IsListedByNameOnly()
    {
        var executor = new BindingExecutor();
        executor.RegisterFunction("custom", new PlainFunction());
        executor.RegisterSource(BindingExecutor.DefaultSourceKey, new PlainSource());

        var catalog = executor.GetCatalog();

        var function = catalog.Functions.Should().ContainSingle().Subject;
        function.Name.Should().Be("custom");
        function.Summary.Should().BeEmpty();
        function.Signature.Should().BeNull();
        function.Parameters.Should().BeEmpty();
        function.Examples.Should().BeEmpty();

        var source = catalog.Sources.Should().ContainSingle().Subject;
        source.IsDefault.Should().BeTrue();
        source.IsSecret.Should().BeFalse();
        source.Summary.Should().BeEmpty();
    }

    [Fact]
    public void GetCatalog_DescribedSource_UsesItsDescriptionWithTheRegisteredKey()
    {
        var executor = new BindingExecutor();
        executor.RegisterSource("primaryVault", new DescribedSource());

        var source = executor.GetCatalog().Sources.Should().ContainSingle().Subject;

        source.Name.Should().Be("primaryVault");
        source.IsDefault.Should().BeFalse();
        source.IsSecret.Should().BeTrue();
        source.Summary.Should().Be("Secrets of primaryVault.");
        source.Examples.Should().ContainSingle().Which.Expression.Should().Be("@(db.password, primaryVault)");
    }

    [Fact]
    public void ConfigFunction_DescribesItselfUnderTheRegisteredName()
    {
        var executor = new BindingExecutor();
        executor.RegisterFunction("cfg", new ConfigBindingFunction());

        var function = executor.GetCatalog().Functions.Should().ContainSingle().Subject;

        function.Name.Should().Be("cfg");
        function.Summary.Should().NotBeEmpty();
        function.Signature.Should().Be("cfg(\"<expr>\")");
        function.Parameters.Should().ContainSingle().Which.Name.Should().Be("expr");
        function.Examples.Select(example => example.Expression).Should().Equal("@cfg(\"/maxPrice\")", "@cfg(\"$.endpoints.db\")");
    }

    [Fact]
    public async Task ConfigFunction_ExamplesParseAndResolve()
    {
        var executor = new BindingExecutor();
        executor.RegisterFunction("config", new ConfigBindingFunction());
        var scope = new BindingScope { Document = JObject.Parse("""{ "maxPrice": 10, "endpoints": { "db": "sql://db" } }""") };
        var examples = executor.GetCatalog().Functions.Single().Examples;
        var resolved = new JArray(examples.Select(example => new JValue(example.Expression)));

        foreach (var value in resolved.Children<JValue>().ToList())
        {
            (await executor.TryBinding(value, includeSecrets: false, scope)).Should().BeTrue();
        }

        resolved.ToString(Newtonsoft.Json.Formatting.None).Should().Be("""[10,"sql://db"]""");
    }

    private sealed class PlainFunction : IBindingFunction
    {
        public ValueTask<BindingValue?> InvokeAsync(BindingFunctionRequest request)
        {
            return new ValueTask<BindingValue?>((BindingValue?)null);
        }
    }

    private sealed class PlainSource : IBindingSource
    {
        public ValueTask<BindingValue?> ResolveAsync(BindingRequest request)
        {
            return new ValueTask<BindingValue?>((BindingValue?)null);
        }
    }

    private sealed class DescribedSource : IBindingSource, IDescribedBinding
    {
        public ValueTask<BindingValue?> ResolveAsync(BindingRequest request)
        {
            return new ValueTask<BindingValue?>((BindingValue?)null);
        }

        public BindingDescription Describe(string name)
        {
            return new BindingDescription
            {
                Summary = $"Secrets of {name}.",
                IsSecret = true,
                Examples = [new BindingExample { Expression = $"@(db.password, {name})", Description = "A secret." }],
            };
        }
    }
}

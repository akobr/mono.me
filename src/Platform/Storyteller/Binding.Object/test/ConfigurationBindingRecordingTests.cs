using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Binding;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using static _42.Platform.Storyteller.Binding.Object.UnitTests.Fixtures;

namespace _42.Platform.Storyteller.Binding.Object.UnitTests;

// What the resolver adds to the manifest of a resolve: JSON-e and a string executor that does not record make it volatile.
public class ConfigurationBindingRecordingTests
{
    private readonly BindingDependencies _dependencies = new();
    private readonly BindingScope _scope;

    public ConfigurationBindingRecordingTests()
    {
        _scope = new BindingScope { Dependencies = _dependencies };
    }

    [Fact]
    public void Resolver_Records()
    {
        new ConfigurationBindingResolver(new RecordingStringBinding()).Should().BeAssignableTo<IRecordingBinding>();
    }

    [Fact]
    public async Task Resolve_JsonE_MakesItVolatile()
    {
        var content = Envelope("jsone", new JObject { ["$eval"] = "1 + 1" });

        await new ConfigurationBindingResolver(new RecordingStringBinding()).ResolveAsync(content, includeSecrets: true, _scope);

        content["value"]!.Value<decimal>().Should().Be(2);
        _dependencies.IsVolatile.Should().BeTrue();
        _dependencies.VolatileReason.Should().Contain("JSON-e");
    }

    [Fact]
    public async Task Resolve_JsonLogic_StaysNonVolatile()
    {
        var content = Envelope("jlogic", new JObject { ["+"] = new JArray(1, 2) });

        await new ConfigurationBindingResolver(new RecordingStringBinding()).ResolveAsync(content, includeSecrets: true, _scope);

        content["value"]!.Value<decimal>().Should().Be(3);
        _dependencies.IsVolatile.Should().BeFalse();
        _dependencies.ToList().Should().BeEmpty();
    }

    [Fact]
    public async Task Resolve_StringsOfTheDocumentTheContextAndThePostPass_RecordIntoOneCollector()
    {
        var content = Parse("""
            {
              "name": "@name",
              "value": {
                "$binding": "jlogic",
                "$definition": { "if": [{ "var": "on" }, "@chosen", "@other"] },
                "$context": { "on": "@flag" }
              }
            }
            """);
        var strings = new RecordingStringBinding();

        await new ConfigurationBindingResolver(strings).ResolveAsync(content, includeSecrets: true, _scope);

        _dependencies.IsVolatile.Should().BeFalse();
        _dependencies.ToList().Select(dependency => dependency.Key).Should().BeEquivalentTo("@name", "@flag", "@chosen");
        strings.Scopes.Should().OnlyContain(scope => ReferenceEquals(scope, _scope));
    }

    [Fact]
    public async Task Resolve_StringExecutorThatDoesNotRecord_MakesItVolatile()
    {
        var content = Parse("""{ "name": "@name" }""");

        await new ConfigurationBindingResolver(new FakeStringBinding()).ResolveAsync(content, includeSecrets: true, _scope);

        _dependencies.IsVolatile.Should().BeTrue();
    }

    [Fact]
    public async Task Resolve_StringExecutorThatDoesNotRecord_WithoutBindingStrings_StaysNonVolatile()
    {
        var content = Parse("""{ "name": "plain", "value": { "$binding": "jlogic", "$definition": 1 } }""");

        await new ConfigurationBindingResolver(new FakeStringBinding()).ResolveAsync(content, includeSecrets: true, _scope);

        _dependencies.IsVolatile.Should().BeFalse();
    }

    [Fact]
    public async Task Resolve_WithoutACollector_ResolvesAsBefore()
    {
        var content = Envelope("jsone", new JObject { ["$eval"] = "1 + 1" });

        await new ConfigurationBindingResolver(new FakeStringBinding()).ResolveAsync(content, includeSecrets: true, new BindingScope());

        content["value"]!.Value<decimal>().Should().Be(2);
    }

    // Binds "@x" to "resolved:@x" and records each binding string as an input, like a recording executor would.
    private sealed class RecordingStringBinding : IBindingExecutor, IRecordingBinding
    {
        public List<BindingScope?> Scopes { get; } = new();

        public ValueTask<bool> TryBinding(JProperty property, bool includeSecrets, BindingScope? scope = null)
        {
            return BindAsync(property.Value, scope, token => property.Value = token);
        }

        public ValueTask<bool> TryBinding(JValue value, bool includeSecrets, BindingScope? scope = null)
        {
            return BindAsync(value, scope, token => value.Replace(token));
        }

        private ValueTask<bool> BindAsync(JToken current, BindingScope? scope, Action<JToken> apply)
        {
            var raw = current.Value<string>();
            if (string.IsNullOrEmpty(raw) || raw[0] != '@')
            {
                return new ValueTask<bool>(false);
            }

            Scopes.Add(scope);
            scope?.Dependencies?.Record(new BindingDependency("fake", "test", raw, "\"1\""));
            apply(new JValue("resolved:" + raw));
            return new ValueTask<bool>(true);
        }
    }
}

using System;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Binding;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using static _42.Platform.Storyteller.Binding.Object.UnitTests.Fixtures;

namespace _42.Platform.Storyteller.Binding.Object.UnitTests;

// ContainsBindings decides whether a resolved read may skip the resolver, so it must find everything the resolver changes.
public class ConfigurationBindingDetectionTests
{
    private readonly ConfigurationBindingResolver _resolver = new(new FailingStringBinding());

    [Theory]
    [InlineData("""{ "host": "@db.host" }""")]
    [InlineData("""{ "url": "@[https://@api.host/v1]" }""")]
    [InlineData("""{ "nested": { "deeper": [1, { "value": "@(db.password, secret)" }] } }""")]
    [InlineData("""{ "list": ["plain", "@config(\"/plan\")"] }""")]
    [InlineData("""{ "rule": { "$binding": "jlogic", "$definition": { "+": [1, 2] } } }""")]
    [InlineData("""{ "$binding": "jsone", "$definition": { "a": 1 } }""")]
    [InlineData("""{ "unknown": { "$binding": "other" } }""")]
    [InlineData("""{ "invalid": { "$binding": "jlogic", "$definition": 1, "extra": true } }""")]
    public void ContainsBindings_ABindingAnywhere_IsTrue(string json)
    {
        _resolver.ContainsBindings(Parse(json)).Should().BeTrue();
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{ "plan": "enterprise", "retries": 3, "enabled": true, "ratio": 0.5, "none": null }""")]
    [InlineData("""{ "@host": "db.example", "mail": "ops@example.com", "list": [" @not-first", ""] }""")]
    [InlineData("""{ "rule": { "$binding": 1, "$definition": { "+": [1, 2] } } }""")]
    [InlineData("""{ "rule": { "binding": "jlogic", "$definition": { "+": [1, 2] } } }""")]
    public void ContainsBindings_NoBinding_IsFalse(string json)
    {
        _resolver.ContainsBindings(Parse(json)).Should().BeFalse();
    }

    [Fact]
    public void ContainsBindings_AStringToken_FollowsTheFirstCharacter()
    {
        _resolver.ContainsBindings(new JValue("@db.host")).Should().BeTrue();
        _resolver.ContainsBindings(new JValue("db.host")).Should().BeFalse();
        _resolver.ContainsBindings(new JValue(42)).Should().BeFalse();
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{ "plan": "enterprise", "retries": 3, "tags": ["a", "b"], "nested": { "mail": "ops@example.com", "none": null } }""")]
    [InlineData("""{ "@host": "db.example", "rule": { "$binding": 1, "$definition": { "+": [1, 2] } } }""")]
    public async Task Resolve_ADocumentWithoutBindings_StaysAsStored(string json)
    {
        var content = Parse(json);
        var stored = content.DeepClone();

        _resolver.ContainsBindings(content).Should().BeFalse();
        await _resolver.ResolveAsync(content, includeSecrets: true, new BindingScope { Document = stored.DeepClone() });

        JToken.DeepEquals(content, stored).Should().BeTrue();
    }

    [Fact]
    public void ContainsBindings_ByDefault_IsTrue()
    {
        IConfigurationBindingResolver resolver = new OpaqueResolver();

        resolver.ContainsBindings(new JObject()).Should().BeTrue();
    }

    // Fails the test when the resolver tries to bind a string.
    private sealed class FailingStringBinding : IBindingExecutor
    {
        public ValueTask<bool> TryBinding(JProperty property, bool includeSecrets, BindingScope? scope = null)
        {
            throw new InvalidOperationException($"'{property.Path}' must not be bound.");
        }

        public ValueTask<bool> TryBinding(JValue value, bool includeSecrets, BindingScope? scope = null)
        {
            throw new InvalidOperationException($"'{value.Path}' must not be bound.");
        }
    }

    // A replacement resolver that does not say which documents it changes.
    private sealed class OpaqueResolver : IConfigurationBindingResolver
    {
        public ValueTask ResolveAsync(JObject content, bool includeSecrets, BindingScope scope)
        {
            return ValueTask.CompletedTask;
        }
    }
}

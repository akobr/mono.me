#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Binding;
using _42.Platform.Storyteller.Configuring;
using _42.Platform.Storyteller.Json;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// The resolve step of the resolved read, on a configuration already read; no Cosmos DB needed.
public class ConfigurationResolutionTests
{
    private static readonly FullKey Key = FullKey.Create(AnnotationKey.CreateResponsibility("billing"), "acme", "main", "default");

    [Fact]
    public async Task ResolveConfigurationAsync_EvaluatesInPlaceAndKeepsTheEffectiveFields()
    {
        var resolver = new RecordingResolver(content => content["host"] = "db.example");
        var service = CreateService(resolver);
        var effective = Effective(new JObject { ["host"] = "@db.host" });

        var resolved = await service.ResolveConfigurationAsync(Key, effective, includeSecrets: true);

        resolved.Should().BeSameAs(effective);
        resolved.Content["host"]!.Value<string>().Should().Be("db.example");
        resolved.Version.Should().Be(3);
        resolved.Hash.Should().Be("0badc0de");
        var call = resolver.Calls.Should().ContainSingle().Subject;
        call.IncludeSecrets.Should().BeTrue();
        call.Scope.Context.Should().Be(new ConfigurationBindingContext(Key));
        call.Scope.Document!["host"]!.Value<string>().Should().Be("@db.host", "the snapshot is a copy taken before the pass");
    }

    [Fact]
    public async Task ResolveConfigurationAsync_PassesTheSecretRights()
    {
        var resolver = new RecordingResolver(_ => { });
        var service = CreateService(resolver);

        await service.ResolveConfigurationAsync(Key, Effective(new JObject { ["host"] = "@db.host" }));

        resolver.Calls.Should().ContainSingle().Which.IncludeSecrets.Should().BeFalse();
    }

    [Fact]
    public async Task WithoutAResolver_TheEffectiveDocumentIsTheResolvedOne()
    {
        var service = CreateService(resolver: null);
        var effective = Effective(new JObject { ["host"] = "@db.host" });

        service.HasBindings(effective).Should().BeFalse();
        (await service.ResolveConfigurationAsync(Key, effective, includeSecrets: true)).Content["host"]!.Value<string>().Should().Be("@db.host");
    }

    [Fact]
    public void HasBindings_AsksTheResolver()
    {
        var resolver = new RecordingResolver(_ => { }) { HasBindings = content => content.SelectToken("host") is not null };
        var service = CreateService(resolver);

        service.HasBindings(Effective(new JObject { ["host"] = "@db.host" })).Should().BeTrue();
        service.HasBindings(Effective(new JObject { ["plan"] = "enterprise" })).Should().BeFalse();
    }

    private static CosmosConfigurationService CreateService(IConfigurationBindingResolver? resolver)
    {
        var options = Options.Create(new JsonSerializerSettings());

        // the resolve step never touches the repositories
        return new CosmosConfigurationService(null!, new JsonSerializationSettingsService(options), options, resolver!);
    }

    private static Configuration Effective(JObject content)
    {
        return new Configuration
        {
            AnnotationKey = Key.Annotation,
            Version = 3,
            Content = content,
            Modified = AuditStamp.Of(AuditActor.System, DateTimeOffset.UnixEpoch),
            Hash = "0badc0de",
        };
    }

    private sealed class RecordingResolver(Action<JObject> resolve) : IConfigurationBindingResolver
    {
        public List<(bool IncludeSecrets, BindingScope Scope)> Calls { get; } = [];

        public Func<JToken, bool> HasBindings { get; init; } = _ => true;

        public ValueTask ResolveAsync(JObject content, bool includeSecrets, BindingScope scope)
        {
            Calls.Add((includeSecrets, scope));
            resolve(content);
            return ValueTask.CompletedTask;
        }

        public bool ContainsBindings(JToken content) => HasBindings(content);
    }
}

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Binding;
using _42.Platform.Storyteller.Configuring;
using _42.Platform.Storyteller.KeyValues;
using FluentAssertions;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// What the key/value binding source records into the manifest of a resolve; no Cosmos DB needed.
public class KeyValueBindingSourceRecordingTests
{
    private static readonly FullKey Key = FullKey.Create(AnnotationKey.CreateResponsibility("billing"), "acme", "main", "default");

    [Fact]
    public void Source_Records()
    {
        Source(new MarkingKeyValueService(), KeyValueSourceKind.Default).Should().BeAssignableTo<IRecordingBinding>();
    }

    [Fact]
    public async Task ResolveAsync_AHit_RecordsTheMarkerOfThePair()
    {
        var service = new MarkingKeyValueService().With("DB.Host", KeyValueProtection.Public, "db-1", "\"7\"");
        var dependencies = Recording();

        var value = await Source(service, KeyValueSourceKind.Default).ResolveAsync(Request("DB.Host", includeSecrets: false, dependencies));

        value!.Token.ToString().Should().Be("db-1");
        dependencies.IsVolatile.Should().BeFalse();
        dependencies.ToList().Should().ContainSingle().Which.Should().Be(
            new BindingDependency(BindingDependencyKinds.KeyValue, "default", "db.host", "\"7\""));
    }

    [Fact]
    public async Task ResolveAsync_AMissingPair_RecordsAbsent()
    {
        var dependencies = Recording();

        var value = await Source(new MarkingKeyValueService(), KeyValueSourceKind.Default).ResolveAsync(Request("db.host", includeSecrets: true, dependencies));

        value.Should().BeNull();
        dependencies.ToList().Should().ContainSingle().Which.Marker.Should().Be(BindingDependency.Absent);
    }

    [Theory]
    [InlineData(KeyValueSourceKind.Secret, KeyValueProtection.Public, true)]
    [InlineData(KeyValueSourceKind.Public, KeyValueProtection.Standard, true)]
    [InlineData(KeyValueSourceKind.Default, KeyValueProtection.Standard, false)]
    public async Task ResolveAsync_APairThatIsNotBound_StillRecordsItsMarker(KeyValueSourceKind kind, KeyValueProtection protection, bool includeSecrets)
    {
        // a protection change of a declined or hidden pair changes what binds, so its marker belongs to the manifest
        var service = new MarkingKeyValueService().With("db.host", protection, "db-1", "\"7\"");
        var dependencies = Recording();

        var value = await Source(service, kind).ResolveAsync(Request("db.host", includeSecrets, dependencies));

        value.Should().BeNull();
        dependencies.IsVolatile.Should().BeFalse();
        dependencies.ToList().Should().ContainSingle().Which.Marker.Should().Be("\"7\"");
    }

    [Fact]
    public async Task ResolveAsync_AMemoHit_RecordsAgain()
    {
        var service = new MarkingKeyValueService().With("db.host", KeyValueProtection.Public, "db-1", "\"7\"");
        var source = Source(service, KeyValueSourceKind.Default);
        var context = new ConfigurationBindingContext(Key);
        var dependencies = Recording();

        await source.ResolveAsync(Request("db.host", includeSecrets: true, dependencies: null, context));
        await source.ResolveAsync(Request("db.host", includeSecrets: true, dependencies, context));

        service.Reads.Should().Be(1);
        dependencies.ToList().Should().ContainSingle().Which.Marker.Should().Be("\"7\"");
    }

    [Fact]
    public async Task ResolveAsync_TwoVersionsOfAPairInOneRead_MakeItVolatile()
    {
        // the secret and the public lookup of one key are two reads; a write between them gives two markers
        var service = new MarkingKeyValueService().With("db.host", KeyValueProtection.Public, "db-1", "\"7\"");
        service.MarkerOfPublicReads = "\"8\"";
        var context = new ConfigurationBindingContext(Key);
        var dependencies = Recording();

        await Source(service, KeyValueSourceKind.Default).ResolveAsync(Request("db.host", includeSecrets: true, dependencies, context));
        await Source(service, KeyValueSourceKind.Public).ResolveAsync(Request("db.host", includeSecrets: true, dependencies, context));

        dependencies.IsVolatile.Should().BeTrue();
    }

    [Fact]
    public async Task ResolveAsync_AServiceWithoutMarkers_MakesItVolatile()
    {
        var service = new PlainKeyValueService();
        var dependencies = Recording();

        var value = await Source(service, KeyValueSourceKind.Default).ResolveAsync(Request("db.host", includeSecrets: true, dependencies));

        value!.Token.ToString().Should().Be("db-1");
        dependencies.IsVolatile.Should().BeTrue();
        dependencies.ToList().Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveAsync_OutsideAConfigurationRead_ReadsAndRecordsNothing()
    {
        var service = new MarkingKeyValueService().With("db.host", KeyValueProtection.Public, "db-1", "\"7\"");
        var dependencies = Recording();
        var request = new BindingRequest { Path = ["db", "host"], IncludeSecrets = true, Dependencies = dependencies };

        var value = await Source(service, KeyValueSourceKind.Default).ResolveAsync(request);

        value.Should().BeNull();
        service.Reads.Should().Be(0);
        dependencies.IsVolatile.Should().BeFalse();
        dependencies.ToList().Should().BeEmpty();
    }

    private static KeyValueBindingSource Source(IKeyValueService service, KeyValueSourceKind kind)
    {
        return new KeyValueBindingSource(new Lazy<IKeyValueService>(() => service), kind);
    }

    private static BindingDependencies Recording()
    {
        return new BindingScope { Dependencies = new BindingDependencies() }.Dependencies!;
    }

    private static BindingRequest Request(string key, bool includeSecrets, BindingDependencies? dependencies, ConfigurationBindingContext? context = null)
    {
        return new BindingRequest
        {
            Path = key.Split('.'),
            IncludeSecrets = includeSecrets,
            Context = context ?? new ConfigurationBindingContext(Key),
            Dependencies = dependencies,
        };
    }

    // Pairs in memory with a marker each, the rules of CosmosKeyValueService.LookupWithMarkerAsync.
    private sealed class MarkingKeyValueService : KeyValueServiceBase, IKeyValueService
    {
        private readonly Dictionary<string, (KeyValueProtection Protection, string Value, string Marker)> _pairs = new(StringComparer.OrdinalIgnoreCase);

        public int Reads { get; private set; }

        // when set, a read without secrets sees this marker, as if the pair was written between two reads
        public string? MarkerOfPublicReads { get; set; }

        public MarkingKeyValueService With(string key, KeyValueProtection protection, string value, string marker)
        {
            _pairs[key] = (protection, value, marker);
            return this;
        }

        public Task<MarkedRead<KeyValueLookup>> LookupWithMarkerAsync(string organization, string project, string view, string key, bool includeSecrets)
        {
            Reads++;

            if (!_pairs.TryGetValue(key, out var pair))
            {
                return Task.FromResult(new MarkedRead<KeyValueLookup>(null, BindingDependency.Absent));
            }

            var marker = !includeSecrets && MarkerOfPublicReads is not null ? MarkerOfPublicReads : pair.Marker;
            var lookup = pair.Protection.IsSecret() && !includeSecrets ? null : new KeyValueLookup(key, pair.Protection, pair.Value, null);
            return Task.FromResult(new MarkedRead<KeyValueLookup>(lookup, marker));
        }
    }

    // Keeps the default member of the interface, which cannot tell the marker.
    private sealed class PlainKeyValueService : KeyValueServiceBase
    {
        public override Task<KeyValueLookup?> LookupAsync(string organization, string project, string view, string key, bool includeSecrets)
        {
            return Task.FromResult<KeyValueLookup?>(new KeyValueLookup(key, KeyValueProtection.Public, "db-1", null));
        }
    }

    private abstract class KeyValueServiceBase : IKeyValueService
    {
        public Task<KeyValuesResponse> ListAsync(string organization, string project, string view, KeyValueListRequest request) => throw new NotSupportedException();

        public Task<KeyValue?> GetAsync(string organization, string project, string view, string key, KeyValueReadOptions options, KeyValueCaller caller) => throw new NotSupportedException();

        public Task<KeyValue> SetAsync(string organization, string project, string view, string key, KeyValueWrite write, KeyValueCaller caller) => throw new NotSupportedException();

        public Task<bool> DeleteAsync(string organization, string project, string view, string key, KeyValueDeleteOptions options, KeyValueCaller caller) => throw new NotSupportedException();

        public Task<KeyValueBulkResult> RestoreAsync(string organization, string project, string view, KeyValueBulkRequest request, KeyValueCaller caller) => throw new NotSupportedException();

        public Task<KeyValueBulkResult> PurgeAsync(string organization, string project, string view, KeyValueBulkRequest request, KeyValueCaller caller) => throw new NotSupportedException();

        public Task<KeyValueBulkResult> ProtectAsync(string organization, string project, string view, KeyValueBulkRequest request, KeyValueCaller caller) => throw new NotSupportedException();

        public Task<IReadOnlyList<KeyValueVersion>> GetVersionsAsync(string organization, string project, string view, string key) => throw new NotSupportedException();

        public Task<KeyValue?> GetVersionAsync(string organization, string project, string view, string key, ulong version, bool reveal, KeyValueCaller caller) => throw new NotSupportedException();

        public Task<DiffResult> GetVersionChangesAsync(string organization, string project, string view, string key, ulong fromVersion, ulong toVersion, KeyValueCaller caller) => throw new NotSupportedException();

        public Task<KeyValueDocument> ExportAsync(string organization, string project, string view, KeyValueExportRequest request, KeyValueCaller caller) => throw new NotSupportedException();

        public Task<KeyValueImportResult> ImportAsync(string organization, string project, string view, KeyValueDocument document, KeyValueImportOptions options, KeyValueCaller caller) => throw new NotSupportedException();

        public virtual Task<KeyValueLookup?> LookupAsync(string organization, string project, string view, string key, bool includeSecrets) => throw new NotSupportedException();
    }
}

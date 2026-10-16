using _42.Platform.Storyteller.Configuring;
using _42.Platform.Storyteller.Transferring;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Backend.Core.UnitTests.Transferring;

// Only the member an export uses.
internal sealed class FakeSchemaService(ExportReads? reads = null) : IConfigurationSchemaService
{
    public IReadOnlyList<SchemaHistory> Histories { get; set; } = [];

    public Task<IReadOnlyList<SchemaHistory>> GetSchemaHistoriesAsync(string organization, string project, string view, CancellationToken cancellationToken = default)
    {
        reads?.Add(ViewArchiveContent.Schemas, organization, project, view);
        return Task.FromResult(Histories);
    }

    public Task<ConfigurationSchema?> GetSchemaAsync(string organization, string project, string view, string annotationType) => throw new NotSupportedException();

    public Task<IReadOnlyList<ConfigurationSchemaSummary>> ListSchemasAsync(string organization, string project, string view) => throw new NotSupportedException();

    public Task<ConfigurationSchema> SetSchemaAsync(string organization, string project, string view, string annotationType, JObject schemaContent, AuditActor actor, bool force) => throw new NotSupportedException();

    public Task<bool> DeleteSchemaAsync(string organization, string project, string view, string annotationType) => throw new NotSupportedException();

    public Task<ConfigurationSchema?> GetAnnotationSchemaAsync(string organization, string project, string view, string annotationKey) => throw new NotSupportedException();

    public Task<ConfigurationSchema> SetAnnotationSchemaAsync(string organization, string project, string view, string annotationKey, JObject schemaContent, AuditActor actor, bool force) => throw new NotSupportedException();

    public Task<bool> DeleteAnnotationSchemaAsync(string organization, string project, string view, string annotationKey) => throw new NotSupportedException();

    public Task<ConfigurationSchema?> GetDescendantTypeSchemaAsync(string organization, string project, string view, string annotationKey, string descendantTypeCode) => throw new NotSupportedException();

    public Task<ConfigurationSchema> SetDescendantTypeSchemaAsync(string organization, string project, string view, string annotationKey, string descendantTypeCode, JObject schemaContent, AuditActor actor, bool force) => throw new NotSupportedException();

    public Task<bool> DeleteDescendantTypeSchemaAsync(string organization, string project, string view, string annotationKey, string descendantTypeCode) => throw new NotSupportedException();

    public Task<CombinedConfigurationSchema?> GetCombinedSchemaAsync(string organization, string project, string view, string annotationKey) => throw new NotSupportedException();

    public Task ValidateContentAsync(string organization, string project, string view, string annotationKey, JObject content) => throw new NotSupportedException();

    public Task<IReadOnlyCollection<ConfigurationVersion>> GetSchemaVersionsAsync(string organization, string project, string view, string annotationType) => throw new NotSupportedException();

    public Task<ConfigurationSchema?> GetSchemaVersionContentAsync(string organization, string project, string view, string annotationType, uint version) => throw new NotSupportedException();

    public Task<DiffResult> GetSchemaVersionChangesAsync(string organization, string project, string view, string annotationType, uint version) => throw new NotSupportedException();

    public Task<DiffResult> GetSchemaVersionChangesAsync(string organization, string project, string view, string annotationType, uint fromVersion, uint toVersion) => throw new NotSupportedException();

    public Task<IReadOnlyCollection<ConfigurationVersion>> GetAnnotationSchemaVersionsAsync(string organization, string project, string view, string annotationKey) => throw new NotSupportedException();

    public Task<ConfigurationSchema?> GetAnnotationSchemaVersionContentAsync(string organization, string project, string view, string annotationKey, uint version) => throw new NotSupportedException();

    public Task<DiffResult> GetAnnotationSchemaVersionChangesAsync(string organization, string project, string view, string annotationKey, uint version) => throw new NotSupportedException();

    public Task<DiffResult> GetAnnotationSchemaVersionChangesAsync(string organization, string project, string view, string annotationKey, uint fromVersion, uint toVersion) => throw new NotSupportedException();

    public Task<IReadOnlyCollection<ConfigurationVersion>> GetDescendantTypeSchemaVersionsAsync(string organization, string project, string view, string annotationKey, string descendantTypeCode) => throw new NotSupportedException();

    public Task<ConfigurationSchema?> GetDescendantTypeSchemaVersionContentAsync(string organization, string project, string view, string annotationKey, string descendantTypeCode, uint version) => throw new NotSupportedException();

    public Task<DiffResult> GetDescendantTypeSchemaVersionChangesAsync(string organization, string project, string view, string annotationKey, string descendantTypeCode, uint version) => throw new NotSupportedException();

    public Task<DiffResult> GetDescendantTypeSchemaVersionChangesAsync(string organization, string project, string view, string annotationKey, string descendantTypeCode, uint fromVersion, uint toVersion) => throw new NotSupportedException();
}

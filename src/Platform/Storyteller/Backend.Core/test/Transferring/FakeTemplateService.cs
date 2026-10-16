using _42.Platform.Storyteller.Configuring;
using _42.Platform.Storyteller.Transferring;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Backend.Core.UnitTests.Transferring;

// Only the member an export uses.
internal sealed class FakeTemplateService(ExportReads? reads = null) : IConfigurationTemplateService
{
    public IReadOnlyList<TemplateHistory> Histories { get; set; } = [];

    public Task<IReadOnlyList<TemplateHistory>> GetTemplateHistoriesAsync(string organization, string project, string view, CancellationToken cancellationToken = default)
    {
        reads?.Add(ViewArchiveContent.Templates, organization, project, view);
        return Task.FromResult(Histories);
    }

    public Task<ConfigurationTemplate?> GetTemplateAsync(string organization, string project, string view, string annotationType) => throw new NotSupportedException();

    public Task<IReadOnlyList<ConfigurationTemplateSummary>> ListTemplatesAsync(string organization, string project, string view) => throw new NotSupportedException();

    public Task<ConfigurationTemplate> CreateOrUpdateTemplateAsync(string organization, string project, string view, string annotationType, JObject value, AuditActor actor) => throw new NotSupportedException();

    public Task<ConfigurationTemplate> PatchTemplateAsync(string organization, string project, string view, string annotationType, JArray patchOperations, AuditActor actor) => throw new NotSupportedException();

    public Task<bool> DeleteTemplateAsync(string organization, string project, string view, string annotationType) => throw new NotSupportedException();

    public Task<IReadOnlyCollection<ConfigurationVersion>> GetTemplateVersionsAsync(string organization, string project, string view, string annotationType) => throw new NotSupportedException();

    public Task<ConfigurationTemplate?> GetTemplateVersionContentAsync(string organization, string project, string view, string annotationType, uint version) => throw new NotSupportedException();

    public Task<DiffResult> GetTemplateVersionChangesAsync(string organization, string project, string view, string annotationType, uint version) => throw new NotSupportedException();

    public Task<DiffResult> GetTemplateVersionChangesAsync(string organization, string project, string view, string annotationType, uint fromVersion, uint toVersion) => throw new NotSupportedException();
}

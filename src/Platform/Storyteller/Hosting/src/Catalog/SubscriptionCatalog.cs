using _42.Platform.Storyteller.Sdk.Configuring;
using _42.Platform.Storyteller.Sdk.Configuring.Settings;
using _42.Platform.Storyteller.Sdk.Hosting.Reading;

namespace _42.Platform.Storyteller.Sdk.Hosting.Catalog;

// The executions of the view of the options' Key, through point reads of the catalog's transport.
internal sealed class SubscriptionCatalog : ISubscriptionCatalog
{
    private readonly StorytellerSettings _settings;
    private readonly CatalogReader _reader;

    public SubscriptionCatalog(StorytellerSettings settings, CatalogReader reader)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(reader);

        _settings = settings;
        _reader = reader;
    }

    // In any status, deleted ones included, so a deleted execution is told from a failure; it returns null as a missing
    // one does.
    public async Task<StorytellerExecution?> GetExecutionAsync(string key, CancellationToken cancellationToken = default)
    {
        var fullKey = CatalogKeys.Parse(key, _settings.Key, nameof(key));

        if (fullKey.Annotation.Type != AnnotationType.Execution)
        {
            throw new ArgumentException(
                $"'{fullKey}' does not name an execution, exe.{{subject}}.{{responsibility}}.{{context}}.",
                nameof(key));
        }

        try
        {
            var annotation = await _reader.ReadAsync(fullKey, includeDeleted: true, CancellationToken.None, cancellationToken).ConfigureAwait(false);
            return annotation.DeletedAt is null ? (StorytellerExecution)annotation : null;
        }
        catch (StorytellerConfigurationException exception) when (CatalogReader.IsMissing(exception))
        {
            return null;
        }
    }
}

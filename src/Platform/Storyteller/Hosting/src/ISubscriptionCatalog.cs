namespace _42.Platform.Storyteller.Sdk.Hosting;

// The executions of the view of the options' Key, read through the platform's annotations. Needs Annotation.Read.
public interface ISubscriptionCatalog
{
    // One execution, in any status; null when it does not exist or is deleted. key: a full key, or an annotation key in
    // the organization, project and view of the options' Key; without that Key, an annotation key throws
    // StorytellerConfigurationException naming STORYTELLER_KEY.
    Task<StorytellerExecution?> GetExecutionAsync(string key, CancellationToken cancellationToken = default);
}

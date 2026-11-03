using _42.Platform.Storyteller.ConfigurationFormats;
using _42.Platform.Storyteller.Sdk.Configuring.Reading;
using _42.Platform.Storyteller.Sdk.Configuring.References;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Sdk.Configuring.Engine;

// Step 3 of the apply pipeline, shared by the engine and the document service: flatten and validate the server's
// content, then resolve its references. The server's document itself is never changed: the cache keeps it as it came.
internal static class ContentApplication
{
    // reuse: an earlier result of the same document, whose fresh values are reused.
    public static async Task<(IReadOnlyDictionary<string, string> Data, SubstitutionResult? Substitution)> ApplyAsync(
        ReferenceSubstitution substitution,
        DocumentIdentity identity,
        Configuration document,
        RunScope scope,
        TimeSpan readTimeout,
        SubstitutionResult? reuse = null)
    {
        var content = document.Content switch
        {
            null => null,
            JToken token => token,
            var other => JToken.FromObject(other),
        };

        try
        {
            var data = DocumentFlattener.Flatten(content);

            if (content is not JObject serverContent)
            {
                return (data, null);
            }

            var result = await substitution.SubstituteAsync(serverContent, reuse, scope.Token).ConfigureAwait(false);

            return ReferenceEquals(result.Content, serverContent)
                ? (data, result)
                : (DocumentFlattener.Flatten(result.Content), result);
        }
        catch (InvalidDocumentException exception)
        {
            throw new StorytellerConfigurationException(
                $"The configuration {identity.Key} is not valid, so it is not applied: {exception.Message}",
                identity.Key,
                null,
                null,
                false,
                exception);
        }
        catch (SecretResolutionException exception)
        {
            // Names the path, the vault and the secret; the resolver's message never holds a value.
            throw new StorytellerConfigurationException(
                $"The configuration {identity.Key} is not applied. {exception.Message}",
                identity.Key,
                null,
                null,
                exception.IsTransient,
                exception);
        }
        catch (OperationCanceledException exception) when (scope.IsTimedOut)
        {
            // Only a resolution waits here: a transient resolver failure, within the ReadTimeout of the whole run.
            throw new StorytellerConfigurationException(
                $"Resolving the secret references of {identity.Key} did not finish within the ReadTimeout of {readTimeout}.",
                identity.Key,
                null,
                null,
                true,
                exception);
        }
    }
}

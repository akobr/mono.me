using System.Linq;
using System.Net;
using System.Threading.Tasks;
using _42.Platform.Cli.Json;
using _42.Platform.Cli.Model;
using _42.Platform.Storyteller.Sdk;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Cli.Services;

public static class StorytellerApiExtensions
{
    /// <summary>
    /// Gets the stored content of a configuration (the current version), without inherited values and templates.
    /// </summary>
    /// <returns>The stored content, or null when the configuration doesn't exist.</returns>
    public static async Task<JObject?> GetStoredConfigurationContentAsync(
        this IConfigurationsApiClient @this,
        string organization,
        string project,
        string view,
        string annotationKey)
    {
        // the calculated configuration (GetConfiguration) contains inherited values, the current version contains only the stored content
        var versions = await @this.GetConfigurationVersionsAsync(organization, project, view, annotationKey);
        var current = versions.FirstOrDefault(version => version.IsCurrent());

        if (current is null)
        {
            return null;
        }

        try
        {
            var data = await @this.GetConfigurationVersionAsync(organization, project, view, annotationKey, current.Version);
            return ToJObject(data.Content);
        }
        catch (ApiException e) when (e.StatusCode == (int)HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>
    /// Replaces the stored content of a configuration with the given content.
    /// </summary>
    /// <remarks>
    /// An existing configuration is changed by a JSON Patch computed from the stored content,
    /// so removed properties and array items are removed on the server as well (no merge).
    /// </remarks>
    /// <param name="force">When <c>true</c>, store the document even if it violates the schema. PUT and PATCH both send the query.</param>
    /// <returns>The saved configuration, or null when there was no change.</returns>
    public static async Task<Storyteller.Sdk.Configuration?> ReplaceConfigurationAsync(
        this IConfigurationsApiClient @this,
        string organization,
        string project,
        string view,
        string annotationKey,
        JObject? storedContent,
        JObject content,
        bool force = false)
    {
        if (storedContent is null)
        {
            return await @this.SetConfigurationAsync(organization, project, view, annotationKey, force, content);
        }

        var patch = JsonPatchBuilder.Create(storedContent, content);

        return patch.Count == 0
            ? null
            : await @this.PatchConfigurationAsync(organization, project, view, annotationKey, force, patch);
    }

    /// <summary>
    /// Gets the content of a template.
    /// </summary>
    /// <returns>The content, or null when the template doesn't exist.</returns>
    public static async Task<JObject?> GetTemplateContentAsync(
        this ITemplatesApiClient @this,
        string organization,
        string project,
        string view,
        string annotationType)
    {
        try
        {
            var data = await @this.GetTemplateAsync(organization, project, view, annotationType);
            return ToJObject(data.Content);
        }
        catch (ApiException e) when (e.StatusCode == (int)HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <summary>
    /// Replaces the content of a template with the given content (by a JSON Patch when the template exists, no merge).
    /// </summary>
    /// <returns>The saved template, or null when there was no change.</returns>
    public static async Task<ConfigurationTemplate?> ReplaceTemplateAsync(
        this ITemplatesApiClient @this,
        string organization,
        string project,
        string view,
        string annotationType,
        JObject? currentContent,
        JObject content)
    {
        if (currentContent is null)
        {
            return await @this.SetTemplateAsync(organization, project, view, annotationType, content);
        }

        var patch = JsonPatchBuilder.Create(currentContent, content);

        return patch.Count == 0
            ? null
            : await @this.PatchTemplateAsync(organization, project, view, annotationType, patch);
    }

    private static JObject ToJObject(object? content)
    {
        return content switch
        {
            null => new JObject(),
            JObject jObject => jObject,
            _ => JObject.FromObject(content),
        };
    }
}

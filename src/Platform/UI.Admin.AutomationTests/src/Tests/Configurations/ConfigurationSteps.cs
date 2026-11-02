using _42.Platform.UI.Admin.AutomationTests.Data;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.Configurations;

// The setup steps the Configurations tests share: the annotations they act on and the versions of a configuration.
internal static class ConfigurationSteps
{
    // Bulk creates a new responsibility (no ancestors, usually no type schema or template) and returns its key.
    public static async Task<string> CreateResponsibilityAsync(App app, AppRoutes routes, PlatformData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var key = data.AnnotationKey(PlatformData.Responsibility);
        await BulkCreateAsync(app, routes, key);
        return key;
    }

    // Bulk creates new annotations (with their missing ancestors) on the annotations page of the routes.
    public static async Task BulkCreateAsync(App app, AppRoutes routes, params string[] keys)
    {
        var annotations = new AnnotationsPage(app, routes);
        await annotations.OpenAsync();
        await annotations.BulkCreateAsync(keys);
    }

    // On the open Edit tab of an annotation without an own configuration: Create empty (v1, the toast "Configuration
    // created"), then each document saved in the JSON editor as the next version (v2, v3, …).
    public static async Task StoreVersionsAsync(App app, ConfigurationDetail configuration, params string[] documents)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(documents);

        await configuration.CreateEmptyAsync();
        await Expect(app.Toast("Configuration created")).ToBeVisibleAsync();
        for (var index = 0; index < documents.Length; index++)
        {
            await configuration.SetDocumentAsync(documents[index]);
            await configuration.SaveAsync(index + 2);
        }
    }
}

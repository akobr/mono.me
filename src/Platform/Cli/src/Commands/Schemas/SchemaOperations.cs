using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Configuration;
using _42.Platform.Cli.Output;
using _42.Platform.Cli.Services;
using _42.Platform.Storyteller.Sdk;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Cli.Commands.Schemas;

internal static class SchemaOperations
{
    public static async Task<int> GetAsync(
        IExtendedConsole console,
        ISchemasApiClient api,
        ICommandContext context,
        SchemaLayer layer,
        string? annotationType,
        string? annotationKey,
        int? version)
    {
        var subject = Describe(layer, annotationType, annotationKey);

        try
        {
            var data = version.HasValue
                ? await GetVersionAsync(api, context, layer, annotationType, annotationKey, version.Value)
                : await GetCurrentAsync(api, context, layer, annotationType, annotationKey);

            console.WriteJson(data);
        }
        catch (ApiException e) when (e.StatusCode == (int)HttpStatusCode.NotFound)
        {
            console.WriteLine(version.HasValue
                ? $"The version {version} of the schema for {subject} has not been found."
                : $"The schema for {subject} has not been found.");
            return ExitCodes.ERROR_WRONG_INPUT;
        }
        catch (ApiException e)
        {
            return WriteApiError(console, e);
        }

        return ExitCodes.SUCCESS;
    }

    public static async Task<int> SetAsync(
        IExtendedConsole console,
        ISchemasApiClient api,
        ICommandContext context,
        SchemaLayer layer,
        string? annotationType,
        string? annotationKey,
        JObject content,
        bool force)
    {
        try
        {
            var saved = await SetCurrentAsync(api, context, layer, annotationType, annotationKey, force, content);
            console.WriteJson(saved);
            WriteForced(console, saved.Version, force, includeVersionLine: true);
        }
        catch (ApiException e)
        {
            return WriteApiError(console, e);
        }

        return ExitCodes.SUCCESS;
    }

    public static async Task<int> EditAsync(
        IExtendedConsole console,
        ISchemasApiClient api,
        ICommandContext context,
        IEditorService editorService,
        EditorOptions editorOptions,
        SchemaLayer layer,
        string? annotationType,
        string? annotationKey,
        bool force)
    {
        var subject = Describe(layer, annotationType, annotationKey);
        ConfigurationSchema current;

        try
        {
            current = await GetCurrentAsync(api, context, layer, annotationType, annotationKey);
        }
        catch (ApiException e) when (e.StatusCode == (int)HttpStatusCode.NotFound)
        {
            console.WriteLine($"The schema for {subject} has not been found.");
            return ExitCodes.ERROR_WRONG_INPUT;
        }
        catch (ApiException e)
        {
            return WriteApiError(console, e);
        }

        var result = await editorService.EditJsonAsync(
            console,
            editorOptions,
            ToJObject(current.Content),
            EditorFilePrefix(layer, context.ViewName, annotationType, annotationKey),
            false);

        if (result.Edited is null)
        {
            return result.ExitCode;
        }

        try
        {
            var saved = await SetCurrentAsync(api, context, layer, annotationType, annotationKey, force, result.Edited);
            console.WriteImportant($"Schema for {subject} has been saved (version {saved.Version}).");
            WriteForced(console, saved.Version, force, includeVersionLine: false);
            return ExitCodes.SUCCESS;
        }
        catch (ApiException e)
        {
            return WriteApiError(console, e);
        }
    }

    public static async Task<int> DeleteAsync(
        IExtendedConsole console,
        ISchemasApiClient api,
        ICommandContext context,
        SchemaLayer layer,
        string? annotationType,
        string? annotationKey)
    {
        var subject = Describe(layer, annotationType, annotationKey);

        try
        {
            await DeleteCurrentAsync(api, context, layer, annotationType, annotationKey);
        }
        catch (ApiException e) when (e.StatusCode == (int)HttpStatusCode.NotFound)
        {
            console.WriteLine($"The schema for {subject} has not been found.");
            return ExitCodes.ERROR_WRONG_INPUT;
        }
        catch (ApiException e)
        {
            return WriteApiError(console, e);
        }

        console.WriteImportant($"The schema for {subject} has been deleted.");
        return ExitCodes.SUCCESS;
    }

    public static async Task<int> VersionsAsync(
        IExtendedConsole console,
        ISchemasApiClient api,
        ICommandContext context,
        SchemaLayer layer,
        string? annotationType,
        string? annotationKey)
    {
        var subject = Describe(layer, annotationType, annotationKey);
        ICollection<ConfigurationVersion> versions;

        try
        {
            versions = await GetVersionsAsync(api, context, layer, annotationType, annotationKey);
        }
        catch (ApiException e) when (e.StatusCode == (int)HttpStatusCode.NotFound)
        {
            console.WriteLine($"The schema for {subject} has not been found.");
            return ExitCodes.ERROR_WRONG_INPUT;
        }
        catch (ApiException e)
        {
            return WriteApiError(console, e);
        }

        console.WriteHeader($"Versions of the schema for {subject}");

        if (versions.Count < 1)
        {
            console.WriteLine("The schema has no versions.".ThemedLowlight(console.Theme));
            return ExitCodes.SUCCESS;
        }

        console.WriteVersions(versions);
        return ExitCodes.SUCCESS;
    }

    public static async Task<int> DiffAsync(
        IExtendedConsole console,
        ISchemasApiClient api,
        ICommandContext context,
        SchemaLayer layer,
        string? annotationType,
        string? annotationKey,
        string? toVersion,
        string? fromVersion,
        string? format)
    {
        var subject = Describe(layer, annotationType, annotationKey);

        if (!string.IsNullOrWhiteSpace(toVersion) && !int.TryParse(toVersion, out _))
        {
            console.WriteLine($"ToVersion '{toVersion}' is not a valid integer.");
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        if (!string.IsNullOrWhiteSpace(fromVersion) && !int.TryParse(fromVersion, out _))
        {
            console.WriteLine($"FromVersion '{fromVersion}' is not a valid integer.");
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        var responseFormat = string.IsNullOrWhiteSpace(format) ? null : format;

        try
        {
            DiffResult diff;

            if (int.TryParse(toVersion, out var to))
            {
                diff = int.TryParse(fromVersion, out var from)
                    ? await DiffCustomAsync(api, context, layer, annotationType, annotationKey, to, from, responseFormat)
                    : await DiffVersionAsync(api, context, layer, annotationType, annotationKey, to, responseFormat);
            }
            else
            {
                var versions = await GetVersionsAsync(api, context, layer, annotationType, annotationKey);

                if (versions.Count == 0)
                {
                    console.WriteLine($"The schema for {subject} has no versions.");
                    return ExitCodes.ERROR_WRONG_INPUT;
                }

                var latestVersion = versions.Max(version => version.Version);
                diff = await DiffVersionAsync(api, context, layer, annotationType, annotationKey, latestVersion, responseFormat);
            }

            console.WriteDiffResult(diff);
        }
        catch (ApiException e) when (e.StatusCode == (int)HttpStatusCode.NotFound)
        {
            console.WriteLine($"The schema for {subject} or the requested version has not been found.");
            return ExitCodes.ERROR_WRONG_INPUT;
        }
        catch (ApiException e)
        {
            return WriteApiError(console, e);
        }

        return ExitCodes.SUCCESS;
    }

    public static async Task<int> DefinitionAsync(
        IExtendedConsole console,
        ISchemasApiClient api,
        ICommandContext context,
        string annotationKey)
    {
        try
        {
            var data = await api.GetCombinedConfigurationSchemaAsync(
                context.OrganizationName,
                context.ProjectName,
                context.ViewName,
                annotationKey);

            console.WriteJson(data);
        }
        catch (ApiException e) when (e.StatusCode == (int)HttpStatusCode.NotFound)
        {
            console.WriteLine($"The combined schema for '{annotationKey}' has not been found.");
            return ExitCodes.ERROR_WRONG_INPUT;
        }
        catch (ApiException e)
        {
            return WriteApiError(console, e);
        }

        return ExitCodes.SUCCESS;
    }

    public static string Describe(SchemaLayer layer, string? annotationType, string? annotationKey)
    {
        return layer switch
        {
            SchemaLayer.Type => $"'{annotationType}'",
            SchemaLayer.Annotation => $"'{annotationKey}'",
            SchemaLayer.Descendant => $"'{annotationType}' under '{annotationKey}'",
            _ => throw new ArgumentOutOfRangeException(nameof(layer)),
        };
    }

    private static void WriteForced(IExtendedConsole console, long version, bool force, bool includeVersionLine)
    {
        if (!force)
        {
            return;
        }

        if (includeVersionLine)
        {
            console.WriteLine($"Schema saved as version {version}.");
        }

        console.WriteLine("Existing configurations were not required to comply.");
    }

    private static int WriteApiError(IExtendedConsole console, ApiException exception)
    {
        if (SchemaValidationConsole.TryWrite(console, exception))
        {
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        console.WriteLine($"Error occurred: {exception.Message}");
        return ExitCodes.ERROR_CRASH;
    }

    private static Task<ConfigurationSchema> GetCurrentAsync(
        ISchemasApiClient api,
        ICommandContext context,
        SchemaLayer layer,
        string? annotationType,
        string? annotationKey)
    {
        return layer switch
        {
            SchemaLayer.Type => api.GetConfigurationSchemaAsync(
                context.OrganizationName,
                context.ProjectName,
                context.ViewName,
                annotationType!),
            SchemaLayer.Annotation => api.GetAnnotationSchemaAsync(
                context.OrganizationName,
                context.ProjectName,
                context.ViewName,
                annotationKey!),
            SchemaLayer.Descendant => api.GetDescendantTypeSchemaAsync(
                context.OrganizationName,
                context.ProjectName,
                context.ViewName,
                annotationKey!,
                annotationType!),
            _ => throw new ArgumentOutOfRangeException(nameof(layer)),
        };
    }

    private static Task<ConfigurationSchema> GetVersionAsync(
        ISchemasApiClient api,
        ICommandContext context,
        SchemaLayer layer,
        string? annotationType,
        string? annotationKey,
        int version)
    {
        return layer switch
        {
            SchemaLayer.Type => api.GetSchemaVersionAsync(
                context.OrganizationName,
                context.ProjectName,
                context.ViewName,
                annotationType!,
                version),
            SchemaLayer.Annotation => api.GetAnnotationSchemaVersionAsync(
                context.OrganizationName,
                context.ProjectName,
                context.ViewName,
                annotationKey!,
                version),
            SchemaLayer.Descendant => api.GetDescendantTypeSchemaVersionAsync(
                context.OrganizationName,
                context.ProjectName,
                context.ViewName,
                annotationKey!,
                annotationType!,
                version),
            _ => throw new ArgumentOutOfRangeException(nameof(layer)),
        };
    }

    private static Task<ConfigurationSchema> SetCurrentAsync(
        ISchemasApiClient api,
        ICommandContext context,
        SchemaLayer layer,
        string? annotationType,
        string? annotationKey,
        bool force,
        object body)
    {
        return layer switch
        {
            SchemaLayer.Type => api.SetConfigurationSchemaAsync(
                context.OrganizationName,
                context.ProjectName,
                context.ViewName,
                annotationType!,
                force,
                body),
            SchemaLayer.Annotation => api.SetAnnotationSchemaAsync(
                context.OrganizationName,
                context.ProjectName,
                context.ViewName,
                annotationKey!,
                force,
                body),
            SchemaLayer.Descendant => api.SetDescendantTypeSchemaAsync(
                context.OrganizationName,
                context.ProjectName,
                context.ViewName,
                annotationKey!,
                annotationType!,
                force,
                body),
            _ => throw new ArgumentOutOfRangeException(nameof(layer)),
        };
    }

    private static Task DeleteCurrentAsync(
        ISchemasApiClient api,
        ICommandContext context,
        SchemaLayer layer,
        string? annotationType,
        string? annotationKey)
    {
        return layer switch
        {
            SchemaLayer.Type => api.DeleteConfigurationSchemaAsync(
                context.OrganizationName,
                context.ProjectName,
                context.ViewName,
                annotationType!),
            SchemaLayer.Annotation => api.DeleteAnnotationSchemaAsync(
                context.OrganizationName,
                context.ProjectName,
                context.ViewName,
                annotationKey!),
            SchemaLayer.Descendant => api.DeleteDescendantTypeSchemaAsync(
                context.OrganizationName,
                context.ProjectName,
                context.ViewName,
                annotationKey!,
                annotationType!),
            _ => throw new ArgumentOutOfRangeException(nameof(layer)),
        };
    }

    private static Task<ICollection<ConfigurationVersion>> GetVersionsAsync(
        ISchemasApiClient api,
        ICommandContext context,
        SchemaLayer layer,
        string? annotationType,
        string? annotationKey)
    {
        return layer switch
        {
            SchemaLayer.Type => api.GetSchemaVersionsAsync(
                context.OrganizationName,
                context.ProjectName,
                context.ViewName,
                annotationType!),
            SchemaLayer.Annotation => api.GetAnnotationSchemaVersionsAsync(
                context.OrganizationName,
                context.ProjectName,
                context.ViewName,
                annotationKey!),
            SchemaLayer.Descendant => api.GetDescendantTypeSchemaVersionsAsync(
                context.OrganizationName,
                context.ProjectName,
                context.ViewName,
                annotationKey!,
                annotationType!),
            _ => throw new ArgumentOutOfRangeException(nameof(layer)),
        };
    }

    private static Task<DiffResult> DiffVersionAsync(
        ISchemasApiClient api,
        ICommandContext context,
        SchemaLayer layer,
        string? annotationType,
        string? annotationKey,
        int version,
        string? format)
    {
        return layer switch
        {
            SchemaLayer.Type => api.GetSchemaVersionDiffAsync(
                context.OrganizationName,
                context.ProjectName,
                context.ViewName,
                annotationType!,
                version,
                format),
            SchemaLayer.Annotation => api.GetAnnotationSchemaVersionDiffAsync(
                context.OrganizationName,
                context.ProjectName,
                context.ViewName,
                annotationKey!,
                version,
                format),
            SchemaLayer.Descendant => api.GetDescendantTypeSchemaVersionDiffAsync(
                context.OrganizationName,
                context.ProjectName,
                context.ViewName,
                annotationKey!,
                annotationType!,
                version,
                format),
            _ => throw new ArgumentOutOfRangeException(nameof(layer)),
        };
    }

    private static Task<DiffResult> DiffCustomAsync(
        ISchemasApiClient api,
        ICommandContext context,
        SchemaLayer layer,
        string? annotationType,
        string? annotationKey,
        int version,
        int versionFrom,
        string? format)
    {
        return layer switch
        {
            SchemaLayer.Type => api.GetSchemaVersionDiffCustomAsync(
                context.OrganizationName,
                context.ProjectName,
                context.ViewName,
                annotationType!,
                version,
                versionFrom,
                format),
            SchemaLayer.Annotation => api.GetAnnotationSchemaVersionDiffCustomAsync(
                context.OrganizationName,
                context.ProjectName,
                context.ViewName,
                annotationKey!,
                version,
                versionFrom,
                format),
            SchemaLayer.Descendant => api.GetDescendantTypeSchemaVersionDiffCustomAsync(
                context.OrganizationName,
                context.ProjectName,
                context.ViewName,
                annotationKey!,
                annotationType!,
                version,
                versionFrom,
                format),
            _ => throw new ArgumentOutOfRangeException(nameof(layer)),
        };
    }

    private static string EditorFilePrefix(SchemaLayer layer, string view, string? annotationType, string? annotationKey)
    {
        return layer switch
        {
            SchemaLayer.Type => $"schema-type-{view}-{annotationType}",
            SchemaLayer.Annotation => $"schema-annotation-{view}-{annotationKey}",
            SchemaLayer.Descendant => $"schema-descendant-{view}-{annotationType}-{annotationKey}",
            _ => throw new ArgumentOutOfRangeException(nameof(layer)),
        };
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

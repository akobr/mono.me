using System;
using System.IO.Abstractions;
using System.Net;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Configuration;
using _42.Platform.Cli.Json;
using _42.Platform.Cli.Output;
using _42.Platform.Cli.Services;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using Sharprompt;

namespace _42.Platform.Cli.Commands.Configuration;

[Command(CommandNames.PATCH, Description = "Apply a JSON Patch (RFC 6902) to the stored content of a configuration.")]
public class ConfigPatchCommand : BaseContextCommand
{
    private readonly IConfigurationsApiClient _configurationApi;
    private readonly IEditorService _editorService;
    private readonly EditorOptions _editorOptions;
    private readonly IFileSystem _fileSystem;

    public ConfigPatchCommand(
        IExtendedConsole console,
        ICommandContext context,
        IConfigurationsApiClient configurationApi,
        IEditorService editorService,
        IOptions<EditorOptions> editorOptions,
        IFileSystem fileSystem)
        : base(console, context)
    {
        _configurationApi = configurationApi;
        _editorService = editorService;
        _editorOptions = editorOptions.Value;
        _fileSystem = fileSystem;
    }

    [Argument(0, Description = "An annotation key to patch the configuration for.")]
    public string AnnotationKey { get; set; } = string.Empty;

    [Option("-i|--import", CommandOptionType.SingleValue, Description = "A file whose top-level value is a JSON Patch array (RFC 6902).")]
    public string? ImportFilePath { get; set; }

    [Option("-f|--force", CommandOptionType.NoValue, Description = "Store the configuration even when it violates the schema.")]
    public bool Force { get; set; }

    [Option("-y|--yes", CommandOptionType.NoValue, Description = "Apply the patch without asking for confirmation.")]
    public bool Yes { get; set; }

    /// <summary>
    /// Applies a JSON Patch to the stored content of an existing configuration.
    /// Without <c>--import</c>, the stored content is opened in the configured editor and the patch is
    /// the difference between that input and the edited document. With <c>--import</c>, the file is the patch.
    /// </summary>
    protected override async Task<int> ExecuteAsync()
    {
        JArray? patchFromFile = null;

        if (ImportFilePath is not null)
        {
            if (string.IsNullOrWhiteSpace(ImportFilePath))
            {
                Console.WriteLine("The --import option requires a path to a JSON Patch file.");
                return ExitCodes.ERROR_WRONG_INPUT;
            }

            patchFromFile = await JsonPatchDocumentReader.ReadAsync(Console, _fileSystem, ImportFilePath);
        }

        StoredConfiguration? loaded;

        try
        {
            loaded = await _configurationApi.GetStoredConfigurationAsync(
                Context.OrganizationName,
                Context.ProjectName,
                Context.ViewName,
                AnnotationKey);
        }
        catch (ApiException exception)
        {
            return WritePatchError(exception);
        }

        if (loaded is not StoredConfiguration stored)
        {
            Console.WriteLine($"Configuration for '{AnnotationKey}' does not exist. 'config patch' applies a patch to stored content and does not create a configuration.");
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        JArray patch;

        if (patchFromFile is not null)
        {
            if (patchFromFile.Count == 0)
            {
                Console.WriteLine("No changes detected.");
                return ExitCodes.WARNING_NO_WORK_NEEDED;
            }

            if (ContainsPointerEscape(patchFromFile))
            {
                Console.WriteImportant("A path in this patch contains '~'. The server applies JSON Pointer escapes as literal characters, so '~0' and '~1' are not decoded.");
            }

            patch = patchFromFile;
        }
        else
        {
            var editResult = await _editorService.EditJsonAsync(
                Console,
                _editorOptions,
                stored.Content,
                $"config-{AnnotationKey}",
                false,
                confirm: false);

            if (editResult.Edited is null)
            {
                return editResult.ExitCode;
            }

            patch = JsonPatchBuilder.Create(stored.Content, editResult.Edited);

            if (patch.Count == 0)
            {
                Console.WriteLine("No changes detected.");
                return ExitCodes.WARNING_NO_WORK_NEEDED;
            }
        }

        Console.WriteHeader("JSON Patch");
        Console.WriteJson(patch);

        if (!Yes)
        {
            var shouldApply = Console.Confirm(new ConfirmOptions
            {
                Message = $"Apply this patch to '{AnnotationKey}'",
                DefaultValue = true,
            });

            if (!shouldApply)
            {
                Console.WriteLine("Patch aborted.");
                return ExitCodes.WARNING_ABORTED;
            }
        }

        try
        {
            var saved = await _configurationApi.PatchConfigurationAsync(
                Context.OrganizationName,
                Context.ProjectName,
                Context.ViewName,
                AnnotationKey,
                Force,
                patch);

            var report = PatchVersionReport.Create(AnnotationKey, stored.Version, saved.Version);
            Console.WriteImportant(report.Message);

            if (!report.Changed)
            {
                return ExitCodes.WARNING_NO_WORK_NEEDED;
            }

            Console.WriteJson(saved);
            return ExitCodes.SUCCESS;
        }
        catch (ApiException exception)
        {
            return WritePatchError(exception);
        }
    }

    private int WritePatchError(ApiException exception)
    {
        if (SchemaValidationConsole.TryWrite(Console, exception))
        {
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        if (exception is ApiException<ErrorResponse> error
            && !string.IsNullOrWhiteSpace(error.Result?.Message))
        {
            Console.WriteLine(error.Result.Message);
            return exception.StatusCode is (int)HttpStatusCode.BadRequest or (int)HttpStatusCode.NotFound
                ? ExitCodes.ERROR_WRONG_INPUT
                : ExitCodes.ERROR_CRASH;
        }

        Console.WriteLine($"Error occurred: {exception.Message}");
        return ExitCodes.ERROR_CRASH;
    }

    private static bool ContainsPointerEscape(JArray patch)
    {
        foreach (var token in patch)
        {
            if (token is not JObject operation)
            {
                continue;
            }

            if (HasTilde(operation, "path") || HasTilde(operation, "from"))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasTilde(JObject operation, string propertyName)
    {
        return operation.Property(propertyName, StringComparison.Ordinal)?.Value is JValue { Type: JTokenType.String } value
            && value.Value is string text
            && text.Contains('~');
    }
}

using System;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Configuration;
using _42.Platform.Cli.Output;
using _42.Platform.Cli.Services;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Sharprompt;

namespace _42.Platform.Cli.Commands.Configuration;

[Command(CommandNames.EDIT, Description = "Edit a configuration in your preferred editor.")]
public class ConfigEditCommand : BaseContextCommand
{
    private readonly IConfigurationApiClient _configurationApi;
    private readonly IFileSystem _fileSystem;
    private readonly IEditorService _editorService;
    private readonly EditorOptions _editorOptions;

    /// <summary>
    /// Initializes a new instance of <see cref="ConfigEditCommand"/> with the specified services and options.
    /// </summary>
    /// <param name="editorOptions">Provides editor-related configuration used by the command.</param>
    public ConfigEditCommand(
        IExtendedConsole console,
        ICommandContext context,
        IConfigurationApiClient configurationApi,
        IFileSystem fileSystem,
        IEditorService editorService,
        IOptions<EditorOptions> editorOptions)
        : base(console, context)
    {
        _configurationApi = configurationApi;
        _fileSystem = fileSystem;
        _editorService = editorService;
        _editorOptions = editorOptions.Value;
    }

    [Argument(0, Description = "An annotation key to edit the configuration for.")]
    public string AnnotationKey { get; set; } = string.Empty;

    /// <summary>
    /// Opens the configured editor to edit the configuration for the current context and specified annotation key, and uploads the updated configuration when the user confirms.
    /// </summary>
    /// <returns>
    /// An exit code indicating the outcome:
    /// - <c>ExitCodes.SUCCESS</c> when the edited configuration was saved;
    /// - <c>ExitCodes.WARNING_NO_WORK_NEEDED</c> when no changes were made;
    /// - <c>ExitCodes.WARNING_ABORTED</c> when the user aborted the operation;
    /// - <c>ExitCodes.ERROR_CRASH</c> when the editor exited with a non-zero code.
    /// </returns>
    protected override async Task<int> ExecuteAsync()
    {
        // 1. Ensure editor is configured
        var editorOptions = _editorOptions;

        if (!editorOptions.IsConfigured)
        {
            Console.WriteImportant("No editor is configured yet. Let's set one up.");
            editorOptions = _editorService.SetupEditorPreference(Console);
        }

        // 2. Fetch existing configuration or start with empty JSON
        string originalJson;
        bool isNewConfiguration;

        try
        {
            var data = await _configurationApi.GetConfigurationAsync(
                Context.OrganizationName,
                Context.ProjectName,
                Context.ViewName,
                AnnotationKey);

            originalJson = SerializeContent(data.Content);
            isNewConfiguration = false;
        }
        catch (ApiException e) when (e.StatusCode == (int)HttpStatusCode.NotFound)
        {
            originalJson = "{\n}";
            isNewConfiguration = true;
            Console.WriteLine($"Configuration for '{AnnotationKey}' does not exist yet, creating new.");
        }

        // 3. Write to temp file
        var invalidChars = _fileSystem.Path.GetInvalidFileNameChars();
        var safeKey = string.Concat(AnnotationKey.Select(c => invalidChars.Contains(c) ? '_' : c));
        var tempFilePath = _fileSystem.Path.Combine(
            _fileSystem.Path.GetTempPath(),
            $"config-{safeKey}.json");

        _fileSystem.File.WriteAllText(tempFilePath, originalJson, Encoding.UTF8);

        try
        {
            // 4. Open editor loop (allows re-editing on invalid JSON)
            string editedJson;

            while (true)
            {
                var exitCode = await _editorService.OpenFileInEditorAsync(tempFilePath, editorOptions);

                if (exitCode != 0)
                {
                    Console.WriteImportant($"Editor exited with code {exitCode}.");
                    return ExitCodes.ERROR_CRASH;
                }

                editedJson = _fileSystem.File.ReadAllText(tempFilePath, Encoding.UTF8);

                // Validate JSON
                try
                {
                    JObject.Parse(editedJson);
                    break;
                }
                catch (JsonReaderException jsonEx)
                {
                    Console.WriteImportant($"Invalid JSON: {jsonEx.Message}");

                    var shouldRetry = Console.Confirm(new ConfirmOptions
                    {
                        Message = "Would you like to re-open the editor to fix the issue",
                        DefaultValue = true,
                    });

                    if (!shouldRetry)
                    {
                        Console.WriteLine("Edit aborted.");
                        return ExitCodes.WARNING_ABORTED;
                    }
                }
            }

            // 5. Normalize both for comparison (re-serialize to consistent formatting)
            var normalizedOriginal = NormalizeJson(originalJson);
            var normalizedEdited = NormalizeJson(editedJson);

            if (string.Equals(normalizedOriginal, normalizedEdited, StringComparison.Ordinal))
            {
                Console.WriteLine("No changes detected.");
                return ExitCodes.WARNING_NO_WORK_NEEDED;
            }

            // 6. Show diff
            Console.WriteHeader(isNewConfiguration ? "New configuration" : "Changes");
            Console.WriteDiff(normalizedOriginal, normalizedEdited);

            // 7. Confirm upload
            var shouldSave = Console.Confirm(new ConfirmOptions
            {
                Message = "Do you want to save these changes",
                DefaultValue = true,
            });

            if (!shouldSave)
            {
                Console.WriteLine("Edit aborted.");
                return ExitCodes.WARNING_ABORTED;
            }

            // 8. Upload
            var parsedContent = JObject.Parse(editedJson);
            var result = await _configurationApi.SetConfigurationAsync(
                Context.OrganizationName,
                Context.ProjectName,
                Context.ViewName,
                AnnotationKey,
                parsedContent);

            Console.WriteImportant($"Configuration for '{AnnotationKey}' has been saved (version {result.Version}).");
            return ExitCodes.SUCCESS;
        }
        finally
        {
            // 9. Clean up temp file
            if (_fileSystem.File.Exists(tempFilePath))
            {
                _fileSystem.File.Delete(tempFilePath);
            }
        }
    }

    /// <summary>
    /// Serializes an object to indented JSON text.
    /// </summary>
    /// <param name="content">The object to serialize using the default JSON serializer settings.</param>
    /// <returns>The JSON representation of <paramref name="content"/> formatted with indentation.</returns>
    private static string SerializeContent(object content)
    {
        var sb = new StringBuilder();
        using var sw = new StringWriter(sb);
        using var jw = new JsonTextWriter(sw) { Formatting = Formatting.Indented, CloseOutput = false };
        JsonSerializer.CreateDefault().Serialize(jw, content);
        return sb.ToString();
    }

    /// <summary>
    /// Normalize a JSON string to a consistently indented representation.
    /// </summary>
    /// <returns>The input JSON re-serialized with consistent indentation.</returns>
    /// <exception cref="Newtonsoft.Json.JsonReaderException">Thrown when the input string is not valid JSON.</exception>
    private static string NormalizeJson(string json)
    {
        var obj = JObject.Parse(json);
        return obj.ToString(Formatting.Indented);
    }
}
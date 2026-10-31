using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.GitHubActions;
using _42.Platform.Cli.Output;
using _42.Platform.Storyteller.ConfigurationFormats;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ConfigurationModel = _42.Platform.Storyteller.Sdk.Configuration;

namespace _42.Platform.Cli.Commands.Configuration;

[Subcommand(
    typeof(ConfigSetCommand),
    typeof(ConfigEditCommand),
    typeof(ConfigPatchCommand),
    typeof(ConfigDeleteCommand),
    typeof(ConfigDiffCommand),
    typeof(ConfigVersionsCommand),
    typeof(ConfigCompositionCommand))]

[Command(CommandNames.CONFIG, CommandNames.CONFIGURATION, Description = "Get and manage configuration in context of an annotation.")]
public class ConfigGetCommand : BaseContextCommand
{
    private const string JsonFormat = "json";

    private readonly IConfigurationsApiClient _configurationApi;
    private readonly IFileSystem _fileSystem;
    private readonly GitHubActionsSettings _gitHubActions;

    public ConfigGetCommand(
        IExtendedConsole console,
        ICommandContext context,
        IConfigurationsApiClient configurationApi,
        IFileSystem fileSystem,
        GitHubActionsSettings gitHubActions)
        : base(console, context)
    {
        _configurationApi = configurationApi;
        _fileSystem = fileSystem;
        _gitHubActions = gitHubActions;
    }

    [Argument(0, Description = "An annotation key to get the configuration for.")]
    public string AnnotationKey { get; set; } = string.Empty;

    [Option("-e|--export", CommandOptionType.SingleValue, Description = "Specify a file where the retrieved configuration will be saved: the whole model, or the content alone with --format flat, properties, env or yaml, --content-only, --github-env or --mask, which then print no content.")]
    public string? ExportFilePath { get; set; }

    public bool IsExportRequested => !string.IsNullOrWhiteSpace(ExportFilePath);

    [Option("-r|--resolved", CommandOptionType.NoValue, Description = "Retrieve resolved configuration, corresponding permission is needed.")]
    public bool IsResolvedRetrievalRequested { get; set; }

    [Option("--version", CommandOptionType.SingleValue, Description = "Retrieve the stored content of a specific version (see 'versions').")]
    public int? Version { get; set; }

    [Option("-f|--format", CommandOptionType.SingleValue, Description = "json (the default), or flat, properties, env or yaml: the content alone in that format.")]
    public string? Format { get; set; }

    [Option("--content-only", CommandOptionType.NoValue, Description = "Print the content alone, as it is, to be piped or redirected: the formatted text, or the JSON content without the model. Messages go to the error output.")]
    public bool IsContentOnly { get; set; }

    [Option("--env-prefix", CommandOptionType.SingleValue, Description = "Prepend this prefix to every env name, for example APP_ (an ASCII letter, then ASCII letters, digits and '_'); with --format env or --github-env.")]
    public string? EnvironmentPrefix { get; set; }

    [Option("--github-env", CommandOptionType.NoValue, Description = "Export the resolved configuration into GITHUB_ENV of a GitHub Actions job, after masking the values bindings produced; needs --resolved, and --env-prefix or --allow-unprefixed.")]
    public bool IsGitHubEnvRequested { get; set; }

    [Option("--mask", CommandOptionType.SingleValue, Description = "bound (the default of --github-env) or all: print ::add-mask:: for the values bindings produced, or for every string and number; needs --resolved.")]
    public string? Mask { get; set; }

    [Option("--allow-unprefixed", CommandOptionType.NoValue, Description = "Let --github-env export without --env-prefix: the names of the configuration then reach every later step of the job.")]
    public bool IsUnprefixedAllowed { get; set; }

    [Option("--allow-unresolved", CommandOptionType.NoValue, Description = "Let --github-env and --mask export binding expressions that were not resolved, as they are.")]
    public bool IsUnresolvedAllowed { get; set; }

    private bool IsMasking => IsGitHubEnvRequested || Mask is not null;

    // The payload goes to the standard output or a file as it is, so every message goes to the error output.
    private bool IsPlainOutput => IsContentOnly || IsMasking;

    // The content with two-space indentation and LF line ends; {} for a configuration without content.
    internal static string ToContentJson(JToken? content)
    {
        if (content is null || content.Type == JTokenType.Null)
        {
            return "{}\n";
        }

        using var writer = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };

        // The indentation of JsonTextWriter writes the NewLine of its writer.
        using (var json = new JsonTextWriter(writer) { Formatting = Formatting.Indented, Indentation = 2, CloseOutput = false })
        {
            content.WriteTo(json);
        }

        writer.Write('\n');
        return writer.ToString();
    }

    protected override async Task<int> ExecuteAsync()
    {
        if (Validate() is not { } options)
        {
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        // The model is displayed as before; anything else is text that sform writes itself.
        using var encoding = options.Format is null && !IsPlainOutput ? null : Console.UseUtf8Output();
        ConfigurationModel configuration;
        string? secretsWithheld;

        try
        {
            (configuration, secretsWithheld) = await ReadAsync(options.Key);
        }
        catch (ApiException exception) when (exception.StatusCode is not ((int)HttpStatusCode.Unauthorized or (int)HttpStatusCode.Forbidden))
        {
            return WriteReadFailure(options.Key, exception);
        }

        if (secretsWithheld is not null)
        {
            // study 3.7: a consumer that cannot get the secrets fails loudly
            if (IsMasking)
            {
                Console.WriteErrorLine($"The API withheld the secrets of the configuration ({secretsWithheld}): the values of secret bindings are their expressions. Nothing was exported. A machine receives secrets over mTLS with its certificate, or from a project that does not require mTLS for secret delivery.");
                return ExitCodes.ERROR_WRONG_INPUT;
            }

            Console.WriteErrorLine($"Warning: the API withheld the secrets of the configuration ({secretsWithheld}): the values of secret bindings are their expressions.");
        }

        return IsMasking
            ? await ExportAsync(options, configuration)
            : await WriteAsync(options, configuration);
    }

    private static JToken? ToToken(object? content)
    {
        return content as JToken ?? (content is null ? null : JToken.FromObject(content));
    }

    private static bool IsValidPrefix(string prefix)
    {
        return prefix.Length > 0
            && char.IsAsciiLetter(prefix[0])
            && prefix.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');
    }

    private static string Paths(IEnumerable<DocumentLeaf> values)
    {
        return string.Join(", ", values.Select(value => value.Path));
    }

    // Without a tag the API has nothing to answer 304 to; an answer without a body is a failure of the read.
    private static ConfigurationModel ResultOf(ConditionalResponse<ConfigurationModel> response)
    {
        return response.Result
            ?? throw new ApiException($"The API answered {response.StatusCode} without a configuration.", response.StatusCode, string.Empty, response.Headers, null);
    }

    // Every check before the first call, so a wrong command line writes its message and nothing else.
    private ReadOptions? Validate()
    {
        string key;

        if (IsPlainOutput)
        {
            if (!Platform.Storyteller.AnnotationKey.TryParse(AnnotationKey, out var parsedKey))
            {
                return Invalid("Please specify a valid annotation key; 'sform assist key' explains them.");
            }

            key = parsedKey;
        }
        else
        {
            // writes its message and throws a WrongInputException
            key = Console.ValidateAnnotationKey(AnnotationKey);
        }

        ConfigurationFormat? format = null;

        if (Format is not null && !Ascii.EqualsIgnoreCase(Format, JsonFormat))
        {
            if (!ConfigurationFormatNames.TryParse(Format, out var parsedFormat))
            {
                return Invalid($"Unknown format '{Format}'; use json, flat, properties, env or yaml.");
            }

            format = parsedFormat;
        }

        MaskMode? mask = IsGitHubEnvRequested ? MaskMode.Bound : null;

        if (Mask is not null)
        {
            mask = Ascii.EqualsIgnoreCase(Mask, "bound") ? MaskMode.Bound
                : Ascii.EqualsIgnoreCase(Mask, "all") ? MaskMode.All
                : null;

            if (mask is null)
            {
                return Invalid($"Unknown mask '{Mask}'; use bound or all.");
            }
        }

        if (Version.HasValue && IsResolvedRetrievalRequested)
        {
            return Invalid("Cannot specify both --version and --resolved.");
        }

        if (IsMasking && (!IsResolvedRetrievalRequested || Version.HasValue))
        {
            return Invalid("--github-env and --mask export the resolved configuration: add --resolved, without --version.");
        }

        if (IsContentOnly && IsMasking && !IsExportRequested)
        {
            return Invalid("--content-only with --github-env or --mask needs --export: their standard output is the ::add-mask:: commands.");
        }

        if (EnvironmentPrefix is not null)
        {
            if (format != ConfigurationFormat.Env && !IsGitHubEnvRequested)
            {
                return Invalid("--env-prefix applies to --format env and --github-env only.");
            }

            if (!IsValidPrefix(EnvironmentPrefix))
            {
                return Invalid("--env-prefix must start with an ASCII letter and hold only ASCII letters, digits and '_', for example APP_.");
            }
        }

        if (IsGitHubEnvRequested && EnvironmentPrefix is null && !IsUnprefixedAllowed)
        {
            return Invalid("--github-env needs --env-prefix, for example APP_, or --allow-unprefixed: the names come from the configuration and reach every later step of the job.");
        }

        if ((IsUnprefixedAllowed || IsUnresolvedAllowed) && !IsMasking)
        {
            return Invalid("--allow-unprefixed and --allow-unresolved apply to --github-env and --mask only.");
        }

        if (IsGitHubEnvRequested && _gitHubActions.EnvironmentFile is null)
        {
            return Invalid($"{GitHubActionsSettings.ENV} is not set: --github-env runs in a step of a GitHub Actions job.");
        }

        return new ReadOptions(key, format, mask);
    }

    private ReadOptions? Invalid(string message)
    {
        WriteMessage(message);
        return null;
    }

    // One message: a plain line on the error output next to a payload, a line of the console otherwise.
    private void WriteMessage(string message)
    {
        if (IsPlainOutput)
        {
            Console.WriteErrorLine(message);
            return;
        }

        Console.WriteLine(message);
    }

    // The effective or the resolved read through the conditional twins, which keep the headers, or the stored content
    // of a version.
    private async Task<(ConfigurationModel Configuration, string? SecretsWithheld)> ReadAsync(string key)
    {
        if (Version is { } version)
        {
            return (await _configurationApi.GetConfigurationVersionAsync(Context.OrganizationName, Context.ProjectName, Context.ViewName, key, version), null);
        }

        var response = IsResolvedRetrievalRequested
            ? await _configurationApi.GetResolvedConfigurationConditionalAsync(Context.OrganizationName, Context.ProjectName, Context.ViewName, key)
            : await _configurationApi.GetConfigurationConditionalAsync(Context.OrganizationName, Context.ProjectName, Context.ViewName, key);

        return (ResultOf(response), response.SecretsWithheld);
    }

    private async Task<ConfigurationModel> ReadEffectiveAsync(string key)
    {
        return ResultOf(await _configurationApi.GetConfigurationConditionalAsync(Context.OrganizationName, Context.ProjectName, Context.ViewName, key));
    }

    private int WriteReadFailure(string key, ApiException exception)
    {
        if (exception.StatusCode == (int)HttpStatusCode.NotFound)
        {
            WriteMessage(Version.HasValue
                ? $"The version {Version} of the configuration for '{key}' has not been found."
                : $"The configuration for '{key}' has not been found.");
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        // A binding that cannot be evaluated: the configuration is wrong, not the tool.
        if (exception.StatusCode == (int)HttpStatusCode.UnprocessableEntity)
        {
            var (message, errorCode) = KeyValueConsoleExtensions.ReadError(exception);
            WriteMessage(string.IsNullOrEmpty(message) ? "The configuration cannot be resolved." : message);

            if (!string.IsNullOrEmpty(errorCode))
            {
                WriteMessage($"Error code: {errorCode}");
            }

            return ExitCodes.ERROR_WRONG_INPUT;
        }

        return IsPlainOutput ? Console.WriteApiErrorLine(exception) : Console.WriteApiError(exception);
    }

    // Without --github-env and --mask: the model as before, or the content alone as text.
    private async Task<int> WriteAsync(ReadOptions options, ConfigurationModel configuration)
    {
        if (IsResolvedRetrievalRequested)
        {
            await WarnOfUnresolvedAsync(options.Key, configuration);
        }

        if (options.Format is null && !IsContentOnly)
        {
            Console.WriteJson(configuration);

            if (IsExportRequested)
            {
                Console.WriteJsonToFile(configuration, ExportFilePath!, _fileSystem);
            }

            return ExitCodes.SUCCESS;
        }

        if (!TryFormat(configuration, options.Format, out var payload))
        {
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        if (IsExportRequested)
        {
            return WriteExportFile(payload);
        }

        if (IsContentOnly)
        {
            Console.WriteRaw(payload);
        }
        else if (payload.Length == 0)
        {
            // As raw as the text: Alba needs a console, which a redirected run in CI does not have.
            Console.WriteRaw("\nThe configuration has no values.\n");
        }
        else
        {
            Console.WriteNumberedText(payload);
        }

        return ExitCodes.SUCCESS;
    }

    // A machine without Secrets.Read gets the expressions of secret bindings as values, and no header: a string that
    // starts with @ and is the same in the effective document says so. Only then is the effective document read; the
    // warning is the only thing that depends on it.
    private async Task WarnOfUnresolvedAsync(string key, ConfigurationModel resolved)
    {
        var values = ConfigurationMasks.ReadValues(ToToken(resolved.Content));

        if (!values.Any(value => value.Kind == DocumentValueKind.String && value.Value.StartsWith('@')))
        {
            return;
        }

        IReadOnlyList<DocumentLeaf> unresolved;

        try
        {
            var effective = await ReadEffectiveAsync(key);
            unresolved = ConfigurationMasks.FindUnresolved(values, ConfigurationMasks.ByPath(ConfigurationMasks.ReadValues(ToToken(effective.Content))));
        }
        catch (Exception exception) when (exception is ApiException or HttpRequestException or JsonException or TaskCanceledException)
        {
            return;
        }

        if (unresolved.Count > 0)
        {
            Console.WriteErrorLine($"Warning: these values are binding expressions that were not resolved: {Paths(unresolved)}. The caller may lack Secrets.Read.");
        }
    }

    // --github-env and --mask, with one resolved and one effective read: everything is computed before anything is
    // written, then the masks are written before any value reaches GITHUB_ENV or a file.
    private async Task<int> ExportAsync(ReadOptions options, ConfigurationModel resolved)
    {
        var content = ToToken(resolved.Content);
        IReadOnlyList<EnvironmentVariable> variables = [];

        if (IsGitHubEnvRequested)
        {
            try
            {
                variables = ConfigurationFormatter.ToEnvironmentVariables(content, EnvironmentPrefix);
            }
            catch (InvalidDocumentException exception)
            {
                WriteInvalidDocument("as variables of GITHUB_ENV", exception);
                return ExitCodes.ERROR_WRONG_INPUT;
            }

            var refusals = GitHubEnvironmentFile.FindRefusals(variables);

            if (refusals.Count > 0)
            {
                foreach (var refusal in refusals)
                {
                    Console.WriteErrorLine(refusal);
                }

                Console.WriteErrorLine("Nothing was exported.");
                return ExitCodes.ERROR_WRONG_INPUT;
            }
        }

        string? payload = null;

        if (IsExportRequested && !TryFormat(resolved, options.Format, out payload))
        {
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        var values = ConfigurationMasks.ReadValues(content);
        var notes = new List<string>();
        IReadOnlyDictionary<string, string>? effective = null;
        string? unknownEffective = null;

        try
        {
            var configuration = await ReadEffectiveAsync(options.Key);
            var effectiveValues = ConfigurationMasks.ByPath(ConfigurationMasks.ReadValues(ToToken(configuration.Content)));
            var unresolved = ConfigurationMasks.FindUnresolved(values, effectiveValues);

            if (unresolved.Count > 0)
            {
                if (!IsUnresolvedAllowed)
                {
                    Console.WriteErrorLine($"These values are binding expressions that were not resolved: {Paths(unresolved)}. The machine may lack Secrets.Read. Nothing was exported; --allow-unresolved exports them as they are.");
                    return ExitCodes.ERROR_WRONG_INPUT;
                }

                notes.Add($"Binding expressions that were not resolved, exported as they are: {Paths(unresolved)}.");
            }

            // The effective document of another version says nothing about which values bindings produced.
            if (configuration.Version != resolved.Version || !string.Equals(configuration.Hash, resolved.Hash, StringComparison.Ordinal))
            {
                unknownEffective = "the effective and the resolved read returned different versions of the configuration";
            }
            else
            {
                effective = effectiveValues;
            }
        }
        catch (ApiException exception) when (exception.StatusCode == (int)HttpStatusCode.NotFound)
        {
            Console.WriteErrorLine($"The configuration for '{options.Key}' has not been found.");
            return ExitCodes.ERROR_WRONG_INPUT;
        }
        catch (ApiException exception) when (exception.StatusCode is not ((int)HttpStatusCode.Unauthorized or (int)HttpStatusCode.Forbidden))
        {
            unknownEffective = $"the effective read failed with {exception.StatusCode}";
            notes.Add($"Unresolved bindings were not checked: {unknownEffective}.");
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or TaskCanceledException)
        {
            // The reads take no token, so a cancellation is the timeout of the HttpClient.
            unknownEffective = "the effective read failed";
            notes.Add($"Unresolved bindings were not checked: {unknownEffective}.");
        }

        var mode = options.Mask!.Value;

        if (unknownEffective is not null && mode == MaskMode.Bound)
        {
            notes.Add($"Every string and number is masked: {unknownEffective}.");
        }

        var plan = ConfigurationMasks.Plan(values, effective, mode, payload is null ? null : options.Format);
        var environmentText = GitHubEnvironmentFile.Build(variables, _gitHubActions.NewDelimiter);

        // From here on a value may reach GITHUB_ENV, a file, or the log of a later step.
        Console.WriteRaw(string.Concat(plan.Data.Select(WorkflowCommands.AddMask)));

        if (IsGitHubEnvRequested && environmentText.Length > 0)
        {
            try
            {
                AppendToEnvironmentFile(_gitHubActions.EnvironmentFile!, environmentText);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Console.WriteErrorLine($"{GitHubActionsSettings.ENV} could not be written: {exception.Message}");
                return ExitCodes.ERROR_WRONG_INPUT;
            }
        }

        if (payload is not null)
        {
            var exportExitCode = WriteExportFile(payload);

            if (exportExitCode != ExitCodes.SUCCESS)
            {
                return exportExitCode;
            }
        }

        foreach (var note in notes)
        {
            Console.WriteErrorLine(note);
        }

        var names = variables.ToDictionary(variable => variable.Path, variable => variable.Name, StringComparer.Ordinal);

        if (IsGitHubEnvRequested)
        {
            Console.WriteErrorLine(variables.Count == 0
                ? $"The configuration has no values; nothing was added to {GitHubActionsSettings.ENV}."
                : $"Exported {variables.Count} variable(s) to {GitHubActionsSettings.ENV}: {string.Join(", ", variables.Select(variable => variable.Name))}.");
        }

        Console.WriteErrorLine($"Masked {plan.Masked.Count} value(s) ({(mode == MaskMode.All ? "all" : "bound")}).");

        if (plan.TooShort.Count > 0)
        {
            Console.WriteErrorLine($"Not masked, shorter than {ConfigurationMasks.MinimumBoundLength} characters: {string.Join(", ", plan.TooShort.Select(value => names.GetValueOrDefault(value.Path, value.Path)))}.");
        }

        if (payload is not null)
        {
            Console.WriteErrorLine($"Wrote the configuration to {ExportFilePath}.");
        }

        return ExitCodes.SUCCESS;
    }

    // The payload of --format and --content-only: the formatted content, or for json the content alone.
    private bool TryFormat(ConfigurationModel configuration, ConfigurationFormat? format, out string payload)
    {
        var content = ToToken(configuration.Content);

        if (format is not { } textFormat)
        {
            payload = ToContentJson(content);
            return true;
        }

        try
        {
            payload = ConfigurationFormatter.Format(content, textFormat, textFormat == ConfigurationFormat.Env ? EnvironmentPrefix : null).Text;
            return true;
        }
        catch (InvalidDocumentException exception)
        {
            WriteInvalidDocument($"as {textFormat.GetName()}", exception);
            payload = string.Empty;
            return false;
        }
    }

    // On the error output in every mode, so the standard output stays empty; the message names the path, never a value.
    private void WriteInvalidDocument(string target, InvalidDocumentException exception)
    {
        Console.WriteErrorLine($"The configuration cannot be written {target}: {exception.Message}");
        Console.WriteErrorLine($"Rule: {exception.Rule}, at {exception.Path}");
    }

    // UTF-8 without a byte order mark, and without a prompt. On Unix, a file of resolved content, which may hold secrets,
    // is the user's alone from the moment it exists: a new file with mode 0600 replaces the one at the path.
    private int WriteExportFile(string text)
    {
        var path = ExportFilePath!;

        try
        {
            if (IsResolvedRetrievalRequested && !OperatingSystem.IsWindows())
            {
                OwnerOnlyFile.WriteAllText(_fileSystem, path, text);
            }
            else
            {
                _fileSystem.File.WriteAllText(path, text, PlainOutputExtensions.Utf8);
            }

            return ExitCodes.SUCCESS;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            WriteMessage($"The configuration could not be written to '{path}': {exception.Message}");
            return ExitCodes.ERROR_WRONG_INPUT;
        }
    }

    // In one write, after a line feed when the job left its last line open: the runner would read that line and the
    // first NAME<<DELIMITER as one NAME=value, and the lines of the value as variables of their own.
    private void AppendToEnvironmentFile(string path, string text)
    {
        var isLineOpen = false;

        if (_fileSystem.File.Exists(path))
        {
            using var stream = _fileSystem.File.OpenRead(path);

            if (stream.Length > 0)
            {
                stream.Seek(-1, SeekOrigin.End);
                isLineOpen = stream.ReadByte() != '\n';
            }
        }

        _fileSystem.File.AppendAllText(path, isLineOpen ? "\n" + text : text, PlainOutputExtensions.Utf8);
    }

    private sealed record ReadOptions(string Key, ConfigurationFormat? Format, MaskMode? Mask);
}

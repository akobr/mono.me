using System;
using System.Collections.Generic;
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
using ConfigurationModel = _42.Platform.Storyteller.Sdk.Configuration;

namespace _42.Platform.Cli.Commands.Configuration;

// sform story config export <key>: the resolved configuration handed to a CI job. It always reads the resolved
// configuration and always masks: the ::add-mask:: commands go to the standard output, before any value reaches
// GITHUB_ENV or a file, and every message goes to the error output as a plain line, never with a value.
[Command(CommandNames.EXPORT, Description = "Export the resolved configuration into a CI job: print ::add-mask:: for its values, then append them to GITHUB_ENV, write them to a file, or both.")]
public class ConfigExportCommand : BaseContextCommand
{
    // The masks and the payload go to the standard output or a file as they are.
    private const bool IsPlainOutput = true;

    private readonly IConfigurationsApiClient _configurationApi;
    private readonly IFileSystem _fileSystem;
    private readonly GitHubActionsSettings _gitHubActions;

    public ConfigExportCommand(
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

    [Argument(0, Description = "An annotation key to export the resolved configuration of.")]
    public string AnnotationKey { get; set; } = string.Empty;

    [Option("--github-env", CommandOptionType.NoValue, Description = "Append every value to GITHUB_ENV of a GitHub Actions job, as a variable with its env name; needs --prefix or --allow-unprefixed.")]
    public bool IsGitHubEnvRequested { get; set; }

    [Option("--mask", CommandOptionType.SingleValue, Description = "bound (the default) or all: print ::add-mask:: for the values bindings produced, or for every string and number. Alone, without --github-env and --export, it prints only the masks.")]
    public string? Mask { get; set; }

    [Option("--prefix", CommandOptionType.SingleValue, Description = "Prepend this prefix to every variable name, for example APP_ (an ASCII letter, then ASCII letters, digits and '_'); with --github-env, and with --export in the env format.")]
    public string? Prefix { get; set; }

    [Option("--allow-unprefixed", CommandOptionType.NoValue, Description = "Let --github-env export without --prefix: the names of the configuration then reach every later step of the job.")]
    public bool IsUnprefixedAllowed { get; set; }

    [Option("--allow-unresolved", CommandOptionType.NoValue, Description = "Export binding expressions that were not resolved as they are, instead of refusing.")]
    public bool IsUnresolvedAllowed { get; set; }

    [Option("-e|--export", CommandOptionType.SingleValue, Description = "Also write the content to this file, in the format of --format; it is replaced without asking, and on Linux and macOS readable by its owner alone.")]
    public string? ExportFilePath { get; set; }

    public bool IsExportRequested => !string.IsNullOrWhiteSpace(ExportFilePath);

    [Option("-f|--format", CommandOptionType.SingleValue, Description = "The format of the --export file: env (the default), flat, properties, yaml, or json for the content alone.")]
    public string? Format { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        if (Validate() is not { } options)
        {
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        using var encoding = Console.UseUtf8Output();
        ConfigurationModel resolved;
        string? secretsWithheld;

        try
        {
            (resolved, secretsWithheld) = await ConfigurationReads.ReadAsync(_configurationApi, Context, options.Key, isResolved: true);
        }
        catch (ApiException exception) when (exception.StatusCode is not ((int)HttpStatusCode.Unauthorized or (int)HttpStatusCode.Forbidden))
        {
            return ConfigurationReads.WriteReadFailure(Console, IsPlainOutput, options.Key, exception);
        }

        // study 3.7: a consumer that cannot get the secrets fails loudly
        if (secretsWithheld is not null)
        {
            Console.WriteErrorLine($"The API withheld the secrets of the configuration ({secretsWithheld}): the values of secret bindings are their expressions. Nothing was exported. A machine receives secrets over mTLS with its certificate, or from a project that does not require mTLS for secret delivery.");
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        return await ExportAsync(options, resolved);
    }

    // Every check before the first call, so a wrong command line writes its message and nothing else.
    private ExportOptions? Validate()
    {
        if (!Platform.Storyteller.AnnotationKey.TryParse(AnnotationKey, out var key))
        {
            return Invalid("Please specify a valid annotation key; 'sform assist key' explains them.");
        }

        ConfigurationFormat? format = ConfigurationFormat.Env;

        if (Format is not null && !ConfigurationReads.TryParseFormat(Format, out format))
        {
            return Invalid($"Unknown format '{Format}'; use env, flat, properties, yaml or json.");
        }

        if (Format is not null && !IsExportRequested)
        {
            return Invalid("--format is the format of the --export file: add --export <file>.");
        }

        var mask = MaskMode.Bound;

        if (Mask is not null)
        {
            if (Ascii.EqualsIgnoreCase(Mask, "bound"))
            {
                mask = MaskMode.Bound;
            }
            else if (Ascii.EqualsIgnoreCase(Mask, "all"))
            {
                mask = MaskMode.All;
            }
            else
            {
                return Invalid($"Unknown mask '{Mask}'; use bound or all.");
            }
        }

        if (!IsGitHubEnvRequested && !IsExportRequested && Mask is null)
        {
            return Invalid("Nothing to export: add --github-env, --export <file> or both, or --mask alone to print only the masks.");
        }

        if (Prefix is not null)
        {
            if (!IsGitHubEnvRequested && !(IsExportRequested && format == ConfigurationFormat.Env))
            {
                return Invalid("--prefix applies to --github-env and to --export in the env format only.");
            }

            if (!ConfigurationReads.IsValidPrefix(Prefix))
            {
                return Invalid("--prefix must start with an ASCII letter and hold only ASCII letters, digits and '_', for example APP_.");
            }
        }

        if (IsGitHubEnvRequested && Prefix is null && !IsUnprefixedAllowed)
        {
            return Invalid("--github-env needs --prefix, for example APP_, or --allow-unprefixed: the names come from the configuration and reach every later step of the job.");
        }

        if (IsUnprefixedAllowed && !IsGitHubEnvRequested)
        {
            return Invalid("--allow-unprefixed applies to --github-env only.");
        }

        if (IsGitHubEnvRequested && _gitHubActions.EnvironmentFile is null)
        {
            return Invalid($"{GitHubActionsSettings.ENV} is not set: --github-env runs in a step of a GitHub Actions job.");
        }

        return new ExportOptions(key, format, mask);
    }

    private ExportOptions? Invalid(string message)
    {
        Console.WriteErrorLine(message);
        return null;
    }

    // One resolved and one effective read: everything is computed before anything is written, then the masks are
    // written before any value reaches GITHUB_ENV or a file.
    private async Task<int> ExportAsync(ExportOptions options, ConfigurationModel resolved)
    {
        var content = ConfigurationReads.ToToken(resolved.Content);
        IReadOnlyList<EnvironmentVariable> variables = [];

        if (IsGitHubEnvRequested)
        {
            try
            {
                variables = ConfigurationFormatter.ToEnvironmentVariables(content, Prefix);
            }
            catch (InvalidDocumentException exception)
            {
                ConfigurationReads.WriteInvalidDocument(Console, "as variables of GITHUB_ENV", exception);
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

        if (IsExportRequested && !ConfigurationReads.TryFormat(Console, resolved, options.Format, Prefix, out payload))
        {
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        var values = ConfigurationMasks.ReadValues(content);
        var notes = new List<string>();
        IReadOnlyDictionary<string, string>? effective = null;
        string? unknownEffective = null;

        try
        {
            var configuration = await ConfigurationReads.ReadEffectiveAsync(_configurationApi, Context, options.Key);
            var effectiveValues = ConfigurationMasks.ByPath(ConfigurationMasks.ReadValues(ConfigurationReads.ToToken(configuration.Content)));
            var unresolved = ConfigurationMasks.FindUnresolved(values, effectiveValues);

            if (unresolved.Count > 0)
            {
                if (!IsUnresolvedAllowed)
                {
                    Console.WriteErrorLine($"These values are binding expressions that were not resolved: {ConfigurationReads.Paths(unresolved)}. The machine may lack Secrets.Read. Nothing was exported; --allow-unresolved exports them as they are.");
                    return ExitCodes.ERROR_WRONG_INPUT;
                }

                notes.Add($"Binding expressions that were not resolved, exported as they are: {ConfigurationReads.Paths(unresolved)}.");
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

        if (unknownEffective is not null && options.Mask == MaskMode.Bound)
        {
            notes.Add($"Every string and number is masked: {unknownEffective}.");
        }

        var plan = ConfigurationMasks.Plan(values, effective, options.Mask, payload is null ? null : options.Format);
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
            var exportExitCode = ConfigurationReads.WriteFile(Console, IsPlainOutput, _fileSystem, ExportFilePath!, payload, isResolved: true);

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

        Console.WriteErrorLine($"Masked {plan.Masked.Count} value(s) ({(options.Mask == MaskMode.All ? "all" : "bound")}).");

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

    private sealed record ExportOptions(string Key, ConfigurationFormat? Format, MaskMode Mask);
}

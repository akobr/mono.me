using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using _42.Platform.Storyteller.ConfigurationFormats;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;
using Newtonsoft.Json;
using ConfigurationModel = _42.Platform.Storyteller.Sdk.Configuration;

namespace _42.Platform.Cli.Commands.Configuration;

[Subcommand(
    typeof(ConfigSetCommand),
    typeof(ConfigEditCommand),
    typeof(ConfigPatchCommand),
    typeof(ConfigDeleteCommand),
    typeof(ConfigDiffCommand),
    typeof(ConfigVersionsCommand),
    typeof(ConfigCompositionCommand),
    typeof(ConfigExportCommand))]

[Command(CommandNames.CONFIG, CommandNames.CONFIGURATION, Description = "Get and manage configuration in context of an annotation.")]
public class ConfigGetCommand : BaseContextCommand
{
    private readonly IConfigurationsApiClient _configurationApi;
    private readonly IFileSystem _fileSystem;

    public ConfigGetCommand(
        IExtendedConsole console,
        ICommandContext context,
        IConfigurationsApiClient configurationApi,
        IFileSystem fileSystem)
        : base(console, context)
    {
        _configurationApi = configurationApi;
        _fileSystem = fileSystem;
    }

    [Argument(0, Description = "An annotation key to get the configuration for.")]
    public string AnnotationKey { get; set; } = string.Empty;

    [Option("-e|--export", CommandOptionType.SingleValue, Description = "Specify a file where the retrieved configuration will be saved: the whole model, or the content alone with --format flat, properties, env or yaml or with --content-only, which then print no content.")]
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

    [Option("--env-prefix", CommandOptionType.SingleValue, Description = "Prepend this prefix to every env name, for example APP_ (an ASCII letter, then ASCII letters, digits and '_'); with --format env.")]
    public string? EnvironmentPrefix { get; set; }

    // The payload goes to the standard output or a file as it is, so every message goes to the error output.
    private bool IsPlainOutput => IsContentOnly;

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
            return ConfigurationReads.WriteReadFailure(Console, IsPlainOutput, options.Key, exception, Version);
        }

        if (secretsWithheld is not null)
        {
            Console.WriteErrorLine($"Warning: the API withheld the secrets of the configuration ({secretsWithheld}): the values of secret bindings are their expressions.");
        }

        return await WriteAsync(options, configuration);
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

        if (!ConfigurationReads.TryParseFormat(Format, out var format))
        {
            return Invalid($"Unknown format '{Format}'; use json, flat, properties, env or yaml.");
        }

        if (Version.HasValue && IsResolvedRetrievalRequested)
        {
            return Invalid("Cannot specify both --version and --resolved.");
        }

        if (EnvironmentPrefix is not null)
        {
            if (format != ConfigurationFormat.Env)
            {
                return Invalid("--env-prefix applies to --format env only.");
            }

            if (!ConfigurationReads.IsValidPrefix(EnvironmentPrefix))
            {
                return Invalid("--env-prefix must start with an ASCII letter and hold only ASCII letters, digits and '_', for example APP_.");
            }
        }

        return new ReadOptions(key, format);
    }

    private ReadOptions? Invalid(string message)
    {
        ConfigurationReads.WriteMessage(Console, IsPlainOutput, message);
        return null;
    }

    // The effective or the resolved read, or the stored content of a version.
    private async Task<(ConfigurationModel Configuration, string? SecretsWithheld)> ReadAsync(string key)
    {
        if (Version is { } version)
        {
            return (await _configurationApi.GetConfigurationVersionAsync(Context.OrganizationName, Context.ProjectName, Context.ViewName, key, version), null);
        }

        return await ConfigurationReads.ReadAsync(_configurationApi, Context, key, IsResolvedRetrievalRequested);
    }

    // The model as before, or the content alone as text.
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

        if (!ConfigurationReads.TryFormat(Console, configuration, options.Format, EnvironmentPrefix, out var payload))
        {
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        if (IsExportRequested)
        {
            return ConfigurationReads.WriteFile(Console, IsPlainOutput, _fileSystem, ExportFilePath!, payload, IsResolvedRetrievalRequested);
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
        var values = ConfigurationMasks.ReadValues(ConfigurationReads.ToToken(resolved.Content));

        if (!values.Any(value => value.Kind == DocumentValueKind.String && value.Value.StartsWith('@')))
        {
            return;
        }

        IReadOnlyList<DocumentLeaf> unresolved;

        try
        {
            var effective = await ConfigurationReads.ReadEffectiveAsync(_configurationApi, Context, key);
            unresolved = ConfigurationMasks.FindUnresolved(values, ConfigurationMasks.ByPath(ConfigurationMasks.ReadValues(ConfigurationReads.ToToken(effective.Content))));
        }
        catch (Exception exception) when (exception is ApiException or HttpRequestException or JsonException or TaskCanceledException)
        {
            return;
        }

        if (unresolved.Count > 0)
        {
            Console.WriteErrorLine($"Warning: these values are binding expressions that were not resolved: {ConfigurationReads.Paths(unresolved)}. The caller may lack Secrets.Read.");
        }
    }

    private sealed record ReadOptions(string Key, ConfigurationFormat? Format);
}

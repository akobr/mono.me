using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using _42.Platform.Storyteller.ConfigurationFormats;
using _42.Platform.Storyteller.Sdk;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ConfigurationModel = _42.Platform.Storyteller.Sdk.Configuration;

namespace _42.Platform.Cli.Commands.Configuration;

// What sform story config and sform story config export share: the reads of a configuration, its content as JSON or
// as the text of a format, the file of --export, and the messages of a read that failed or of a document that a format
// cannot write. A message is a plain line on the error output next to a payload, a line of the console otherwise.
internal static class ConfigurationReads
{
    public const string JsonFormat = "json";

    // The effective or the resolved read through the conditional twins, which keep the headers.
    public static async Task<(ConfigurationModel Configuration, string? SecretsWithheld)> ReadAsync(IConfigurationsApiClient api, ICommandContext context, string key, bool isResolved)
    {
        var response = isResolved
            ? await api.GetResolvedConfigurationConditionalAsync(context.OrganizationName, context.ProjectName, context.ViewName, key)
            : await api.GetConfigurationConditionalAsync(context.OrganizationName, context.ProjectName, context.ViewName, key);

        return (ResultOf(response), response.SecretsWithheld);
    }

    public static async Task<ConfigurationModel> ReadEffectiveAsync(IConfigurationsApiClient api, ICommandContext context, string key)
    {
        return (await ReadAsync(api, context, key, isResolved: false)).Configuration;
    }

    // json and no name are the JSON content (null); false for a name that is no format.
    public static bool TryParseFormat(string? name, out ConfigurationFormat? format)
    {
        format = null;

        if (name is null || Ascii.EqualsIgnoreCase(name, JsonFormat))
        {
            return true;
        }

        if (!ConfigurationFormatNames.TryParse(name, out var parsed))
        {
            return false;
        }

        format = parsed;
        return true;
    }

    public static bool IsValidPrefix(string prefix)
    {
        return prefix.Length > 0
            && char.IsAsciiLetter(prefix[0])
            && prefix.All(character => char.IsAsciiLetterOrDigit(character) || character == '_');
    }

    public static JToken? ToToken(object? content)
    {
        return content as JToken ?? (content is null ? null : JToken.FromObject(content));
    }

    // The content with two-space indentation and LF line ends; {} for a configuration without content.
    public static string ToContentJson(JToken? content)
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

    public static string Paths(IEnumerable<DocumentLeaf> values)
    {
        return string.Join(", ", values.Select(value => value.Path));
    }

    public static void WriteMessage(IExtendedConsole console, bool isPlainOutput, string message)
    {
        if (isPlainOutput)
        {
            console.WriteErrorLine(message);
            return;
        }

        console.WriteLine(message);
    }

    // 401 and 403 never get here: Program reports them.
    public static int WriteReadFailure(IExtendedConsole console, bool isPlainOutput, string key, ApiException exception, int? version = null)
    {
        if (exception.StatusCode == (int)HttpStatusCode.NotFound)
        {
            WriteMessage(console, isPlainOutput, version.HasValue
                ? $"The version {version} of the configuration for '{key}' has not been found."
                : $"The configuration for '{key}' has not been found.");
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        // A binding that cannot be evaluated: the configuration is wrong, not the tool.
        if (exception.StatusCode == (int)HttpStatusCode.UnprocessableEntity)
        {
            var (message, errorCode) = KeyValueConsoleExtensions.ReadError(exception);
            WriteMessage(console, isPlainOutput, string.IsNullOrEmpty(message) ? "The configuration cannot be resolved." : message);

            if (!string.IsNullOrEmpty(errorCode))
            {
                WriteMessage(console, isPlainOutput, $"Error code: {errorCode}");
            }

            return ExitCodes.ERROR_WRONG_INPUT;
        }

        return isPlainOutput ? console.WriteApiErrorLine(exception) : console.WriteApiError(exception);
    }

    // The formatted content, or for json (null) the content alone; the prefix applies to env only.
    public static bool TryFormat(IExtendedConsole console, ConfigurationModel configuration, ConfigurationFormat? format, string? envPrefix, out string payload)
    {
        var content = ToToken(configuration.Content);

        if (format is not { } textFormat)
        {
            payload = ToContentJson(content);
            return true;
        }

        try
        {
            payload = ConfigurationFormatter.Format(content, textFormat, textFormat == ConfigurationFormat.Env ? envPrefix : null).Text;
            return true;
        }
        catch (InvalidDocumentException exception)
        {
            WriteInvalidDocument(console, $"as {textFormat.GetName()}", exception);
            payload = string.Empty;
            return false;
        }
    }

    // On the error output in every mode, so the standard output stays empty; the message names the path, never a value.
    public static void WriteInvalidDocument(IExtendedConsole console, string target, InvalidDocumentException exception)
    {
        console.WriteErrorLine($"The configuration cannot be written {target}: {exception.Message}");
        console.WriteErrorLine($"Rule: {exception.Rule}, at {exception.Path}");
    }

    // UTF-8 without a byte order mark, and without a prompt. On Unix, a file of resolved content, which may hold secrets,
    // is the user's alone from the moment it exists: a new file with mode 0600 replaces the one at the path.
    public static int WriteFile(IExtendedConsole console, bool isPlainOutput, IFileSystem fileSystem, string path, string text, bool isResolved)
    {
        try
        {
            if (isResolved && !OperatingSystem.IsWindows())
            {
                OwnerOnlyFile.WriteAllText(fileSystem, path, text);
            }
            else
            {
                fileSystem.File.WriteAllText(path, text, PlainOutputExtensions.Utf8);
            }

            return ExitCodes.SUCCESS;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            WriteMessage(console, isPlainOutput, $"The configuration could not be written to '{path}': {exception.Message}");
            return ExitCodes.ERROR_WRONG_INPUT;
        }
    }

    // Without a tag the API has nothing to answer 304 to; an answer without a body is a failure of the read.
    private static ConfigurationModel ResultOf(ConditionalResponse<ConfigurationModel> response)
    {
        return response.Result
            ?? throw new ApiException($"The API answered {response.StatusCode} without a configuration.", response.StatusCode, string.Empty, response.Headers, null);
    }
}

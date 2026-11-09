using System;
using System.IO.Abstractions;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using _42.Platform.Cli.Output.Exceptions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Cli.Commands;

public static class JsonInputBuilder
{
    // Only copies the -i file into the empty document; the -x values are written by path (InlineProperties).
    private static readonly JsonMergeSettings JsonMergeOptions = new()
    {
        MergeArrayHandling = MergeArrayHandling.Union,
        MergeNullValueHandling = MergeNullValueHandling.Ignore,
        PropertyNameComparison = StringComparison.Ordinal,
    };

    /// <summary>
    /// Builds a JSON document from an imported file and inline properties (in this order).
    /// </summary>
    /// <param name="console">The console for error messages, written as plain lines on its standard error.</param>
    /// <param name="fileSystem">The file system to read the imported file from.</param>
    /// <param name="importFilePath">An optional path of a JSON file.</param>
    /// <param name="inlineProperties">
    /// Optional inline properties in the format <c>path=value</c>, written into the document at their paths in order
    /// (see <see cref="InlineProperties"/>).
    /// </param>
    /// <returns>The built document.</returns>
    /// <exception cref="WrongInputException">Thrown when the file doesn't exist or holds no JSON object, or an inline property has a wrong format or cannot be set.</exception>
    public static async Task<JObject> BuildAsync(
        IExtendedConsole console,
        IFileSystem fileSystem,
        string? importFilePath,
        string[]? inlineProperties)
    {
        var content = new JObject();

        if (!string.IsNullOrWhiteSpace(importFilePath))
        {
            if (!fileSystem.File.Exists(importFilePath))
            {
                var message = $"The file '{fileSystem.Path.GetFullPath(importFilePath)}' does not exist.";
                console.WriteErrorLine(message);
                throw new WrongInputException(message);
            }

            // A string that looks like a date stays the string of the file, as the SDK reads it, rather than a date
            // converted to the local time zone; an -x value over it is then a string, not a date.
            JObject fileContent;

            try
            {
                using var fileReader = fileSystem.File.OpenText(importFilePath);
                await using var jsonReader = new JsonTextReader(fileReader) { DateParseHandling = DateParseHandling.None };
                fileContent = await JObject.LoadAsync(
                    jsonReader,
                    new JsonLoadSettings
                    {
                        CommentHandling = CommentHandling.Ignore,
                        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Ignore,
                        LineInfoHandling = LineInfoHandling.Ignore,
                    });
            }
            catch (JsonReaderException exception)
            {
                // a file that is not JSON, or a JSON array or value: the input is wrong, not the tool
                var message = $"The file '{fileSystem.Path.GetFullPath(importFilePath)}' is not a JSON object: {exception.Message}";
                console.WriteImportant(message);
                throw new WrongInputException(message);
            }

            content.Merge(fileContent, JsonMergeOptions);
        }

        if (inlineProperties?.Length > 0)
        {
            try
            {
                InlineProperties.Apply(content, inlineProperties);
            }
            catch (WrongInputException exception)
            {
                console.WriteErrorLine(exception.Message);
                throw;
            }
        }

        return content;
    }
}

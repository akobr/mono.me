using System;
using System.IO.Abstractions;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output.Exceptions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Cli.Commands;

public static class JsonInputBuilder
{
    private static readonly JsonMergeSettings JsonMergeOptions = new()
    {
        MergeArrayHandling = MergeArrayHandling.Union,
        MergeNullValueHandling = MergeNullValueHandling.Ignore,
        PropertyNameComparison = StringComparison.Ordinal,
    };

    /// <summary>
    /// Builds a JSON document from an imported file and inline properties (in this order).
    /// </summary>
    /// <param name="console">The console for error messages.</param>
    /// <param name="fileSystem">The file system to read the imported file from.</param>
    /// <param name="importFilePath">An optional path of a JSON file.</param>
    /// <param name="inlineProperties">Optional inline properties in the format <c>name=value</c>.</param>
    /// <returns>The built document.</returns>
    /// <exception cref="WrongInputException">Thrown when the file doesn't exist or an inline property has a wrong format.</exception>
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
                console.WriteImportant(message);
                throw new WrongInputException(message);
            }

            using var fileReader = fileSystem.File.OpenText(importFilePath);
            await using var jsonReader = new JsonTextReader(fileReader);
            var fileContent = await JObject.LoadAsync(
                jsonReader,
                new JsonLoadSettings
                {
                    CommentHandling = CommentHandling.Ignore,
                    DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Ignore,
                    LineInfoHandling = LineInfoHandling.Ignore,
                });

            content.Merge(fileContent, JsonMergeOptions);
        }

        if (inlineProperties?.Length > 0)
        {
            JObject inlineContent = new();

            foreach (var inlineProperty in inlineProperties)
            {
                var parts = inlineProperty.Split('=', 2);

                if (parts.Length != 2)
                {
                    var message = $"The inline property '{inlineProperty}' is not in the correct format.";
                    console.WriteImportant(message);
                    throw new WrongInputException(message);
                }

                inlineContent[parts[0]] = new JValue(PropertyValueParser.ParseValue(parts[1]));
            }

            content.Merge(inlineContent, JsonMergeOptions);
        }

        return content;
    }
}

using System;
using System.IO.Abstractions;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output.Exceptions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Cli.Json;

/// <summary>
/// Reads a JSON Patch document (RFC 6902) from a file.
/// </summary>
public static class JsonPatchDocumentReader
{
    /// <summary>
    /// Reads and checks a JSON Patch array. The returned array is the file's operations, unchanged.
    /// </summary>
    /// <param name="console">The console for error messages.</param>
    /// <param name="fileSystem">The file system to read from.</param>
    /// <param name="importFilePath">The path of the patch file.</param>
    /// <returns>The patch operations.</returns>
    /// <exception cref="WrongInputException">Thrown when the file is missing, is not a JSON array, or an operation is not a valid patch operation.</exception>
    public static async Task<JArray> ReadAsync(IExtendedConsole console, IFileSystem fileSystem, string importFilePath)
    {
        if (!fileSystem.File.Exists(importFilePath))
        {
            var message = $"The file '{fileSystem.Path.GetFullPath(importFilePath)}' does not exist.";
            console.WriteImportant(message);
            throw new WrongInputException(message);
        }

        JToken token;

        try
        {
            using var fileReader = fileSystem.File.OpenText(importFilePath);
            using var jsonReader = new JsonTextReader(fileReader);
            token = await JToken.LoadAsync(
                jsonReader,
                new JsonLoadSettings
                {
                    CommentHandling = CommentHandling.Ignore,
                    DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Ignore,
                    LineInfoHandling = LineInfoHandling.Ignore,
                });

            if (await jsonReader.ReadAsync())
            {
                throw new JsonReaderException("Additional content was found after the JSON Patch array.");
            }
        }
        catch (JsonReaderException exception)
        {
            var message = $"Invalid JSON Patch document: {exception.Message}";
            console.WriteImportant(message);
            throw new WrongInputException(message, exception);
        }

        if (token is not JArray patch)
        {
            throw Fail(console, "The patch file must be a JSON array of operations (RFC 6902).");
        }

        for (var index = 0; index < patch.Count; index++)
        {
            ValidateOperation(console, patch[index], index);
        }

        return patch;
    }

    private static void ValidateOperation(IExtendedConsole console, JToken token, int index)
    {
        if (token is not JObject operation)
        {
            throw Fail(console, $"Operation {index} must be a JSON object.");
        }

        if (operation.Property("op", StringComparison.Ordinal)?.Value is not JValue { Type: JTokenType.String } opValue
            || opValue.Value is not string op)
        {
            throw Fail(console, $"Operation {index} is missing a string 'op'.");
        }

        if (operation.Property("path", StringComparison.Ordinal)?.Value is not JValue { Type: JTokenType.String })
        {
            throw Fail(console, $"Operation {index} is missing a string 'path'.");
        }

        switch (op)
        {
            case "add":
            case "replace":
            case "test":
                if (operation.Property("value", StringComparison.Ordinal) is null)
                {
                    throw Fail(console, $"Operation {index} is missing 'value'.");
                }

                break;

            case "remove":
                break;

            case "move":
            case "copy":
                if (operation.Property("from", StringComparison.Ordinal)?.Value is not JValue { Type: JTokenType.String })
                {
                    throw Fail(console, $"Operation {index} is missing a string 'from'.");
                }

                break;

            default:
                throw Fail(console, $"Operation {index} has an unknown op '{op}'.");
        }
    }

    private static WrongInputException Fail(IExtendedConsole console, string message)
    {
        console.WriteImportant(message);
        return new WrongInputException(message);
    }
}

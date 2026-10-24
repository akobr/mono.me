using System;
using System.IO;
using System.IO.Abstractions;

namespace _42.Platform.Storyteller.Sdk.Credentials;

internal static class CredentialFiles
{
    // Read before every use, never cached: the platform may renew the file while the process runs.
    // The source names where the path came from (a variable or an option), for the message.
    public static string ReadText(IFileSystem fileSystem, string path, string? source, string description)
    {
        var origin = source is null ? string.Empty : $" in {source}";
        string content;

        try
        {
            if (!fileSystem.File.Exists(path))
            {
                throw StorytellerAuthenticationException.Configuration($"The {description} {path}{origin} does not exist.");
            }

            content = fileSystem.File.ReadAllText(path).Trim();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            throw StorytellerAuthenticationException.Configuration($"The {description} {path}{origin} could not be read.", exception);
        }

        return content.Length > 0
            ? content
            : throw StorytellerAuthenticationException.Configuration($"The {description} {path}{origin} is empty.");
    }
}

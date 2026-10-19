using System;
using System.IO;
using System.IO.Abstractions;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Identity.Client.Extensions.Msal;

namespace _42.Platform.Cli.Telemetry;

// The anonymous installation id in telemetry.json, in the ~/.42for.net directory of the token store, which survives
// tool updates. The id is a random Guid, never derived from the machine, the user or a token.
public sealed class InstallationIdStore
{
    private static readonly JsonSerializerOptions FileJson = new() { WriteIndented = true };

    private readonly IFileSystem _fileSystem;
    private readonly string _directory;
    private readonly string _filePath;

    public InstallationIdStore(IFileSystem fileSystem)
        : this(fileSystem, fileSystem.Path.Combine(MsalCacheHelper.UserRootDirectory, ".42for.net"))
    {
    }

    internal InstallationIdStore(IFileSystem fileSystem, string directory)
    {
        _fileSystem = fileSystem;
        _directory = directory;
        _filePath = fileSystem.Path.Combine(directory, Constants.TELEMETRY_JSON);
    }

    // A missing, empty, damaged or non-Guid id is replaced by a new one. False when the id cannot be read
    // nor written: an id that is not kept would count every run as a new installation.
    public bool TryGetOrCreate(out string id, out bool created)
    {
        id = string.Empty;
        created = false;

        try
        {
            if (TryRead(out var stored))
            {
                id = stored;
                return true;
            }

            var newId = Guid.NewGuid().ToString("N");
            _fileSystem.Directory.CreateDirectory(_directory);
            _fileSystem.File.WriteAllText(_filePath, JsonSerializer.Serialize(new TelemetryFile { InstallationId = newId }, FileJson));

            id = newId;
            created = true;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or JsonException)
        {
            return false;
        }
    }

    private bool TryRead(out string id)
    {
        id = string.Empty;

        if (!_fileSystem.File.Exists(_filePath))
        {
            return false;
        }

        try
        {
            var stored = JsonSerializer.Deserialize<TelemetryFile>(_fileSystem.File.ReadAllText(_filePath));

            if (!Guid.TryParse(stored?.InstallationId, out var guid) || guid == Guid.Empty)
            {
                return false;
            }

            id = guid.ToString("N");
            return true;
        }
        catch (JsonException)
        {
            // A damaged file gets a new id.
            return false;
        }
    }

    private sealed class TelemetryFile
    {
        [JsonPropertyName("installationId")]
        public string? InstallationId { get; set; }
    }
}

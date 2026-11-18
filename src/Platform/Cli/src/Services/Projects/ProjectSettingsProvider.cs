using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Commands;
using _42.Platform.Cli.Commands.Projects;
using _42.Platform.Cli.Configuration;
using _42.Platform.Cli.Output;
using _42.Platform.Storyteller.Sdk;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SdkSettings = _42.Platform.Storyteller.Sdk.ProjectSettings;

namespace _42.Platform.Cli.Services.Projects;

// Loads the settings of the context project for the console output (spec section 7.3) and keeps them in
// project-settings.cache.json of the state directory, per API and project, with their ETag and when they were read.
// Within FreshFor of a read the cache is used without a request; after that GET …/settings asks with If-None-Match, and
// a 304 keeps it. sform project settings set, edit and reset write their answer into it (IProjectSettingsCache). A failed
// read keeps showing what was read before (a refusal shows nothing of it) and is remembered for RetryAfterFailure.
// Without a console, with the output redirected to a file or a pipe (also from a terminal), with --builtin,
// SFORM_PROJECT_SETTINGS=builtin or output.projectSettings=builtin, no request is made. Any failure (offline, 401, a 403
// of a machine without a read scope, a 404 of an older API, a broken cache file) gives the built-in display and one
// debug line in the log: a command never fails because of the settings.
public sealed class ProjectSettingsProvider : IProjectSettingsProvider, IProjectSettingsCache
{
    // How long a read is used without asking the API again.
    public static readonly TimeSpan FreshFor = TimeSpan.FromMinutes(10);

    // How long the console output waits for the settings before it shows the cached or the built-in names.
    public static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(2);

    // How long after a failed read no request is made again, so a bad network costs one wait, not one per command.
    public static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(1);

    private static readonly JsonSerializerOptions CacheJson = new() { WriteIndented = true };

    private readonly IAccessApiClient _accessApi;
    private readonly ICommandContext _context;
    private readonly IFileSystem _fileSystem;
    private readonly string _cachePath;
    private readonly string _baseUrl;
    private readonly OutputSwitches _switches;
    private readonly OutputTerminal _terminal;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly Dictionary<string, ProjectDisplay> _displays = new(StringComparer.Ordinal);

    public ProjectSettingsProvider(
        IExtendedConsole console,
        ICommandContext context,
        IAccessApiClient accessApi,
        IFileSystem fileSystem,
        CliPaths paths,
        IOptions<GeneralOptions> general,
        OutputSwitches switches,
        TimeProvider timeProvider,
        ILogger<ProjectSettingsProvider> logger)
        : this(accessApi, context, fileSystem, paths.ProjectSettingsCacheFile, general.Value.BaseUrl, switches, OutputTerminal.Of(console), timeProvider, logger)
    {
    }

    internal ProjectSettingsProvider(
        IAccessApiClient accessApi,
        ICommandContext context,
        IFileSystem fileSystem,
        string cachePath,
        string? baseUrl,
        OutputSwitches switches,
        OutputTerminal terminal,
        TimeProvider timeProvider,
        ILogger logger)
    {
        _accessApi = accessApi;
        _context = context;
        _fileSystem = fileSystem;
        _cachePath = cachePath;
        _baseUrl = baseUrl ?? string.Empty;
        _switches = switches;
        _terminal = terminal;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<ProjectDisplay> GetDisplayAsync(CancellationToken cancellationToken = default)
    {
        if (!_terminal.HasConsole() || !_switches.UsesProjectSettings || !_context.IsProjectKnown)
        {
            return ProjectDisplay.BuiltIn;
        }

        var project = $"{_context.OrganizationName}.{_context.ProjectName}";

        if (_displays.TryGetValue(project, out var known))
        {
            return known;
        }

        var display = await LoadAsync(project, cancellationToken);
        _displays[project] = display;
        return display;
    }

    public Task RememberAsync(string organization, string project, SdkSettings settings, string? etag, CancellationToken cancellationToken = default)
    {
        try
        {
            var key = $"{organization}.{project}";
            WriteCache(ReadCache().With(Entry(key, settings, etag)));
            _displays.Remove(key);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "The project settings could not be kept in {Path}", _cachePath);
        }

        return Task.CompletedTask;
    }

    private async Task<ProjectDisplay> LoadAsync(string project, CancellationToken cancellationToken)
    {
        try
        {
            var cache = ReadCache();
            var entry = cache.Find(_baseUrl, project);
            var now = _timeProvider.GetUtcNow();

            if (entry?.FailedAt is { } failedAt && failedAt <= now && now - failedAt < RetryAfterFailure)
            {
                return StaleOrBuiltIn(entry);
            }

            if (entry is { HasSettings: true, FailedAt: null } && entry.FetchedAt <= now && now - entry.FetchedAt < FreshFor)
            {
                return Display(entry.Settings);
            }

            try
            {
                var fetched = await FetchAsync(project, entry is { HasSettings: true } ? entry : null, now, cancellationToken);
                TryWriteCache(cache.With(fetched));
                return Display(fetched.Settings);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogDebug(exception, "The project settings of {Project} could not be read; the console output shows the cached or the built-in names and dates", project);

                // A refusal (signed out, no read scope, no such project, an older API) shows nothing of what was cached.
                var refused = exception is ApiException { StatusCode: 401 or 403 or 404 };
                var failed = (refused || entry is null
                    ? new ProjectSettingsCacheEntry { BaseUrl = _baseUrl, Project = project }
                    : entry) with { FailedAt = now };
                TryWriteCache(cache.With(failed));
                return StaleOrBuiltIn(failed);
            }
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "The project settings of {Project} could not be read; the console output shows the built-in names and dates", project);
            return ProjectDisplay.BuiltIn;
        }
    }

    // The settings read before a failure, else the built-in display.
    private ProjectDisplay StaleOrBuiltIn(ProjectSettingsCacheEntry entry)
    {
        return entry.HasSettings ? Display(entry.Settings) : ProjectDisplay.BuiltIn;
    }

    // The settings of the API: the cached ones again on a 304 of their ETag.
    private async Task<ProjectSettingsCacheEntry> FetchAsync(string project, ProjectSettingsCacheEntry? cached, DateTimeOffset now, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ReadTimeout);

        var response = await _accessApi.GetProjectSettingsConditionalAsync(_context.OrganizationName, _context.ProjectName, cached?.ETag, timeout.Token);

        if (response.IsNotModified && cached is not null)
        {
            return cached with { FetchedAt = now, FailedAt = null };
        }

        return Entry(project, response.Result ?? throw new InvalidOperationException("The API answered no project settings."), response.ETag);
    }

    private ProjectSettingsCacheEntry Entry(string project, SdkSettings settings, string? etag)
    {
        return new ProjectSettingsCacheEntry
        {
            BaseUrl = _baseUrl,
            Project = project,
            ETag = etag ?? ProjectSettingsDocuments.ETagOf(settings.Version),
            FetchedAt = _timeProvider.GetUtcNow(),
            Settings = ProjectSettingsCacheValues.From(settings),
        };
    }

    private ProjectDisplay Display(ProjectSettingsCacheValues settings)
    {
        var getVariable = _switches.GetVariable;
        var colors = TerminalCapabilities.IsNoColor(getVariable)
            ? TypeColorMode.None
            : TerminalCapabilities.SupportsTrueColor(getVariable) && _terminal.TakesEscapeCodes()
                ? TypeColorMode.TrueColor
                : TypeColorMode.Console;
        var icons = _switches.ShowsIcons(_terminal.OutputEncoding());

        return ProjectDisplay.Create(settings.ToAppearances(), settings.DateFormat, colors, icons);
    }

    // An empty cache for a missing or unreadable file.
    private ProjectSettingsCacheFile ReadCache()
    {
        try
        {
            if (!_fileSystem.File.Exists(_cachePath))
            {
                return new ProjectSettingsCacheFile();
            }

            return JsonSerializer.Deserialize<ProjectSettingsCacheFile>(_fileSystem.File.ReadAllText(_cachePath)) is { Entries: not null } file
                ? file
                : new ProjectSettingsCacheFile();
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "The cache of the project settings at {Path} could not be read", _cachePath);
            return new ProjectSettingsCacheFile();
        }
    }

    private void TryWriteCache(ProjectSettingsCacheFile file)
    {
        try
        {
            WriteCache(file);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "The project settings could not be kept in {Path}", _cachePath);
        }
    }

    // Written next to the file and moved over it, so a run that reads it meanwhile never sees half of it.
    private void WriteCache(ProjectSettingsCacheFile file)
    {
        var directory = _fileSystem.Path.GetDirectoryName(_cachePath);

        if (!string.IsNullOrEmpty(directory))
        {
            _fileSystem.Directory.CreateDirectory(directory);
        }

        var temporary = $"{_cachePath}.{Guid.NewGuid():N}.tmp";
        _fileSystem.File.WriteAllText(temporary, JsonSerializer.Serialize(file, CacheJson));

        try
        {
            _fileSystem.File.Move(temporary, _cachePath, overwrite: true);
        }
        finally
        {
            if (_fileSystem.File.Exists(temporary))
            {
                _fileSystem.File.Delete(temporary);
            }
        }
    }
}

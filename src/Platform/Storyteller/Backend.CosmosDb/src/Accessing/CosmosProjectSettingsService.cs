using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Entities.Access;
using _42.Platform.Storyteller.KeyValues;
using _42.Platform.Storyteller.KeyValues.Audit;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Accessing;

// The settings of a project on its access point apt.{organization}.{project} in the partition access of core. A write
// reads the document with its ETag, checks the caller's condition against the settings' Version, computes and validates
// the new settings, and replaces the document with only Settings changed under the ETag, so the members, the policies
// and the description stay; a concurrent write of another member only moves the ETag, so one retry suffices. The audit
// event follows the replace, best effort.
public sealed class CosmosProjectSettingsService : IProjectSettingsService
{
    private const string Partition = AccessPointEntity.Partition;

    private readonly IContainerRepositoryProvider _repositoryProvider;
    private readonly JsonSerializerOptions _serializerOptions;
    private readonly IKeyValueAuditLog? _audit;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CosmosProjectSettingsService> _logger;

    public CosmosProjectSettingsService(
        IContainerRepositoryProvider repositoryProvider,
        IOptions<JsonSerializerOptions> serializerOptions,
        IKeyValueAuditLog? audit = null,
        TimeProvider? timeProvider = null,
        ILogger<CosmosProjectSettingsService>? logger = null)
    {
        _repositoryProvider = repositoryProvider;
        _serializerOptions = serializerOptions.Value;
        _audit = audit;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<CosmosProjectSettingsService>.Instance;
    }

    public async Task<ProjectSettings> GetAsync(string organization, string project, CancellationToken cancellationToken = default)
    {
        var repository = _repositoryProvider.GetCore();
        var entity = await repository.Container.TryReadItemAsync(
            IdOf(organization, project),
            new PartitionKey(Partition),
            stream => stream.DeserializeSystemTextJson<AccessPointEntity>(_serializerOptions),
            cancellationToken: cancellationToken);

        if (entity is null)
        {
            throw NotFound(organization, project);
        }

        return entity.Settings?.ToModel() ?? new ProjectSettings();
    }

    public Task<ProjectSettingsWrite> SetAsync(
        string organization,
        string project,
        ProjectSettings settings,
        ProjectSettingsCondition condition,
        KeyValueCaller caller,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        // Version and Modified of the input are no part of the values, so they are dropped before the rules run.
        var input = settings with { Version = 0, Modified = null };
        return WriteAsync(organization, project, condition, caller, _ => Valid(ProjectSettingsRules.Normalize(input)), cancellationToken);
    }

    public Task<ProjectSettingsWrite> PatchAsync(
        string organization,
        string project,
        JsonObject patch,
        ProjectSettingsCondition condition,
        KeyValueCaller caller,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(patch);

        // Applied to the settings of each attempt's read, so a retry merges into the fresh state. The Api shape carries
        // Version and Modified, which the reading ignores.
        return WriteAsync(
            organization,
            project,
            condition,
            caller,
            stored => Valid(ProjectSettingsJson.Read(ProjectSettingsJson.ApplyMergePatch(ProjectSettingsJson.ToJson(stored, ProjectSettingsJsonShape.Api), patch))),
            cancellationToken);
    }

    // The detail of a ProjectSettingsChange event: "v{old}→v{new}: " and the changed parts, Types.{code} per changed type
    // in the catalog's order, DefaultTimeZone and DateFormat; names only, never values.
    internal static string DetailOf(ProjectSettings before, ProjectSettings after)
    {
        var parts = new List<string>();

        foreach (var code in ProjectSettingsCatalog.Default.TypeCodes)
        {
            if (AppearanceOf(before, code) != AppearanceOf(after, code))
            {
                parts.Add($"{nameof(ProjectSettings.Types)}.{code}");
            }
        }

        if (!string.Equals(before.DefaultTimeZone, after.DefaultTimeZone, StringComparison.Ordinal))
        {
            parts.Add(nameof(ProjectSettings.DefaultTimeZone));
        }

        if (!string.Equals(before.DateFormat, after.DateFormat, StringComparison.Ordinal))
        {
            parts.Add(nameof(ProjectSettings.DateFormat));
        }

        return $"v{before.Version}→v{after.Version}: {string.Join(", ", parts)}";
    }

    private static string IdOf(string organization, string project) => $"{AccessPointEntity.IdPrefix}{organization}.{project}";

    private static NotFoundException NotFound(string organization, string project)
    {
        return new NotFoundException($"The project '{organization}.{project}' doesn't exist.");
    }

    private static ProjectSettings Valid(ProjectSettingsCheck check)
    {
        return check.IsValid ? check.Settings : throw new ProjectSettingsInvalidException(check.Problems);
    }

    private static ProjectTypeAppearance? AppearanceOf(ProjectSettings settings, string code)
    {
        var appearance = settings.Types?.FirstOrDefault(entry => string.Equals(entry.Key, code, StringComparison.OrdinalIgnoreCase)).Value;
        return appearance is { Label: null, Plural: null, Color: null, Icon: null } ? null : appearance;
    }

    private async Task<ProjectSettingsWrite> WriteAsync(
        string organization,
        string project,
        ProjectSettingsCondition condition,
        KeyValueCaller caller,
        Func<ProjectSettings, ProjectSettings> compute,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(caller);

        var repository = _repositoryProvider.GetCore();
        var partitionKey = new PartitionKey(Partition);
        var id = IdOf(organization, project);

        for (var attempt = 1; ; attempt++)
        {
            var (entity, eTag) = await repository.Container.TryReadItemWithETagAsync(
                id,
                partitionKey,
                stream => stream.DeserializeSystemTextJson<AccessPointEntity>(_serializerOptions),
                cancellationToken: cancellationToken);

            if (entity is null)
            {
                throw NotFound(organization, project);
            }

            var stored = entity.Settings?.ToModel() ?? new ProjectSettings();

            // the caller's condition is about the settings it read, so it is never retried
            if (!condition.Holds(stored.Version))
            {
                throw new ProjectSettingsPreconditionFailedException(stored.Version);
            }

            var computed = compute(stored);

            if (ProjectSettingsRules.SameValues(stored, computed))
            {
                return new ProjectSettingsWrite(stored, Changed: false);
            }

            var now = _timeProvider.GetUtcNow();
            var next = computed with { Version = stored.Version + 1, Modified = AuditStamp.Of(caller.Actor, now) };
            var updated = entity with { Settings = ProjectSettingsEntity.From(next) };

            try
            {
                await repository.Container.ReplaceItemAsync(
                    updated,
                    updated.Id,
                    partitionKey,
                    new ItemRequestOptions { IfMatchEtag = eTag },
                    cancellationToken);
            }
            catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.PreconditionFailed)
            {
                if (attempt > 1)
                {
                    throw new ConflictException($"The project '{organization}.{project}' was changed concurrently, try again.", ErrorCodes.Conflict);
                }

                continue;
            }

            await AuditAsync(organization, project, caller, stored, next, now);
            return new ProjectSettingsWrite(next, Changed: true);
        }
    }

    // Best effort: the settings are stored, so a failed append is logged and the write still succeeds (a retry of the
    // request would change nothing and write no event).
    private async Task AuditAsync(string organization, string project, KeyValueCaller caller, ProjectSettings before, ProjectSettings after, DateTimeOffset now)
    {
        if (_audit is null)
        {
            return;
        }

        try
        {
            await _audit.WriteAsync(organization, project, new KeyValueAuditEvent
            {
                Id = CosmosKeyValueAuditLog.NewId(now),
                Time = now,
                Operation = KeyValueAuditOperation.ProjectSettingsChange,
                Actor = caller.Actor,
                Channel = caller.Channel,
                CorrelationId = caller.CorrelationId,
                Detail = DetailOf(before, after),
            });
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "The change of the settings of the project {Organization}.{Project} to version {Version} was not recorded in its audit trail.", organization, project, after.Version);
        }
    }
}

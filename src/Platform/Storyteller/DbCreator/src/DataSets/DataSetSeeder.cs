using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.DbCreator.Configuration;
using _42.Platform.Storyteller.Transferring;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.DbCreator.DataSets;

// Seeds data sets into one target through the services of the API, as the System actor and with Existing = Skip: what
// exists stays, what is missing is created, so a run again or after a stop completes what is missing. Writes nothing
// to the console; the result tells what happened.
public sealed class DataSetSeeder
{
    private readonly IFileSystem _fileSystem;
    private readonly DataSetCatalog _catalog;
    private readonly DataSetProvisioner _provisioner;
    private readonly IViewImporter _importer;
    private readonly IAnnotationBulkWriter _bulkWriter;
    private readonly IDataSetSeedRecordStore _records;
    private readonly BaseAccountOptions _baseAccount;
    private readonly TimeProvider _time;
    private readonly ILogger<DataSetSeeder> _logger;

    public DataSetSeeder(
        IFileSystem fileSystem,
        DataSetCatalog catalog,
        DataSetProvisioner provisioner,
        IViewImporter importer,
        IAnnotationBulkWriter bulkWriter,
        IDataSetSeedRecordStore records,
        IOptions<BaseAccountOptions> baseAccount,
        TimeProvider time,
        ILogger<DataSetSeeder> logger)
    {
        _fileSystem = fileSystem;
        _catalog = catalog;
        _provisioner = provisioner;
        _importer = importer;
        _bulkWriter = bulkWriter;
        _records = records;
        _baseAccount = baseAccount.Value;
        _time = time;
        _logger = logger;
    }

    // Resolves the sets, the target and the owner, makes the target exist, then seeds every set after its
    // dependencies. A set that depends on a set with failures is not seeded. DataSetException for wrong input, before
    // anything is written; the containers must exist (CoreDbStructureBuilder.EnsureContainersAsync).
    public async Task<DataSetRunResult> SeedAsync(DataSetSeedRequest request, CancellationToken cancellationToken = default)
    {
        return await SeedAsync(Plan(request), request.Options, cancellationToken);
    }

    // The sets, the target and the owner of a request, from the files and the options alone, so wrong input is
    // refused before the database is touched. DataSetException for wrong input.
    public DataSetSeedPlan Plan(DataSetSeedRequest request)
    {
        var catalog = _catalog.GetSets(request.Paths);
        var names = request.Sets.Count > 0
            ? request.Sets.Select(name => name.Trim()).Distinct(StringComparer.Ordinal).ToList()
            : catalog.Where(set => set.Source == DataSetSource.Path).Select(set => set.Name).ToList();

        if (names.Count == 0)
        {
            throw new DataSetException("No set to seed: name the sets, or give the folders of sets.");
        }

        var sets = DataSetCatalog.Resolve(catalog, names);
        var named = names.Select(name => sets.First(set => set.Name == name)).ToList();
        var target = DataSetTarget.Resolve(named, request.Organization, request.Project, request.View);
        var owner = DataSetOwner.Resolve(request.Owner, _baseAccount);

        // the project, the view and a dotted organization now; the organization when the provisioning knows whether it
        // is new
        target.EnsureNames(isNewOrganization: false);

        // the limits of every set's files and of its annotations.jsonl, without reading the documents or expanding a
        // generator, so a set beyond them is refused before the target is provisioned and the sets before it are seeded
        foreach (var set in sets)
        {
            DataSetFolder.Validate(_fileSystem, set.Folder);
            DataSetAnnotationLinesReader.CountAnnotations(_fileSystem, set.Folder);
        }

        // the descriptions of the named sets first: a dependency such as core is not what the project is about
        var described = named.Concat(sets.Except(named)).Select(set => set.Manifest).ToList();

        return new DataSetSeedPlan(
            sets,
            named,
            target,
            owner,
            described.Select(manifest => manifest.ProjectDescription).FirstOrDefault(description => description is not null),
            described.Select(manifest => manifest.ViewDescription).FirstOrDefault(description => description is not null));
    }

    // Makes the plan's target exist, then seeds its sets in order. DataSetException for a name of the target that
    // breaks the rules or an organization the owner does not own, before anything is written.
    public async Task<DataSetRunResult> SeedAsync(DataSetSeedPlan plan, DataSetSeedOptions options, CancellationToken cancellationToken = default)
    {
        var target = plan.Target;
        var provisioning = await _provisioner.ProvisionAsync(
            target,
            plan.Owner,
            plan.ProjectDescription,
            plan.ViewDescription,
            options.DryRun,
            cancellationToken);

        var results = new List<DataSetSeedResult>(plan.Sets.Count);
        var failed = new HashSet<string>(StringComparer.Ordinal);

        foreach (var set in plan.Sets)
        {
            var failedDependency = set.Manifest.DependsOn.FirstOrDefault(failed.Contains);

            if (failedDependency is not null)
            {
                _logger.LogInformation("The set {Set} is not seeded: the set {Dependency} it depends on failed.", set.Name, failedDependency);
                results.Add(new DataSetSeedResult { Set = set, FailedDependency = failedDependency });
                failed.Add(set.Name);
                continue;
            }

            var result = await SeedSetAsync(set, target, options, cancellationToken);
            results.Add(result);

            if (result.HasFailures)
            {
                failed.Add(set.Name);
            }
        }

        return new DataSetRunResult(target, plan.Owner, provisioning, results, options.DryRun);
    }

    // One set into a target that exists: the annotation files (ancestors first), the annotations of
    // annotations.jsonl in bulk, then the schemas, templates and configurations, all with one overlay, so each step
    // sees what the earlier ones made active or failed (also in a dry run). The record is written after a seed without
    // failures; a set whose record has its version and hash is not read further, unless Again.
    public async Task<DataSetSeedResult> SeedSetAsync(DataSet set, DataSetTarget target, DataSetSeedOptions options, CancellationToken cancellationToken = default)
    {
        var started = _time.GetTimestamp();
        var hash = DataSetFolder.ComputeHash(_fileSystem, set.Folder);
        var record = await _records.GetAsync(target, set.Name, cancellationToken);

        if (!options.Again && record is not null && record.Version == set.Manifest.Version && record.Hash == hash)
        {
            _logger.LogInformation("The set {Set} is seeded in {Target} already (version {Version}).", set.Name, target, record.Version);
            return new DataSetSeedResult { Set = set, Hash = hash, IsAlreadySeeded = true, Elapsed = _time.GetElapsedTime(started) };
        }

        _logger.LogInformation("Seeding the set {Set} (version {Version}) into {Target}.", set.Name, set.Manifest.Version, target);

        var content = DataSetContent.Read(_fileSystem, set.Folder, target);
        var importOptions = new ViewImportOptions
        {
            Actor = AuditActor.System,
            Existing = ViewImportExisting.Skip,
            Force = options.Force,
            DryRun = options.DryRun,
        };
        var overlay = new ViewImportOverlay();
        var documents = new ViewImportTally(
            null,
            content.Documents.Files.Count,
            content.Documents.IgnoredFiles,
            content.Documents.ExcludedFiles);
        var items = new List<DataSetSeedItem>();

        await ImportAsync(target, content.Documents.Files.Where(IsAnnotation).ToList(), importOptions, overlay, documents, items, cancellationToken);
        var lines = await WriteLinesAsync(target, content.Lines, options, overlay, items, cancellationToken);
        await ImportAsync(target, content.Documents.Files.Where(file => !IsAnnotation(file)).ToList(), importOptions, overlay, documents, items, cancellationToken);

        var isRecorded = false;

        if (!options.DryRun && items.All(item => item.Outcome != ViewImportOutcome.Failed))
        {
            await _records.SaveAsync(CreateRecord(set, target, hash, documents, lines, _time.GetUtcNow()), cancellationToken);
            isRecorded = true;
        }

        var result = new DataSetSeedResult
        {
            Set = set,
            Hash = hash,
            Documents = documents,
            Lines = lines,
            Items = items,
            IsRecorded = isRecorded,
            Elapsed = _time.GetElapsedTime(started),
        };

        _logger.LogInformation(
            "Seeded the set {Set} into {Target} in {Elapsed}: {Failed} failed, recorded: {IsRecorded}.",
            set.Name,
            target,
            result.Elapsed,
            result.Failures.Count(),
            isRecorded);
        return result;
    }

    private static bool IsAnnotation(ViewArchiveFile file)
    {
        return file.Kind == ViewDocumentKind.Annotation;
    }

    private static DataSetSeedItem ToItem(ViewArchiveFile file, ViewImportResult result)
    {
        return new DataSetSeedItem
        {
            Path = file.Path,
            Kind = file.Kind,
            AnnotationType = file.AnnotationType,
            AnnotationKey = file.AnnotationKey?.ToString(),
            Outcome = result.Outcome,
            ErrorCode = result.ErrorCode,
            Message = result.Message,
            Warnings = result.Warnings,
        };
    }

    private static bool IsListed(ViewImportResult result)
    {
        return result.Outcome is not (ViewImportOutcome.Created or ViewImportOutcome.Unchanged);
    }

    // What the importer's overlay tracks of a failed annotation: what hangs on it is DependencyFailed. A duplicate
    // changes nothing, its first occurrence counts.
    private static bool IsFailedAnnotation(ViewImportResult result)
    {
        return result.ErrorCode != ErrorCodes.DuplicateFile
               && (result.Outcome == ViewImportOutcome.Failed || result.ErrorCode == ErrorCodes.DependencyFailed);
    }

    private static ViewImportResult DependencyFailed(AnnotationKey key, IReadOnlyList<string> failed)
    {
        var ancestors = failed.Where(failedKey => failedKey != key.ToString()).ToList();

        return new ViewImportResult(
            ViewImportOutcome.Skipped,
            ErrorCode: ErrorCodes.DependencyFailed,
            Message: ancestors.Count switch
            {
                0 => $"The annotation {key} failed in this import.",
                1 => $"The ancestor {ancestors[0]} failed in this import.",
                _ => $"The ancestors {string.Join(", ", ancestors)} failed in this import.",
            });
    }

    private static DataSetSeedRecord CreateRecord(DataSet set, DataSetTarget target, string hash, ViewImportTally documents, DataSetLinesResult? lines, DateTimeOffset seeded)
    {
        var total = documents.Summary;
        var bulk = lines?.Summary ?? new ViewImportSummary();

        return new DataSetSeedRecord
        {
            Organization = target.Organization,
            PartitionKey = DataSetSeedRecord.GetPartitionKey(target.Project),
            Set = set.Name,
            View = target.View,
            Version = set.Manifest.Version,
            Hash = hash,
            Seeded = AuditStamp.Of(AuditActor.System, seeded),
            Documents = documents.Total,
            Annotations = lines?.Annotations ?? 0,
            Created = total.Created + bulk.Created,
            Updated = total.Updated + bulk.Updated,
            Unchanged = total.Unchanged + bulk.Unchanged,
            Skipped = total.Skipped + bulk.Skipped,
            Warnings = total.Warnings + bulk.Warnings,
        };
    }

    private async Task ImportAsync(
        DataSetTarget target,
        IReadOnlyList<ViewArchiveFile> files,
        ViewImportOptions options,
        ViewImportOverlay overlay,
        ViewImportTally tally,
        List<DataSetSeedItem> items,
        CancellationToken cancellationToken)
    {
        if (files.Count == 0)
        {
            return;
        }

        await foreach (var (file, result) in _importer.ImportAsync(target.Organization, target.Project, target.View, files, options, overlay, cancellationToken))
        {
            tally.Add(file, result);

            if (IsListed(result))
            {
                items.Add(ToItem(file, result));
            }
        }
    }

    // The annotations of annotations.jsonl through the bulk writer, after the annotation files. One that could not
    // be read is Failed; one on or below an annotation that failed (in a file or a line) is DependencyFailed without
    // being sent, as the importer does. The failures join the overlay for the configurations and schemas after it, and
    // in a dry run so do the keys the writer would create.
    private async Task<DataSetLinesResult?> WriteLinesAsync(
        DataSetTarget target,
        DataSetAnnotationLines? lines,
        DataSetSeedOptions options,
        ViewImportOverlay overlay,
        List<DataSetSeedItem> items,
        CancellationToken cancellationToken)
    {
        if (lines is null)
        {
            return null;
        }

        var annotations = lines.Annotations;
        var results = new ViewImportResult?[annotations.Count];

        for (var index = 0; index < annotations.Count; index++)
        {
            var annotation = annotations[index];

            if (annotation.Annotation is null)
            {
                results[index] = new ViewImportResult(ViewImportOutcome.Failed, ErrorCode: annotation.ErrorCode, Message: annotation.Message);

                if (annotation.Key is not null && IsFailedAnnotation(results[index]!))
                {
                    overlay.AddFailedAnnotation(annotation.Key);
                }
            }
        }

        // ancestors first, so a key below a DependencyFailed one is DependencyFailed too
        var sent = new List<int>();

        foreach (var index in Enumerable.Range(0, annotations.Count).Where(index => results[index] is null).OrderBy(index => annotations[index].Key!.GetDepth()))
        {
            var key = annotations[index].Key!;
            var failed = key.GetAllAncestorKeys()
                .Append(key)
                .Select(candidate => candidate.ToString())
                .Where(overlay.FailedAnnotations.Contains)
                .ToList();

            if (failed.Count > 0)
            {
                results[index] = DependencyFailed(key, failed);
                overlay.AddFailedAnnotation(key);
                continue;
            }

            sent.Add(index);
        }

        sent.Sort();
        IReadOnlyList<string> ancestorsCreated = [];
        var relationUpdates = 0;

        if (sent.Count > 0)
        {
            var bulk = await _bulkWriter.CreateMissingAnnotationsAsync(
                target.Organization,
                target.Project,
                target.View,
                sent.Select(index => annotations[index].Annotation!).ToList(),
                AuditActor.System,
                new AnnotationBulkOptions { DryRun = options.DryRun, Progress = options.AnnotationProgress },
                cancellationToken);

            for (var position = 0; position < sent.Count; position++)
            {
                var index = sent[position];
                var result = bulk.Items[position].Result;
                results[index] = result;

                if (IsFailedAnnotation(result))
                {
                    overlay.AddFailedAnnotation(annotations[index].Key!);
                }
                else if (options.DryRun && result.Outcome == ViewImportOutcome.Created)
                {
                    overlay.AddActiveAnnotation(annotations[index].Key!);
                }
            }

            if (options.DryRun)
            {
                foreach (var ancestor in bulk.AncestorsCreated)
                {
                    overlay.AddActiveAnnotation(AnnotationKey.Parse(ancestor));
                }
            }

            ancestorsCreated = bulk.AncestorsCreated;
            relationUpdates = bulk.RelationUpdates;
        }

        // the counts only: the items of a large set would be every created annotation
        var tally = new ViewImportTally([ViewArchiveContent.Annotations], annotations.Count, maxItems: 0);

        for (var index = 0; index < annotations.Count; index++)
        {
            var annotation = annotations[index];
            var result = results[index]!;
            var file = new ViewArchiveFile
            {
                Path = DataSetAnnotationLinesReader.FileName,
                Kind = ViewDocumentKind.Annotation,
                AnnotationKey = annotation.Key,
            };

            tally.Add(file, result);

            if (IsListed(result))
            {
                items.Add(ToItem(file, result) with { Line = annotation.Line });
            }
        }

        return new DataSetLinesResult(
            lines.Lines,
            lines.GeneratorLines,
            annotations.Count,
            tally.Summaries[0],
            ancestorsCreated,
            relationUpdates);
    }
}

using System.Globalization;
using System.IO.Abstractions;
using System.Net;
using System.Text;
using _42.Testing.UI.Automation.Reporting.Testiny.Api;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace _42.Testing.UI.Automation.Reporting.Testiny;

// Reports a run as a classic Testiny test run (Reporting:Testiny:Mode TestRun) whose test cases are the [TestCase] ids
// of the tests; links of another system (TestCaseLink.System) are ignored.
// - StartAsync creates the run (the run's name as its title, the description under the markdown header), adds it to
//   Reporting:Testiny:TestPlanId, checks the linked ids with POST /testcase/find when the case validation is on
//   (unknown ids become warnings and are left out) and plans every linked case as NOTRUN, 200 per request. The plan and
//   the check are optional: Testiny refusing either is a warning, and the run goes on outside the plan or unchecked.
// - ReportAsync sets the result of each linked case, worst wins over every test that links it (FAILED > BLOCKED >
//   SKIPPED > PASSED; per test only its last attempt counts), uploads the artifacts per Reporting:Testiny:Attachments
//   and Reporting:Testiny:MaxAttachmentBytes to each linked case and writes one markdown comment per test and linked
//   case per Reporting:Testiny:Comments, the comments of a batch in one request and their mappings in another.
// - CompleteAsync writes the totals and the warnings into the run's description and closes it, with a fresh _etag and
//   one more try after an update conflict.
// - DisposeAsync closes a run that was created but never completed (its start failed after the creation, so the
//   dispatcher sends it nothing more, or its completion failed), best effort, with why in its description.
// The calls are serialized, so concurrent batches keep the state consistent, and every step remembers what Testiny has
// accepted: a call the dispatcher retries after a transient failure (429, 5xx, a timeout) repeats only what is
// missing, never a run, an upload or a comment. A failure after which Testiny may have done the work anyway (a timeout,
// a network failure, a 5xx other than 503: TestinyTransientException.MayHaveBeenApplied) is never blindly sent again:
// the retried start (and DisposeAsync) looks the run up first (POST /testrun/find by title, the one whose description
// names the descriptor's RunId) and creates it only when there is none, and an upload or a comment is given up as a
// warning; only 429, 408 and 503 are sent again. Testiny refusing one case, upload or comment (a 4xx about the data)
// becomes a warning and the rest goes on, and so does an upload that failed transiently twice; any other failure
// surfaces to the dispatcher, which logs it as a run warning and never fails a test.
public sealed class TestinyTestRunReporter : ITestRunReporter
{
    public const string SystemName = "Testiny";

    // Items per bulk request (mappings, comments).
    public const int ChunkSize = 200;

    // An upload that failed transiently this often is given up, so the comments of its batch still go out.
    public const int MaxUploadAttempts = 2;

    private const int MaxTitleLength = 200;
    private const int MaxFileNameLength = 120;
    private const int MaxExtensionLength = 16;
    private const int MaxListedIds = 20;

    // The newest runs of the title looked at for the one an uncertain creation may have made.
    private const int MaxFoundRuns = 20;

    // How long DisposeAsync tries to close a run that was never completed; less than the 10 s the dispatcher gives it.
    private static readonly TimeSpan AbandonTimeout = TimeSpan.FromSeconds(8);

    private readonly TestinyClient client;
    private readonly TestinyOptions options;
    private readonly bool validateCaseIds;
    private readonly IFileSystem fileSystem;
    private readonly ILogger logger;
    private readonly HttpClient? ownedHttpClient;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly object warningsGate = new();
    private readonly List<string> warnings = [];
    private readonly HashSet<string> invalidIds = new(StringComparer.Ordinal);
    private readonly HashSet<long> missingCases = [];
    private readonly HashSet<long> rejectedCases = [];
    private readonly Dictionary<long, Dictionary<string, CaseResult>> resultsByCase = [];
    private readonly Dictionary<long, string> sentStatus = [];
    private readonly Dictionary<string, int> handledAttempts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TestinyArtifactState> uploads = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> uploadFailures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> comments = new(StringComparer.Ordinal);
    private readonly HashSet<string> refusedComments = new(StringComparer.Ordinal);
    private readonly HashSet<string> mappedComments = new(StringComparer.Ordinal);
    private TestRunDescriptor? run;
    private long? runId;
    private string? etag;
    private string? stopReason;
    private bool validated;
    private bool planAttached;
    private bool completed;

    // The last POST /testrun failed in a way after which the run may exist anyway.
    private bool creationUncertain;
    private int attachmentCount;
    private int commentCount;
    private volatile Uri? runUrl;

    // validateCaseIds: check the linked ids once at the start (the provider turns it on when Reporting:UnmappedTests
    // is not Ignore). fileSystem reads the artifacts; a FileSystem over System.IO when null.
    public TestinyTestRunReporter(TestinyClient client, TestinyOptions options, bool validateCaseIds = false, IFileSystem? fileSystem = null, ILogger? logger = null)
        : this(client, options, validateCaseIds, fileSystem, logger, ownedHttpClient: null)
    {
    }

    internal TestinyTestRunReporter(TestinyClient client, TestinyOptions options, bool validateCaseIds, IFileSystem? fileSystem, ILogger? logger, HttpClient? ownedHttpClient)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);

        this.client = client;
        this.options = options;
        this.validateCaseIds = validateCaseIds;
        this.fileSystem = fileSystem ?? new FileSystem();
        this.logger = logger ?? NullLogger.Instance;
        this.ownedHttpClient = ownedHttpClient;
    }

    public string System => SystemName;

    public Uri? RunUrl => runUrl;

    // The id of the Testiny run, once StartAsync created it.
    public long? RunId => runId;

    // What went wrong in Testiny without failing the run (unknown ids, refused cases, uploads, comments); they end up in
    // the run's description.
    public IReadOnlyList<string> Warnings
    {
        get
        {
            lock (warningsGate)
            {
                return [.. warnings];
            }
        }
    }

    internal bool ValidatesCaseIds => validateCaseIds;

    public async Task StartAsync(TestRunDescriptor run, IReadOnlyList<TestIdentity> plannedTests, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(plannedTests);

        await gate.WaitAsync(cancellationToken);
        try
        {
            this.run ??= run;
            var id = await CreateRunAsync(this.run, cancellationToken);

            if (options.TestPlanId is { } plan && !planAttached)
            {
                await AttachToPlanAsync(id, plan, cancellationToken);
            }

            var cases = PlannedCases(plannedTests);
            if (validateCaseIds && !validated && cases.Count > 0)
            {
                await ValidateCasesAsync(cases, cancellationToken);
            }

            if (options.PlanCasesAsNotRun)
            {
                var pending = cases.Keys.Where(caseId => IsUsable(caseId) && !sentStatus.ContainsKey(caseId)).Order().ToArray();
                await MapResultsAsync(pending.Select(caseId => (caseId, TestinyResultStatus.NotRun)).ToArray(), cancellationToken);
            }

            stopReason = null;
        }
        catch (Exception exception) when (runId is not null)
        {
            // The run exists; if the dispatcher gives up on the start, DisposeAsync closes it with this reason.
            stopReason = $"its start failed: {Describe(exception)}";
            throw;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task ReportAsync(IReadOnlyList<TestResultReport> results, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(results);

        await gate.WaitAsync(cancellationToken);
        try
        {
            var id = runId ?? throw new InvalidOperationException("The Testiny run was not created: StartAsync did not succeed.");
            var latest = LatestAttempts(results);

            // The status of every case the batch touches, over all results so far: a later passing test of a case does
            // not hide an earlier failure.
            var touched = new SortedSet<long>();
            foreach (var report in latest)
            {
                foreach (var caseId in CasesOf(report.Test).Where(IsUsable))
                {
                    if (!resultsByCase.TryGetValue(caseId, out var tests))
                    {
                        resultsByCase[caseId] = tests = new Dictionary<string, CaseResult>(StringComparer.Ordinal);
                    }

                    if (!tests.TryGetValue(report.Test.Id, out var known) || report.Attempt >= known.Attempt)
                    {
                        tests[report.Test.Id] = new CaseResult(report.Attempt, report.Outcome);
                    }

                    touched.Add(caseId);
                }
            }

            var changes = touched
                .Select(caseId => (CaseId: caseId, Status: StatusOf(caseId)))
                .Where(change => !sentStatus.TryGetValue(change.CaseId, out var sent) || !string.Equals(sent, change.Status, StringComparison.Ordinal))
                .ToArray();
            await MapResultsAsync(changes, cancellationToken);

            var pendingComments = new List<PendingComment>();
            foreach (var report in latest)
            {
                var cases = CasesOf(report.Test).Where(IsUsable).ToArray();
                if (cases.Length == 0)
                {
                    continue;
                }

                handledAttempts[report.Test.Id] = report.Attempt;
                var notes = await UploadArtifactsAsync(id, report, cases, cancellationToken);

                if (Applies(options.Comments, report.Outcome))
                {
                    var text = TestinyResultComment.Build(report, run!, StatusOf(report.Outcome), notes, options.MaxAttachmentBytes);
                    pendingComments.AddRange(cases.Select(caseId => new PendingComment(ResultKey(report, caseId), caseId, TestinyResultComment.ShortName(report.Test), text)));
                }
            }

            await WriteCommentsAsync(id, pendingComments, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task CompleteAsync(TestRunSummary summary, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(summary);

        await gate.WaitAsync(cancellationToken);
        try
        {
            if (completed || runId is not { } id)
            {
                return;
            }

            var description = TestinyRunDescription.Completed(run!, summary, CasesByStatus(), attachmentCount, commentCount, [.. summary.Warnings.Concat(Warnings).Distinct(StringComparer.Ordinal)]);

            try
            {
                await UpdateRunAsync(
                    current => new TestinyTestRun { Id = id, Description = description, IsClosed = options.CloseRunOnComplete ? true : null, ETag = current },
                    fetchFirst: true,
                    cancellationToken);
            }
            catch (Exception exception)
            {
                stopReason = $"its completion failed: {Describe(exception)}";
                throw;
            }

            completed = true;
            logger.LogInformation(
                "Testiny run {RunId} {State}: {Cases} test case(s), {Attachments} attachment(s), {Comments} comment(s).",
                id,
                options.CloseRunOnComplete ? "closed" : "completed",
                sentStatus.Count,
                attachmentCount,
                commentCount);
        }
        finally
        {
            gate.Release();
        }
    }

    // Closes a run that was created but never completed, then disposes the owned HttpClient. Never throws.
    public async ValueTask DisposeAsync()
    {
        try
        {
            await CloseAbandonedRunAsync();
        }
        finally
        {
            ownedHttpClient?.Dispose();
        }
    }

    // Failures: every outcome but Passed and Skipped.
    private static bool Applies(TestinyContentPolicy policy, TestOutcome outcome)
    {
        return policy switch
        {
            TestinyContentPolicy.All => true,
            TestinyContentPolicy.Failures => outcome is not (TestOutcome.Passed or TestOutcome.Skipped),
            _ => false,
        };
    }

    // FAILED > BLOCKED > SKIPPED > PASSED.
    private static int Rank(TestOutcome outcome)
    {
        return outcome switch
        {
            TestOutcome.Passed => 0,
            TestOutcome.Skipped => 1,
            TestOutcome.Blocked => 2,
            _ => 3,
        };
    }

    private static string ResultKey(TestResultReport report, long caseId)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{report.Test.Id}#{report.Attempt}|{caseId}");
    }

    // A file name Testiny accepts on every platform: letters, digits, '.', '-' and '_', neither starting nor ending with
    // '.' or '-', at most MaxFileNameLength characters; a longer name is cut before its extension, which stays.
    private static string FileNameOf(TestArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        var name = new StringBuilder(artifact.Name.Length);
        foreach (var character in Path.GetFileName(artifact.Name))
        {
            name.Append(char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_' ? character : '-');
        }

        var result = name.ToString().Trim('.', '-');
        var extension = Path.GetExtension(result);
        if (extension.Length is < 2 or > MaxExtensionLength)
        {
            extension = string.Empty;
        }

        if (result.Length > MaxFileNameLength)
        {
            result = result[..(MaxFileNameLength - extension.Length)].TrimEnd('.', '-') + extension;
        }

        return result.Length == 0 ? "artifact" : result.Length == extension.Length ? "artifact" + extension : result;
    }

    private static string Describe(Exception exception)
    {
        return exception is OperationCanceledException
            ? "it did not finish in time"
            : $"{exception.GetType().Name}: {exception.Message}";
    }

    private static string Title(TestResultReport report, TestArtifact artifact)
    {
        var attempt = report.Attempt > 1 ? string.Create(CultureInfo.InvariantCulture, $" (attempt {report.Attempt})") : string.Empty;
        return TestinyMarkdown.Truncate($"{TestinyResultComment.ShortName(report.Test)}: {artifact.Name}{attempt}", MaxTitleLength);
    }

    private static string Title(TestRunDescriptor descriptor)
    {
        return TestinyMarkdown.Truncate(descriptor.Name, MaxTitleLength);
    }

    private string StatusOf(TestOutcome outcome)
    {
        return Rank(outcome) switch
        {
            0 => TestinyResultStatus.Passed,
            1 => options.ResultForSkipped,
            2 => options.ResultForBlocked,
            _ => TestinyResultStatus.Failed,
        };
    }

    private string StatusOf(long caseId)
    {
        return StatusOf(resultsByCase[caseId].Values.MaxBy(result => Rank(result.Outcome))!.Outcome);
    }

    // The last attempt of each test in the batch, in the batch's order, unless a later attempt was handled already.
    private IReadOnlyList<TestResultReport> LatestAttempts(IReadOnlyList<TestResultReport> results)
    {
        return results
            .GroupBy(report => report.Test.Id, StringComparer.Ordinal)
            .Select(group => group.MaxBy(report => report.Attempt)!)
            .Where(report => !handledAttempts.TryGetValue(report.Test.Id, out var handled) || handled <= report.Attempt)
            .ToArray();
    }

    private bool IsUsable(long caseId)
    {
        return !missingCases.Contains(caseId) && !rejectedCases.Contains(caseId);
    }

    // The distinct Testiny ids of a test; an id of another form is a warning, once.
    private long[] CasesOf(TestIdentity test)
    {
        var ids = new List<long>();
        foreach (var link in test.Cases)
        {
            if (!link.AppliesTo(SystemName))
            {
                continue;
            }

            if (!TestinyCaseIds.TryParse(link.Id, out var caseId))
            {
                if (invalidIds.Add(link.Id))
                {
                    AddWarning($"[TestCase(\"{link.Id}\")] of {TestinyResultComment.ShortName(test)} is not a Testiny test case id (a number such as 1234, or TC-1234); it is ignored.");
                }

                continue;
            }

            if (!ids.Contains(caseId))
            {
                ids.Add(caseId);
            }
        }

        return [.. ids];
    }

    private Dictionary<long, List<TestIdentity>> PlannedCases(IReadOnlyList<TestIdentity> plannedTests)
    {
        var cases = new Dictionary<long, List<TestIdentity>>();
        foreach (var test in plannedTests)
        {
            foreach (var caseId in CasesOf(test))
            {
                if (!cases.TryGetValue(caseId, out var tests))
                {
                    cases[caseId] = tests = [];
                }

                tests.Add(test);
            }
        }

        return cases;
    }

    private async Task<long> CreateRunAsync(TestRunDescriptor descriptor, CancellationToken cancellationToken)
    {
        if (runId is { } existing)
        {
            return existing;
        }

        // The previous POST /testrun may have created the run before its answer was lost, and a second run would stay
        // open forever. A failed lookup surfaces: transiently, so the dispatcher tries again, or for good, so no run is
        // made at all.
        if (creationUncertain && await FindCreatedRunAsync(descriptor, cancellationToken) is { } found)
        {
            var adopted = Adopt(found);
            logger.LogInformation("Testiny run {RunId} was created by an earlier try whose answer was lost; it is used: {Url}", adopted, runUrl?.ToString() ?? "(no RunUrlTemplate)");
            return adopted;
        }

        TestinyTestRun created;
        try
        {
            created = await client.CreateTestRunAsync(
                new TestinyTestRun
                {
                    Title = Title(descriptor),
                    ProjectId = options.ProjectId,
                    Description = TestinyRunDescription.Started(descriptor),
                },
                cancellationToken);
        }
        catch (TestinyTransientException exception) when (exception.MayHaveBeenApplied)
        {
            creationUncertain = true;
            stopReason = $"its start failed: {Describe(exception)}";
            throw;
        }

        var id = Adopt(created);
        logger.LogInformation("Testiny run {RunId} '{Title}' created in project {ProjectId}: {Url}", id, created.Title ?? descriptor.Name, options.ProjectId, runUrl?.ToString() ?? "(no RunUrlTemplate)");
        return id;
    }

    // The run of the project with the descriptor's title whose description names its RunId (TestinyRunDescription
    // writes it as code), or null.
    private async Task<TestinyTestRun?> FindCreatedRunAsync(TestRunDescriptor descriptor, CancellationToken cancellationToken)
    {
        var marker = TestinyMarkdown.Code(descriptor.RunId);
        var runs = await client.FindTestRunsAsync(options.ProjectId, Title(descriptor), MaxFoundRuns, cancellationToken);
        return runs.FirstOrDefault(candidate => candidate.Id is > 0 && candidate.Description?.Contains(marker, StringComparison.Ordinal) == true);
    }

    private long Adopt(TestinyTestRun created)
    {
        var id = created.Id is > 0 ? created.Id.Value : throw new InvalidDataException("Testiny answered POST /testrun without the id of the run.");
        runId = id;
        etag = created.ETag;
        runUrl = options.FormatRunUrl(id);
        creationUncertain = false;
        return id;
    }

    // Optional: Testiny refusing the plan (an unknown id, one of another project, no access) is a warning, and the run
    // goes on outside the plan; a transient failure surfaces, so the dispatcher tries the start again.
    private async Task AttachToPlanAsync(long id, long plan, CancellationToken cancellationToken)
    {
        try
        {
            await UpdateRunAsync(current => new TestinyTestRun { Id = id, TestPlanId = plan, ETag = current }, fetchFirst: false, cancellationToken);
        }
        catch (TestinyApiException exception)
        {
            AddWarning(string.Create(CultureInfo.InvariantCulture, $"Testiny did not add run {id} to test plan {plan} (Reporting:Testiny:TestPlanId); the run is outside the plan: {exception.Message}"));
        }

        planAttached = true;
    }

    // Unknown ids (not a test case of the project, or deleted) are left out of the run, as a warning. Optional: Testiny
    // refusing the check is a warning, and the ids are used unchecked.
    private async Task ValidateCasesAsync(Dictionary<long, List<TestIdentity>> cases, CancellationToken cancellationToken)
    {
        IReadOnlySet<long> found;
        try
        {
            found = await client.FindTestCasesAsync(options.ProjectId, cases.Keys, cancellationToken);
        }
        catch (TestinyApiException exception)
        {
            AddWarning($"Testiny did not check the [TestCase] ids, so they are used unchecked: {exception.Message}");
            validated = true;
            return;
        }

        validated = true;

        var missing = cases.Where(pair => !found.Contains(pair.Key)).OrderBy(pair => pair.Key).ToArray();
        if (missing.Length == 0)
        {
            return;
        }

        missingCases.UnionWith(missing.Select(pair => pair.Key));
        var listed = string.Join(", ", missing.Take(MaxListedIds).Select(pair =>
            string.Create(CultureInfo.InvariantCulture, $"{pair.Key} ({string.Join(", ", pair.Value.Select(TestinyResultComment.ShortName).Distinct(StringComparer.Ordinal))})")));
        var more = missing.Length > MaxListedIds ? string.Create(CultureInfo.InvariantCulture, $" and {missing.Length - MaxListedIds} more") : string.Empty;
        AddWarning(string.Create(CultureInfo.InvariantCulture, $"{missing.Length} [TestCase] id(s) are no test cases of project {options.ProjectId}, and their results are not reported: {listed}{more}."));
    }

    // add_or_update in chunks; what Testiny accepted is remembered at once, so a retry sends only the rest. A chunk that
    // Testiny refuses for its data is sent again case by case, so one bad id costs only its own result.
    private async Task MapResultsAsync(IReadOnlyList<(long CaseId, string Status)> items, CancellationToken cancellationToken)
    {
        var id = runId!.Value;
        foreach (var chunk in items.Chunk(ChunkSize))
        {
            try
            {
                await client.MapTestCasesAsync(chunk.Select(item => Mapping(id, item)).ToArray(), TestinyMappingOperation.AddOrUpdate, cancellationToken);
                Accept(chunk);
            }
            catch (TestinyApiException exception) when (exception.IsDataError)
            {
                if (chunk.Length == 1)
                {
                    Reject(chunk[0].CaseId, exception);
                    continue;
                }

                foreach (var item in chunk)
                {
                    try
                    {
                        await client.MapTestCasesAsync([Mapping(id, item)], TestinyMappingOperation.AddOrUpdate, cancellationToken);
                        Accept([item]);
                    }
                    catch (TestinyApiException single) when (single.IsDataError)
                    {
                        Reject(item.CaseId, single);
                    }
                }
            }
        }

        static TestinyTestCaseRunMapping Mapping(long runId, (long CaseId, string Status) item)
        {
            return new TestinyTestCaseRunMapping(new TestinyTestCaseRunIds(item.CaseId, runId), new TestinyResultValues(item.Status));
        }

        void Accept(IEnumerable<(long CaseId, string Status)> accepted)
        {
            foreach (var (caseId, status) in accepted)
            {
                sentStatus[caseId] = status;
            }
        }
    }

    private void Reject(long caseId, TestinyApiException exception)
    {
        if (rejectedCases.Add(caseId))
        {
            AddWarning(string.Create(CultureInfo.InvariantCulture, $"Testiny refused test case {caseId} in run {runId}, and its results are not reported: {exception.Message}"));
        }
    }

    // The notes the comment lists; each file goes to each case once, whatever the dispatcher retries.
    private async Task<IReadOnlyList<TestinyArtifactNote>> UploadArtifactsAsync(long id, TestResultReport report, IReadOnlyList<long> cases, CancellationToken cancellationToken)
    {
        var attach = Applies(options.Attachments, report.Outcome);
        var notes = new List<TestinyArtifactNote>(report.Artifacts.Count);

        foreach (var artifact in report.Artifacts)
        {
            if (!attach || (artifact.Kind == ArtifactKind.Trace && !options.AttachTraces))
            {
                notes.Add(new TestinyArtifactNote(artifact, TestinyArtifactState.NotAttached, artifact.SizeBytes));
                continue;
            }

            var file = fileSystem.FileInfo.New(artifact.Path);
            if (!file.Exists)
            {
                notes.Add(new TestinyArtifactNote(artifact, TestinyArtifactState.Missing, artifact.SizeBytes));
                continue;
            }

            if (file.Length > options.MaxAttachmentBytes)
            {
                notes.Add(new TestinyArtifactNote(artifact, TestinyArtifactState.TooLarge, file.Length));
                continue;
            }

            var state = TestinyArtifactState.Attached;
            foreach (var caseId in cases)
            {
                var key = $"{ResultKey(report, caseId)}|{artifact.Path}";
                if (uploads.TryGetValue(key, out var done))
                {
                    state = done == TestinyArtifactState.UploadFailed ? done : state;
                    continue;
                }

                try
                {
                    await using var content = fileSystem.File.OpenRead(artifact.Path);
                    await client.UploadResultAttachmentAsync(id, caseId, content, Title(report, artifact), FileNameOf(artifact), artifact.MimeType, cancellationToken);
                    uploads[key] = TestinyArtifactState.Attached;
                    attachmentCount++;
                }
                catch (Exception exception) when (exception is TestinyApiException { IsDataError: true } or TestinyApiException { StatusCode: HttpStatusCode.RequestEntityTooLarge } or IOException or UnauthorizedAccessException
                    || (exception is TestinyTransientException transient && (transient.MayHaveBeenApplied || GivesUp(key))))
                {
                    // A file that may have been stored anyway is not sent again, so it is never there twice.
                    uploads[key] = TestinyArtifactState.UploadFailed;
                    state = TestinyArtifactState.UploadFailed;
                    var attempts = exception switch
                    {
                        TestinyTransientException { MayHaveBeenApplied: true } => " (it may have reached Testiny, so it is not sent again)",
                        TestinyTransientException => string.Create(CultureInfo.InvariantCulture, $" {MaxUploadAttempts} times"),
                        _ => string.Empty,
                    };
                    AddWarning(string.Create(CultureInfo.InvariantCulture, $"Uploading {artifact.Name} of {TestinyResultComment.ShortName(report.Test)} to test case {caseId} failed{attempts}: {exception.Message}"));
                }
            }

            notes.Add(new TestinyArtifactNote(artifact, state, file.Length));
        }

        return notes;
    }

    // The comments not created yet, in chunks (Testiny answers a bulk create in the order of the request), then the
    // mappings of the created ones not made yet. A chunk that Testiny refuses for its data is sent again comment by
    // comment, so one bad comment costs only itself.
    private async Task WriteCommentsAsync(long id, IReadOnlyList<PendingComment> pending, CancellationToken cancellationToken)
    {
        foreach (var chunk in pending.Where(comment => !comments.ContainsKey(comment.Key) && !refusedComments.Contains(comment.Key)).Chunk(ChunkSize))
        {
            try
            {
                await CreateCommentsAsync(chunk, cancellationToken);
            }
            catch (TestinyTransientException exception) when (exception.MayHaveBeenApplied)
            {
                GiveUpComments(chunk, exception);
            }
            catch (TestinyApiException exception) when (exception.IsDataError)
            {
                if (chunk.Length == 1)
                {
                    RefuseComment(chunk[0], exception);
                    continue;
                }

                foreach (var comment in chunk)
                {
                    try
                    {
                        await CreateCommentsAsync([comment], cancellationToken);
                    }
                    catch (TestinyApiException single) when (single.IsDataError)
                    {
                        RefuseComment(comment, single);
                    }
                    catch (TestinyTransientException single) when (single.MayHaveBeenApplied)
                    {
                        GiveUpComments([comment], single);
                    }
                }
            }
        }

        foreach (var chunk in pending.Where(comment => comments.ContainsKey(comment.Key) && !mappedComments.Contains(comment.Key)).Chunk(ChunkSize))
        {
            try
            {
                await client.MapCommentsToResultsAsync(
                    chunk.Select(comment => new TestinyCommentRunMapping(new TestinyCommentRunIds(comments[comment.Key], comment.CaseId, id))).ToArray(),
                    cancellationToken);
            }
            catch (TestinyApiException exception) when (exception.IsDataError)
            {
                AddWarning(string.Create(CultureInfo.InvariantCulture, $"Testiny refused {chunk.Length} comment(s) on the results of test case(s) {string.Join(", ", chunk.Select(comment => comment.CaseId).Distinct())}: {exception.Message}"));
            }

            mappedComments.UnionWith(chunk.Select(comment => comment.Key));
        }
    }

    // POST /comment/bulk; the ids are remembered at once, so a retry creates only the rest.
    private async Task CreateCommentsAsync(IReadOnlyList<PendingComment> chunk, CancellationToken cancellationToken)
    {
        var created = await client.CreateCommentsAsync(
            chunk.Select(comment => new TestinyComment { ProjectId = options.ProjectId, Type = TestinyComment.TextType, Text = comment.Text }).ToArray(),
            cancellationToken);

        if (created.Count != chunk.Count || created.Any(comment => comment.Id is not > 0))
        {
            throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"Testiny answered POST /comment/bulk of {chunk.Count} comment(s) with {created.Count} comment(s) with ids."));
        }

        for (var index = 0; index < chunk.Count; index++)
        {
            comments[chunk[index].Key] = created[index].Id!.Value;
        }

        commentCount += chunk.Count;
    }

    // POST /comment/bulk failed in a way after which the comments may exist anyway: they are not created again (they
    // would be there twice) and, without their ids, not mapped either.
    private void GiveUpComments(IReadOnlyList<PendingComment> chunk, TestinyTransientException exception)
    {
        refusedComments.UnionWith(chunk.Select(comment => comment.Key));
        AddWarning(string.Create(CultureInfo.InvariantCulture, $"Creating {chunk.Count} comment(s) on the results of test case(s) {string.Join(", ", chunk.Select(comment => comment.CaseId).Distinct())} failed, and they may have reached Testiny unmapped, so they are not sent again: {exception.Message}"));
    }

    private void RefuseComment(PendingComment comment, TestinyApiException exception)
    {
        refusedComments.Add(comment.Key);
        AddWarning(string.Create(CultureInfo.InvariantCulture, $"Testiny refused the comment of {comment.Test} on the result of test case {comment.CaseId}: {exception.Message}"));
    }

    // Counts a transient failure of an upload; true when it is the last one allowed.
    private bool GivesUp(string key)
    {
        var failures = uploadFailures.GetValueOrDefault(key) + 1;
        uploadFailures[key] = failures;
        return failures >= MaxUploadAttempts;
    }

    // DisposeAsync: a run that was created but not completed is closed (or, without CloseRunOnComplete, only described)
    // with why, within AbandonTimeout; a failure is logged.
    private async Task CloseAbandonedRunAsync()
    {
        using var timeout = new CancellationTokenSource(AbandonTimeout);
        try
        {
            await gate.WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Testiny run {RunId} may be left open: a call to Testiny was still running at the end.", runId);
            return;
        }

        try
        {
            // A creation whose answer was lost and that was never tried again: the run may exist, and is closed then.
            if (runId is null && creationUncertain && run is not null && await FindCreatedRunAsync(run, timeout.Token) is { } found)
            {
                Adopt(found);
            }

            if (completed || runId is not { } id || run is null)
            {
                return;
            }

            var reason = stopReason ?? "it was not completed";
            var description = TestinyRunDescription.Stopped(run, reason, CasesByStatus(), Warnings);
            await UpdateRunAsync(
                current => new TestinyTestRun { Id = id, Description = description, IsClosed = options.CloseRunOnComplete ? true : null, ETag = current },
                fetchFirst: true,
                timeout.Token);

            completed = true;
            logger.LogWarning("Testiny run {RunId} was {State} without the rest of its results: {Reason}.", id, options.CloseRunOnComplete ? "closed" : "described", reason);
        }
        catch (Exception exception)
        {
            logger.LogWarning("Testiny run {RunId} is left open: closing it failed: {Error}", runId, Describe(exception));
        }
        finally
        {
            gate.Release();
        }
    }

    private Dictionary<string, int> CasesByStatus()
    {
        return sentStatus.Values
            .GroupBy(status => status, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
    }

    // PUT /testrun/{id} with the known _etag (or a fresh one when fetchFirst); an update conflict fetches it once more
    // and tries again.
    private async Task UpdateRunAsync(Func<string?, TestinyTestRun> update, bool fetchFirst, CancellationToken cancellationToken)
    {
        var id = runId!.Value;
        if (fetchFirst || etag is null)
        {
            etag = (await client.GetTestRunAsync(id, cancellationToken)).ETag;
        }

        TestinyTestRun updated;
        try
        {
            updated = await client.UpdateTestRunAsync(update(etag), cancellationToken);
        }
        catch (TestinyApiException exception) when (exception.IsUpdateConflict)
        {
            logger.LogInformation("Testiny run {RunId} was changed elsewhere; updating it with a fresh _etag: {Message}", id, exception.Message);
            etag = (await client.GetTestRunAsync(id, cancellationToken)).ETag;
            updated = await client.UpdateTestRunAsync(update(etag), cancellationToken);
        }

        etag = updated.ETag ?? etag;
    }

    private void AddWarning(string warning)
    {
        lock (warningsGate)
        {
            warnings.Add(warning);
        }

        logger.LogWarning("{Warning}", warning);
    }

    private sealed record CaseResult(int Attempt, TestOutcome Outcome);

    private sealed record PendingComment(string Key, long CaseId, string Test, string Text);
}

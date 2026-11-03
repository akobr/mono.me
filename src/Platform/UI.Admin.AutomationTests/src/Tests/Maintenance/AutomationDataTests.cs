using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Platform.UI.Admin.AutomationTests.Support;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;
using _42.Testing.UI.Automation.Runtime.Users;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.Maintenance;

// Housekeeping of the target's view, not a case of the catalogue (no [TestCase], so no reporter gets its result): the
// automation data of earlier runs, which the tests themselves never delete. Skipped unless a run asks for it with
// Maintenance:Purge=true, and always on the mock ([RequiresPurge]). [Serial]: it runs after the tests without a parallel
// constraint and never beside another serial test, so it does not compete with most tests for the API or hold an
// Administrator of the pool while they run. It is not ordered against a test with a key of its own ([NotInParallel],
// e.g. ModelingExportImportTests), which may run beside it: the age of the data (OlderThanHours, never the current run),
// not the order, keeps it off the data of a running test. Not covered: the
// disposable projects and organizations (the API cannot delete a project), key values (at_<runId>…) and the views of
// Target:RoleProjects.
public sealed partial class AutomationDataTests : PlatformTest
{
    // Trees deleted or purged at a time.
    private const int Parallelism = 4;

    // What the purge did, a line per root, for the summary.
    private readonly ConcurrentQueue<string> deleted = new();
    private readonly ConcurrentQueue<string> purged = new();
    private readonly ConcurrentQueue<string> skipped = new();
    private readonly ConcurrentQueue<string> left = new();
    private readonly List<string> notes = [];
    private readonly Stopwatch clock = new();

    // An Owner or an Administrator of Target:Project, whatever Target:RoleProjects maps: a purge needs an Administrator.
    protected override UserRequirement RequiredUser => AdminUser;

    private MaintenanceSettings Maintenance => Settings.Bind<MaintenanceSettings>(MaintenanceSettings.Section);

    // No request starts after Maintenance:BudgetSeconds.
    private bool InBudget => clock.Elapsed < TimeSpan.FromSeconds(Maintenance.BudgetSeconds);

    // PurgeAutomationData: deletes the roots that earlier runs created in the target's view and purges them.
    // Through the API with the signed-in user's own token (StorytellerApi), not through the UI: hundreds of trees
    // through the grid would be slow and brittle.
    //  1. lists the roots subjects and responsibilities named ^at- (active or not), following the continuation tokens;
    //  2. keeps those named like generated data (at-<yyyyMMdd>-<HHmmss>-<xxxx>-…) of another run and created
    //     (Created.At, the server's UTC) more than Maintenance:OlderThanHours ago;
    //  3. deletes them with their descendants and configurations, four at a time, the subjects before the
    //     responsibilities: their descendants overlap (a usage, an execution, a unit of execution), so the two never go
    //     at once; 404 is done;
    //  4. purges the deleted roots of the view named so and deleted (Deletion.DeletedAt) more than OlderThanHours ago or
    //     by step 3; 404 is done, 409 is skipped (restored meanwhile);
    //  5. starts no request after Maintenance:BudgetSeconds (the next run continues) and writes what it deleted, purged
    //     and left to the test's output.
    // It fails when the API refuses it (a second 401, a 403, a 5xx, a list answer other than 2xx), when a list gets no
    // answer within Target:ApiTimeoutMs, when the admin UI gives no Authorization header (at the start or after a 401)
    // and on a network error; what is left is not a failure, nor is a delete or a purge without an answer within
    // Target:ApiWriteTimeoutMs (it may still finish on the server).
    [Test]
    [Category(AutomationCategories.Maintenance)]
    [Serial]
    [RequiresPurge]
    public async Task PurgeAutomationData()
    {
        clock.Start();
        var routes = AppRoutes.For(Target);
        var cutoff = DateTimeOffset.UtcNow.AddHours(-Maintenance.OlderThanHours);
        var api = new StorytellerApi(App, routes, Context.APIRequest);
        await api.ConnectAsync();
        try
        {
            var subjects = await ListAsync(api, RootsPath(api, "subjects"));
            var responsibilities = await ListAsync(api, RootsPath(api, "responsibilities"));
            notes.Add($"Listed {subjects.Count + responsibilities.Count} root(s) named ^at-.");

            var deletedNow = new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);
            foreach (var roots in new[] { subjects, responsibilities })
            {
                var old = roots.Where(root => IsOfEarlierRun(root) && root.CreatedAt < cutoff).Select(root => root.Key);
                await ForEachAsync(old, async key =>
                {
                    switch (await DeleteAsync(api, api.ViewPath($"annotations/{Uri.EscapeDataString(key)}"), key))
                    {
                        case null:
                            break;
                        case { IsSuccess: true }:
                            deletedNow[key] = true;
                            deleted.Enqueue(key);
                            break;
                        case { Status: (int)HttpStatusCode.NotFound }:
                            deleted.Enqueue($"{key} (gone already)");
                            break;
                        case var response:
                            skipped.Enqueue($"{key}: not deleted, {response.Status} {response.Message()}");
                            break;
                    }
                });
            }

            var deletedRoots = await ListAsync(api, AppRoutes.WithQuery(api.ViewPath("annotations/deleted"), ("roots", "true")));
            var purgeable = deletedRoots
                .Where(root => IsOfEarlierRun(root) && (root.DeletedAt < cutoff || deletedNow.ContainsKey(root.Key)))
                .Select(root => root.Key);
            await ForEachAsync(purgeable, async key =>
            {
                switch (await DeleteAsync(api, api.ViewPath($"annotations/deleted/{Uri.EscapeDataString(key)}"), key))
                {
                    case null:
                        break;
                    case { IsSuccess: true } or { Status: (int)HttpStatusCode.NotFound }:
                        purged.Enqueue(key);
                        break;
                    case var response:
                        // 409: restored meanwhile (or a descendant was).
                        skipped.Enqueue($"{key}: not purged, {response.Status} {response.Message()}");
                        break;
                }
            });
        }
        finally
        {
            TestContext.Current?.Output.WriteLine(Summary(routes, api));
        }
    }

    // The list of a root type, active or not, named ^at- (a regular expression of the API).
    private static string RootsPath(StorytellerApi api, string list)
    {
        return AppRoutes.WithQuery(api.ViewPath(list), ("nameQuery", "^at-"), ("includeInactive", "true"));
    }

    private static void AppendKeys(StringBuilder text, string what, IReadOnlyCollection<string> keys)
    {
        text.AppendLine(CultureInfo.InvariantCulture, $"{what}: {keys.Count}");
        foreach (var key in keys.Order(StringComparer.Ordinal))
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"  {key}");
        }
    }

    // at-<yyyyMMdd>-<HHmmss>-<4 of a-z0-9>-…: the start of every generated name part (PlatformData.Slug).
    [GeneratedRegex(@"^at-\d{8}-\d{6}-[a-z0-9]{4}-", RegexOptions.CultureInvariant)]
    private static partial Regex GeneratedName();

    // A root named like the generated data of a run (PlatformData) other than this one.
    private bool IsOfEarlierRun(Root root)
    {
        return GeneratedName().IsMatch(root.Name) && !root.Name.StartsWith($"{PlatformData.RunSlug}-", StringComparison.Ordinal);
    }

    // Every page of a list while the budget lasts; an answer other than 2xx is a refusal.
    private async Task<List<Root>> ListAsync(StorytellerApi api, string path)
    {
        var roots = new List<Root>();
        string? token = null;
        do
        {
            if (!InBudget)
            {
                notes.Add($"The budget ran out while listing {path}: what is not listed is left for the next run.");
                break;
            }

            var response = await api.SendAsync("GET", AppRoutes.WithQuery(path, ("continuationToken", token)));
            if (!response.IsSuccess)
            {
                throw new HttpRequestException($"The API did not list {api.BaseUrl}/v1/{path}: {response.Status} {response.Message()}", null, (HttpStatusCode)response.Status);
            }

            var page = response.Json();
            roots.AddRange(page?["Annotations"]?.AsArray().Select(Root.From) ?? []);
            token = page?["ContinuationToken"]?.GetValue<string>();
        }
        while (!string.IsNullOrEmpty(token));

        return roots;
    }

    // A delete or a purge; one that does not answer within Target:ApiWriteTimeoutMs may still finish on the server, so
    // its key is left for the next run (null), not a failure. Only the request's own timeout: a renewal of the header
    // that fails throws an InvalidOperationException, which fails the test.
    private async Task<StorytellerApiResponse?> DeleteAsync(StorytellerApi api, string path, string key)
    {
        try
        {
            return await api.SendAsync("DELETE", path);
        }
        catch (TimeoutException)
        {
            left.Enqueue($"{key}: no answer within {App.ApiWriteTimeoutMs} ms");
            return null;
        }
    }

    // Runs the action for each key, Parallelism at a time; a key that the budget leaves no time for is left.
    private async Task ForEachAsync(IEnumerable<string> keys, Func<string, Task> action)
    {
        await Parallel.ForEachAsync(keys, new ParallelOptions { MaxDegreeOfParallelism = Parallelism }, async (key, _) =>
        {
            if (InBudget)
            {
                await action(key);
            }
            else
            {
                left.Enqueue(key);
            }
        });
    }

    private string Summary(AppRoutes routes, StorytellerApi api)
    {
        var maintenance = Maintenance;
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"PurgeAutomationData of {routes.Organization}/{routes.Project}/{routes.View} through {api.BaseUrl}:");
        text.AppendLine(CultureInfo.InvariantCulture, $"the roots of runs other than {PlatformData.RunSlug} created or deleted more than {maintenance.OlderThanHours} h ago, within {maintenance.BudgetSeconds} s (took {clock.Elapsed.TotalSeconds:F0} s).");
        foreach (var note in notes)
        {
            text.AppendLine(note);
        }

        AppendKeys(text, "Deleted, with their descendants", deleted);
        AppendKeys(text, "Purged", purged);
        AppendKeys(text, "Skipped", skipped);
        AppendKeys(text, "Left for the next run (the budget ran out, or no answer in time)", left);
        return text.ToString();
    }

    // A root of a list: its key, its name (the last part of the key) and when it was created and deleted (the server's
    // UTC; DeletedAt only in the list of the deleted).
    private sealed record Root(string Key, string Name, DateTimeOffset? CreatedAt, DateTimeOffset? DeletedAt)
    {
        public static Root From(JsonNode? annotation)
        {
            return new Root(
                annotation?["AnnotationKey"]?.GetValue<string>() ?? string.Empty,
                annotation?[nameof(Name)]?.GetValue<string>() ?? string.Empty,
                annotation?["Created"]?["At"]?.GetValue<DateTimeOffset>(),
                annotation?["Deletion"]?[nameof(DeletedAt)]?.GetValue<DateTimeOffset>());
        }
    }
}

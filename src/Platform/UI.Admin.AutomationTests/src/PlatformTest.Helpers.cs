using System.Text.Json;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Platform.UI.Admin.AutomationTests.Support;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests;

// The helpers of the tests: a disposable project for project-wide changes, the recorders of what the page copies and
// fetches (init scripts: install them before the first navigation), downloads, the mock API and a second page.
public abstract partial class PlatformTest
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private MockApi? mockApi;

    // The API of the mock build, in the view of Routes (another view: new MockApi(App, routes)); only on a mock target
    // (Target.IsMock), so a test that uses it is [MockOnly], or uses OpenSecondPageAsync on a deployed target.
    protected MockApi MockApi => Target.IsMock
        ? mockApi ??= new MockApi(App, Routes)
        : throw new InvalidOperationException(
            "MockApi works on the admin UI's mock build only (Target:MockScenario set or Target:SignIn None); mark the test [MockOnly(…)], or write through a second page (OpenSecondPageAsync) on a deployed target.");

    // A new project in Target:Organization for a test that changes project-wide state (mode P, [DisposableProject]):
    // New project on the organization overview with a generated key (PlatformData.ProjectKey()), as an Owner of the
    // organization ([UserRole(Role.Owner)]). Returns once the toast "Project <key> created" showed and the project's
    // annotations opened (the app's own landing), with the project's routes in its default view. The project stays in
    // the organization; the report's properties name it.
    protected async Task<AppRoutes> CreateDisposableProjectAsync(string? description = null)
    {
        if (!User.HasRole(OwnerRole))
        {
            throw new InvalidOperationException(
                $"A disposable project needs an Owner of Target:Organization '{Target.Organization}', and {User} is none; mark the test [UserRole(Role.Owner)].");
        }

        var organization = AppRoutes.For(Target);
        var project = PlatformData.ProjectKey();
        var created = organization with { Project = project, View = AppRoutes.DefaultView };

        var overview = new OrganizationOverview(App, organization);
        await overview.OpenAsync();
        await overview.CreateProjectAsync(project, description);
        await Expect(App.Toast($"Project {project} created")).ToBeVisibleAsync();
        await App.ExpectAtAsync(created.Annotations);
        await new AnnotationsPage(App, created).WaitForCatalogAsync();

        Scope.Properties["disposableProject"] = $"{created.Organization}/{project}";
        return created;
    }

    // Records what the page copies (navigator.clipboard.writeText and write, document.execCommand('copy')) instead of
    // writing the system clipboard, on every engine and provider; read it with CopiedTextAsync or
    // WaitForCopiedTextAsync. Install it before the first navigation (an init script of the context, also in a second
    // page); in a page that loaded already it applies at once too.
    protected async Task InstallClipboardRecorderAsync()
    {
        await Context.AddInitScriptAsync(PageScripts.ClipboardRecorder);
        if (HasLoaded(Page))
        {
            await Page.EvaluateAsync(PageScripts.ClipboardRecorder);
        }
    }

    // The text the page copied last, null when it copied nothing (or the recorder is not installed).
    protected async Task<string?> CopiedTextAsync()
    {
        return await Page.EvaluateAsync<string?>("() => (window.__copied ?? []).at(-1) ?? null");
    }

    // Waits until the text the page copied last is the expected one (up to Execution:ActionTimeoutMs), e.g. after a
    // click on a Copy button.
    protected async Task WaitForCopiedTextAsync(string expected)
    {
        ArgumentNullException.ThrowIfNull(expected);

        try
        {
            await Page.WaitForFunctionAsync("expected => (window.__copied ?? []).at(-1) === expected", expected, new() { PollingInterval = 100 });
        }
        catch (TimeoutException exception)
        {
            var copied = await Page.EvaluateAsync<string[]?>("() => window.__copied ?? null");
            var what = copied is null ? "the clipboard recorder is not installed (InstallClipboardRecorderAsync before the first navigation)"
                : copied.Length == 0 ? "the page copied nothing"
                : $"the page copied {string.Join(", ", copied.Select(text => $"'{text}'"))}";
            throw new TimeoutException($"The page did not copy '{expected}': {what}.", exception);
        }
    }

    // Records every fetch of the page (method, URL, body) for a check that a request was or was not sent; read with
    // RecordedRequestsAsync. Install it before the first navigation, like the clipboard recorder.
    protected async Task InstallRequestRecorderAsync()
    {
        await Context.AddInitScriptAsync(PageScripts.RequestRecorder);
        if (HasLoaded(Page))
        {
            await Page.EvaluateAsync(PageScripts.RequestRecorder);
        }
    }

    // The fetches of the page since its last load, oldest first (the record starts again with each page load).
    protected async Task<IReadOnlyList<RecordedRequest>> RecordedRequestsAsync()
    {
        var json = await Page.EvaluateAsync<string>("() => JSON.stringify(window.__requests ?? [])");
        return JsonSerializer.Deserialize<RecordedRequest[]>(json, WebJson) ?? [];
    }

    // Waits until the page sent a request whose path ends with pathEnd and whose query parameter matches the pattern (a
    // JavaScript regular expression), among the fetches the request recorder (InstallRequestRecorderAsync) holds since
    // the last page load, up to Execution:ActionTimeoutMs; the error tells what the page sent instead.
    protected async Task WaitForRequestAsync(string pathEnd, string parameter, string pattern)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pathEnd);
        ArgumentException.ThrowIfNullOrWhiteSpace(parameter);
        ArgumentNullException.ThrowIfNull(pattern);

        try
        {
            await Page.WaitForFunctionAsync(
                """
                ([pathEnd, parameter, pattern]) => (window.__requests ?? []).some((request) => {
                  const url = new URL(request.url);
                  return url.pathname.endsWith(pathEnd) && new RegExp(pattern).test(url.searchParams.get(parameter) ?? '');
                })
                """,
                new[] { pathEnd, parameter, pattern },
                new() { PollingInterval = 100 });
        }
        catch (TimeoutException exception)
        {
            var installed = await Page.EvaluateAsync<bool>("() => Array.isArray(window.__requests)");
            IReadOnlyList<RecordedRequest> requests = installed ? await RecordedRequestsAsync() : [];
            var what = !installed ? "the request recorder is not installed (InstallRequestRecorderAsync before the first navigation)"
                : requests.Count == 0 ? "the page sent nothing"
                : $"the page sent {string.Join(", ", requests.Select(request => $"{request.Method} {request.Url}"))}";
            throw new TimeoutException($"The page did not request …{pathEnd} with {parameter} matching '{pattern}': {what}.", exception);
        }
    }

    // Runs the action (e.g. the click on a download button) and returns the file it downloads, read over the browser's
    // connection (never Download.PathAsync, which a remote browser does not have), within timeoutMs. The file is not
    // kept as an artifact.
    protected Task<DownloadedFile> DownloadAsync(Func<Task> action, int timeoutMs = 60_000)
    {
        return DownloadAsync(action, _ => Task.CompletedTask, timeoutMs);
    }

    // Like DownloadAsync(action, timeoutMs), but runs started with the suggested file name as soon as the download
    // begins, before its bytes are read: a toast that confirms the download lasts about 5 seconds, and reading a large
    // file over a remote browser's connection can take longer.
    protected async Task<DownloadedFile> DownloadAsync(Func<Task> action, Func<string, Task> started, int timeoutMs = 60_000)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(started);
        ArgumentOutOfRangeException.ThrowIfLessThan(timeoutMs, 1);

        var download = await Page.RunAndWaitForDownloadAsync(action, new() { Timeout = timeoutMs });
        await started(download.SuggestedFilename);
        await using var stream = await download.CreateReadStreamAsync()
            ?? throw new InvalidOperationException($"The download of {download.SuggestedFilename} has no content: {await download.FailureAsync()}");
        using var content = new MemoryStream();
        await stream.CopyToAsync(content);
        return new DownloadedFile(download.SuggestedFilename, content.ToArray());
    }

    // A second page in the test's context (the same sign-in, traced with it, closed at the end) with its own App, whose
    // first navigation carries the mock scenario: "another writer" of a deployed target that saves through the UI. On
    // the mock its tab has data of its own (MSW keeps it in sessionStorage), so a concurrent edit there uses MockApi.
    protected async Task<App> OpenSecondPageAsync()
    {
        var page = await Context.NewPageAsync();
        return new App(page, Target.MockScenario);
    }

    private static bool HasLoaded(IPage page)
    {
        return Uri.TryCreate(page.Url, UriKind.Absolute, out var url) && url.Scheme is "http" or "https";
    }
}

using System.Text.Json;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using Microsoft.Playwright;

namespace _42.Platform.UI.Admin.AutomationTests.Support;

// The API of the admin UI's mock build, from inside the page (PlatformTest.MockApi; only on a mock target): MSW serves
// it in the page's service worker and keeps its data in the tab's sessionStorage, so only the test's own page reaches
// the data the test sees, and a second page has data of its own. "Another writer" of a concurrent-edit case uses it:
// FetchAsync sends a request as the app would (as Ada Lovelace), ChangeConfigurationAsync saves a configuration as
// Grace Hopper through the mock's window.storytellerMock hook. The UI does not know of the change until it reads again
// (a reload, a refetch, a save that conflicts). Sections add calls in MockApi.<Section>.cs.
public sealed partial class MockApi(App app, AppRoutes routes)
{
    // The API base of the mock build: .env.e2e sets VITE_API_BASE_URL=/api and its app-config.json is empty, so the API
    // is on the app's origin; MSW answers <origin>/api/v1/….
    public const string ApiBase = "/api/";

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    // A path of the API below the view of the routes, for FetchAsync: ViewPath("annotations/simple") is
    // "v1/<organization>/<project>/<view>/annotations/simple".
    public string ViewPath(string rest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rest);
        return $"v1/{routes.Organization}/{routes.Project}/{routes.View}/{rest.TrimStart('/')}";
    }

    // Sends a request to the mock API with the page's fetch and returns the answer. path: below the API base, e.g.
    // "v1/access/account" or "v1/acme/billing/default/annotations/simple"; body: serialized as JSON when not a string.
    // Waits until the service worker controls the page, so the page must show the app (any App.GotoAsync) first.
    public async Task<MockApiResponse> FetchAsync(string method, string path, object? body = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        await WaitForMockAsync("!!navigator.serviceWorker?.controller", "its service worker (MSW)");
        var json = body switch
        {
            null => null,
            string text => text,
            _ => JsonSerializer.Serialize(body),
        };

        var answer = await app.Page.EvaluateAsync<string>(
            """
            async ([method, path, body, base]) => {
              const response = await fetch(new URL(path, new URL(base, location.origin)).href, {
                method,
                headers: body === null ? undefined : { 'Content-Type': 'application/json' },
                body: body ?? undefined,
              });
              return JSON.stringify({ status: response.status, body: await response.text() });
            }
            """,
            new object?[] { method.ToUpperInvariant(), path.TrimStart('/'), json, ApiBase });
        return JsonSerializer.Deserialize<MockApiResponse>(answer, WebJson)
            ?? throw new InvalidOperationException($"The mock API gave no answer to {method} {path}.");
    }

    // Saves a new version of the configuration of key in the view of the routes, as Grace Hopper (the mock's concurrent
    // edit): the latest content with the changes merged over its top-level properties. changes: an object serialized as
    // JSON, e.g. new Dictionary<string, object> { ["retries"] = 9 }.
    public async Task ChangeConfigurationAsync(string key, object changes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(changes);

        await WaitForMockAsync("!!window.storytellerMock", "window.storytellerMock");
        var argument = JsonSerializer.Serialize(new
        {
            key,
            changes,
            scope = new { org = routes.Organization, project = routes.Project, view = routes.View },
        });
        await app.Page.EvaluateAsync(
            "argument => { const { key, changes, scope } = JSON.parse(argument); window.storytellerMock.changeConfiguration(key, changes, scope); }",
            argument);
    }

    private async Task WaitForMockAsync(string ready, string what)
    {
        if (!Uri.TryCreate(app.Page.Url, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException($"The mock API needs a page of the app; open one with App.GotoAsync first (the page is at {app.Page.Url}).");
        }

        try
        {
            await app.Page.WaitForFunctionAsync($"() => {ready}", null, new PageWaitForFunctionOptions { PollingInterval = 100 });
        }
        catch (TimeoutException exception)
        {
            throw new InvalidOperationException($"The page at {app.Page.Url} has no {what}: the target is not the admin UI's mock build.", exception);
        }
    }
}

using System.Net;
using System.Threading.Channels;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using Microsoft.Playwright;

namespace _42.Platform.UI.Admin.AutomationTests.Support;

// The Storyteller API of a deployed target, called as the signed-in user of the test's page, for housekeeping that would
// be slow and brittle through the UI (Tests/Maintenance). ConnectAsync opens /orgs and takes the API's base and the
// user's Authorization header from the admin UI's first request to the API that has one: not a preflight (OPTIONS), a
// URL with /v1/ other than the anonymous /v1/auth/configuration, the header read with HeaderValueAsync (the provisional
// headers may lack it). The requests go through the browser context's APIRequest, outside the page (no CORS), with the
// admin UI's split: Target:ApiTimeoutMs for a GET, Target:ApiWriteTimeoutMs for any other method. A 401 opens /orgs
// again and takes the header once more, once; a second 401, a 403 and a 5xx throw an HttpRequestException with the
// response. A request without an answer in time throws a TimeoutException, and only such a request: a capture that
// finds no header (also the one of a renewal) throws an InvalidOperationException, so a caller that leaves a slow
// request for later does not take a failed sign-in for one. Not on the mock: MSW answers only inside the page
// (MockApi).
public sealed class StorytellerApi(App app, AppRoutes routes, IAPIRequestContext requests)
{
    private const string VersionPath = "/v1/";

    private const string AuthConfigurationPath = "/v1/auth/configuration";

    private readonly SemaphoreSlim renewal = new(1, 1);
    private Credentials? credentials;
    private bool renewed;

    // The API's base as the admin UI calls it (before /v1/), e.g. https://api.example.com/api; after ConnectAsync.
    public string BaseUrl => Current.BaseUrl;

    private Credentials Current => credentials ?? throw new InvalidOperationException("Connect first: StorytellerApi.ConnectAsync takes the API's base and the user's header from the admin UI.");

    // A path below v1 in the view of the routes, for SendAsync: ViewPath("subjects") is
    // "<organization>/<project>/<view>/subjects"; AppRoutes.WithQuery adds an escaped query.
    public string ViewPath(string rest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rest);
        return $"{routes.Organization}/{routes.Project}/{routes.View}/{rest.TrimStart('/')}";
    }

    public async Task ConnectAsync()
    {
        credentials = await CaptureAsync();
    }

    // Sends a request to <BaseUrl>/v1/<path> as the user and returns the answer; 404, 409 and the other answers the
    // caller handles come back, a refusal of the API throws, and so does a request without an answer in time
    // (TimeoutException).
    public async Task<StorytellerApiResponse> SendAsync(string method, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var used = Current;
        var response = await FetchAsync(method, path, used);
        if (response.Status == (int)HttpStatusCode.Unauthorized)
        {
            response = await FetchAsync(method, path, await RenewAsync(used, method, path, response));
        }

        if (response.Status is (int)HttpStatusCode.Unauthorized or (int)HttpStatusCode.Forbidden or >= 500)
        {
            throw Refused(method, path, response);
        }

        return response;
    }

    // Playwright's message up to its call log ("Call log:" and the lines of the request, its headers among them).
    private static string WithoutCallLog(string message)
    {
        var log = message.IndexOf("Call log:", StringComparison.Ordinal);
        return (log >= 0 ? message[..log] : message).Trim();
    }

    // The response is disposed once its body is read: the driver keeps every body until then (hundreds of deletes in a
    // purge). Playwright's message of a timeout or a network error ends with the call log, which lists the request's
    // headers, the Authorization header included: the exception is thrown again without it and without the original.
    private async Task<StorytellerApiResponse> FetchAsync(string method, string path, Credentials with)
    {
        var url = $"{with.BaseUrl}{VersionPath}{path.TrimStart('/')}";
        var timeout = method == "GET" ? app.ApiTimeoutMs : app.ApiWriteTimeoutMs;
        IAPIResponse fetched;
        try
        {
            fetched = await requests.FetchAsync(url, new()
            {
                Method = method,
                Headers = new Dictionary<string, string> { ["Authorization"] = with.Authorization, ["Accept"] = "application/json" },
                Timeout = timeout,
            });
        }
        catch (TimeoutException)
        {
            throw new TimeoutException($"{method} {url}: no answer within {timeout} ms.");
        }
        catch (PlaywrightException exception)
        {
            throw new PlaywrightException($"{method} {url} failed: {WithoutCallLog(exception.Message)}");
        }

        await using var response = fetched;
        return new StorytellerApiResponse(response.Status, await response.TextAsync());
    }

    // A fresh header after a 401 of the header rejected: the one another request took meanwhile, else /orgs once more.
    // A second renewal is a refusal: the user's sign-in does not give the API a header it accepts.
    private async Task<Credentials> RenewAsync(Credentials rejected, string method, string path, StorytellerApiResponse response)
    {
        await renewal.WaitAsync();
        try
        {
            if (!ReferenceEquals(credentials, rejected))
            {
                return Current;
            }

            if (renewed)
            {
                throw Refused(method, path, response);
            }

            renewed = true;
            credentials = await CaptureAsync();
            return credentials;
        }
        finally
        {
            renewal.Release();
        }
    }

    // Opens /orgs and reads the header of the admin UI's first API request that has one, within Target:ApiTimeoutMs. A
    // failure is an InvalidOperationException, not a TimeoutException (the class's comment).
    private async Task<Credentials> CaptureAsync()
    {
        var candidates = Channel.CreateUnbounded<IRequest>();
        EventHandler<IRequest> collect = (_, request) =>
        {
            if (request.Method != "OPTIONS"
                && request.Url.Contains(VersionPath, StringComparison.Ordinal)
                && !request.Url.Contains(AuthConfigurationPath, StringComparison.Ordinal))
            {
                candidates.Writer.TryWrite(request);
            }
        };

        app.Page.Request += collect;
        try
        {
            await app.GotoAsync(routes.Organizations);
            using var timeout = new CancellationTokenSource(app.ApiTimeoutMs);
            while (true)
            {
                var request = await candidates.Reader.ReadAsync(timeout.Token);
                if (await request.HeaderValueAsync("authorization") is { Length: > 0 } authorization)
                {
                    var url = request.Url;
                    return new Credentials(url[..url.IndexOf(VersionPath, StringComparison.Ordinal)], authorization);
                }
            }
        }
        catch (OperationCanceledException exception)
        {
            throw new InvalidOperationException($"The admin UI sent no API request with an Authorization header within {app.ApiTimeoutMs} ms of opening /{routes.Organizations}; the page is at {app.Page.Url}.", exception);
        }
        catch (TimeoutException exception)
        {
            throw new InvalidOperationException($"The admin UI did not open /{routes.Organizations}, so it gave no Authorization header: {exception.Message}", exception);
        }
        finally
        {
            app.Page.Request -= collect;
        }
    }

    // The status, the method and the URL and the API's message; never the header.
    private HttpRequestException Refused(string method, string path, StorytellerApiResponse response)
    {
        return new HttpRequestException(
            $"The API refused {method} {BaseUrl}{VersionPath}{path.TrimStart('/')}: {response.Status} {response.Message()}",
            null,
            (HttpStatusCode)response.Status);
    }

    // The API's base and the user's Authorization header ("Bearer <token>").
    private sealed record Credentials(string BaseUrl, string Authorization);
}

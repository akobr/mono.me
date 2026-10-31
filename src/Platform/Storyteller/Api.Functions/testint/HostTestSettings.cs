using System.Net.Http.Headers;

namespace _42.Platform.Storyteller.Api.Functions.IntegrationTests;

// A running Functions host of the API and a project to call. Every value comes from an environment variable; without
// them the tests do nothing.
// - STORYTELLER_TEST_API_BASE_URL: the API base URL with /api, for example http://localhost:7071/api.
// - STORYTELLER_TEST_TOKEN: a bearer token of a user who is a Contributor of the project.
// - STORYTELLER_TEST_ORGANIZATION and STORYTELLER_TEST_PROJECT: the project.
// - STORYTELLER_TEST_VIEW (optional): the view, "default" when not set.
internal sealed record HostTestSettings(string BaseUrl, string Token, string Organization, string Project, string View)
{
    public const string Variables =
        "STORYTELLER_TEST_API_BASE_URL, STORYTELLER_TEST_TOKEN, STORYTELLER_TEST_ORGANIZATION and STORYTELLER_TEST_PROJECT";

    // The path of the project under the base URL, without a leading slash.
    public string ProjectPath => $"v1/{Organization}/{Project}";

    // The path of the view under the base URL, without a leading slash.
    public string ViewPath => $"{ProjectPath}/{View}";

    // Null when the environment is not configured.
    public static HostTestSettings? TryRead()
    {
        var baseUrl = Read("STORYTELLER_TEST_API_BASE_URL");
        var token = Read("STORYTELLER_TEST_TOKEN");
        var organization = Read("STORYTELLER_TEST_ORGANIZATION");
        var project = Read("STORYTELLER_TEST_PROJECT");

        if (baseUrl is null || token is null || organization is null || project is null)
        {
            return null;
        }

        return new HostTestSettings(baseUrl.TrimEnd('/'), token, organization, project, Read("STORYTELLER_TEST_VIEW") ?? "default");
    }

    public HttpClient CreateClient()
    {
        var client = new HttpClient { BaseAddress = new Uri($"{BaseUrl}/") };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        return client;
    }

    private static string? Read(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

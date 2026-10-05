namespace _42.Platform.Storyteller.Access.AuthKit.IntegrationTests;

// A WorkOS staging environment. Every value comes from an environment variable; without them the tests do nothing.
internal sealed record WorkOsTestSettings(
    string ApiKey,
    string ClientId,
    string AuthKitDomain,
    string OrganizationId,
    string Permission)
{
    public const string Variables = "WORKOS_TEST_API_KEY, WORKOS_TEST_CLIENT_ID, WORKOS_TEST_AUTHKIT_DOMAIN, WORKOS_TEST_ORGANIZATION_ID, WORKOS_TEST_PERMISSION";

    public static WorkOsTestSettings? TryRead()
    {
        var apiKey = Read("WORKOS_TEST_API_KEY");
        var clientId = Read("WORKOS_TEST_CLIENT_ID");
        var domain = Read("WORKOS_TEST_AUTHKIT_DOMAIN");
        var organization = Read("WORKOS_TEST_ORGANIZATION_ID");
        var permission = Read("WORKOS_TEST_PERMISSION");

        return apiKey is null || clientId is null || domain is null || organization is null || permission is null
            ? null
            : new WorkOsTestSettings(apiKey, clientId, domain.TrimEnd('/'), organization, permission);
    }

    private static string? Read(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

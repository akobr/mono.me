using _42.Platform.Storyteller.Mcp.Hosting;

namespace _42.Platform.Storyteller.Mcp.IntegrationTests;

// A Storyteller API with the user token exchange and the MCP server as an actor. Every value comes from an environment
// variable; without them the tests do nothing.
// - MCP_TEST_STORYTELLER_BASE_URL: the API base URL with /api. With a client assertion it must equal the API's
//   MachineAuth:Federation:PublicBaseUrl, since the assertion's aud is {base URL}/v1/access/token, unless
//   MCP_TEST_ACTOR_ASSERTION_AUDIENCE names the aud (Mcp:Actor:ClientAssertionKey:Audience).
// - MCP_TEST_ACTOR_ID: the actor's Id in the API's Auth:TokenExchange:Actors.
// - Exactly one credential of the actor: MCP_TEST_ACTOR_CLIENT_SECRET (an API in Development only),
//   MCP_TEST_ACTOR_TOKEN_FILE (a workload identity token file), or MCP_TEST_ACTOR_CERTIFICATE_PATH with
//   MCP_TEST_ACTOR_CERTIFICATE_PASSWORD (a PKCS#12 client assertion key whose JWKS the actor names).
// - MCP_TEST_USER_TOKEN: a token of the person for the MCP server, as an MCP client gets it (its aud one of the actor's
//   SubjectAudiences).
// - MCP_TEST_EXPECT_NO_ACCOUNT (optional, true or false): the person has no Storyteller account, so the exchange must
//   refuse the sign-in instead of finding the account.
internal sealed record ExchangeTestSettings(string BaseUrl, McpActorOptions Actor, string UserToken, bool ExpectNoAccount)
{
    public const string Variables =
        "MCP_TEST_STORYTELLER_BASE_URL, MCP_TEST_ACTOR_ID, one of MCP_TEST_ACTOR_CLIENT_SECRET, MCP_TEST_ACTOR_TOKEN_FILE or "
        + "MCP_TEST_ACTOR_CERTIFICATE_PATH, and MCP_TEST_USER_TOKEN";

    public const string ExpectNoAccountVariable = "MCP_TEST_EXPECT_NO_ACCOUNT";

    // Null when the environment is not configured; an exception when it is configured wrongly.
    public static ExchangeTestSettings? TryRead()
    {
        var baseUrl = Read("MCP_TEST_STORYTELLER_BASE_URL");
        var actorId = Read("MCP_TEST_ACTOR_ID");
        var userToken = Read("MCP_TEST_USER_TOKEN");
        var secret = Read("MCP_TEST_ACTOR_CLIENT_SECRET");
        var tokenFile = Read("MCP_TEST_ACTOR_TOKEN_FILE");
        var certificate = Read("MCP_TEST_ACTOR_CERTIFICATE_PATH");
        var credentials = new[] { secret, tokenFile, certificate }.Count(value => value is not null);

        if (baseUrl is null || actorId is null || userToken is null || credentials == 0)
        {
            return null;
        }

        if (credentials > 1)
        {
            throw new InvalidOperationException("Set one actor credential: MCP_TEST_ACTOR_CLIENT_SECRET, MCP_TEST_ACTOR_TOKEN_FILE or MCP_TEST_ACTOR_CERTIFICATE_PATH.");
        }

        var actor = new McpActorOptions { Id = actorId };

        if (secret is not null)
        {
            actor.ClientSecret = secret;
        }
        else if (tokenFile is not null)
        {
            actor.WorkloadIdentity = new McpWorkloadIdentityOptions { Kind = McpWorkloadIdentityKind.TokenFile, TokenFilePath = tokenFile };
        }
        else
        {
            actor.ClientAssertionKey = new McpClientAssertionKeyOptions
            {
                CertificatePath = certificate,
                CertificatePassword = Read("MCP_TEST_ACTOR_CERTIFICATE_PASSWORD"),
                Audience = Read("MCP_TEST_ACTOR_ASSERTION_AUDIENCE"),
            };
        }

        return new ExchangeTestSettings(baseUrl.TrimEnd('/'), actor, userToken, ReadFlag(ExpectNoAccountVariable));
    }

    private static bool ReadFlag(string name)
    {
        return Read(name) switch
        {
            null => false,
            { } value when bool.TryParse(value, out var flag) => flag,
            _ => throw new InvalidOperationException($"{name} must be true or false."),
        };
    }

    private static string? Read(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

using _42.Platform.Storyteller.AspireHosting;

// The namespace of Aspire's own resources.
namespace Aspire.Hosting.ApplicationModel;

// A Storyteller API that runs outside the AppHost, or a project of the AppHost (the local platform). Not a process: it
// starts Running, and holds what WithReference passes on: the endpoint, the view of short keys and one machine
// credential. No connection string: nothing reads ConnectionStrings__storyteller, and with one Aspire's own
// WithReference(resource, "name") would bind silently and pass no STORYTELLER_*; without one, that call does not compile.
public sealed class StorytellerResource : Resource
{
    internal StorytellerResource(string name, ReferenceExpression endpoint)
        : base(name)
    {
        Endpoint = endpoint;
    }

    public ReferenceExpression Endpoint { get; }

    // The view of short keys (WithStorytellerKey, WithExecution), set by WithView.
    public string? Organization { get; private set; }

    public string? Project { get; private set; }

    public string? View { get; private set; }

    // Secrets are parameters only, so they never sit in the AppHost's code; Aspire resolves them at start and the
    // dashboard masks them.
    internal ParameterResource? ApiKey { get; private set; }

    internal ParameterResource? ClientId { get; private set; }

    internal ParameterResource? ClientSecret { get; private set; }

    internal string? TokenUrl { get; private set; }

    internal string? TokenScope { get; private set; }

    // Absolute, against the AppHost directory, so every process finds the same file whatever its working directory.
    internal string? CertificatePath { get; private set; }

    internal ParameterResource? CertificatePassword { get; private set; }

    internal void SetView(string organization, string project, string view)
    {
        Organization = organization;
        Project = project;
        View = view;
    }

    // An API key alone, or with a client certificate (the SDK's CertificateAndApiKey).
    internal void AddApiKey(ParameterResource apiKey)
    {
        RequireCredential(ApiKey is null && ClientId is null, "an API key");
        ApiKey = apiKey;
    }

    internal void AddClientCredentials(ParameterResource clientId, ParameterResource clientSecret, string? tokenUrl, string? tokenScope)
    {
        RequireCredential(ApiKey is null && ClientId is null && CertificatePath is null, "client credentials");
        ClientId = clientId;
        ClientSecret = clientSecret;
        TokenUrl = tokenUrl;
        TokenScope = tokenScope;
    }

    // A client certificate alone, or with an API key.
    internal void AddClientCertificate(string path, ParameterResource? password)
    {
        RequireCredential(ClientId is null && CertificatePath is null, "a client certificate");
        CertificatePath = path;
        CertificatePassword = password;
    }

    // The variables of WithReference: the endpoint and those of the credential that is set. Parameters and expressions
    // stay what they are, so Aspire resolves them at start and a manifest holds their references.
    internal void WriteVariables(IDictionary<string, object> variables)
    {
        variables[StorytellerVariableNames.ENDPOINT] = Endpoint;
        Write(variables, StorytellerVariableNames.API_KEY, ApiKey);
        Write(variables, StorytellerVariableNames.CLIENT_ID, ClientId);
        Write(variables, StorytellerVariableNames.CLIENT_SECRET, ClientSecret);
        Write(variables, StorytellerVariableNames.TOKEN_URL, TokenUrl);
        Write(variables, StorytellerVariableNames.TOKEN_SCOPE, TokenScope);
        Write(variables, StorytellerVariableNames.CLIENT_CERTIFICATE, CertificatePath);
        Write(variables, StorytellerVariableNames.CLIENT_CERTIFICATE_PASSWORD, CertificatePassword);
    }

    private static void Write(IDictionary<string, object> variables, string name, object? value)
    {
        if (value is not null)
        {
            variables[name] = value;
        }
    }

    // One machine per resource, as the SDK reads one credential from the variables; a combination the SDK refuses fails
    // here, at the line that added it.
    private void RequireCredential(bool isAllowed, string credential)
    {
        if (isAllowed)
        {
            return;
        }

        throw new InvalidOperationException(
            $"The Storyteller resource '{Name}' already has {DescribeCredential()}, so it cannot take {credential}: a resource has an API key, client credentials or a client certificate, or an API key with a client certificate. Add another Storyteller resource for another machine.");
    }

    private string DescribeCredential()
    {
        return (ApiKey, ClientId, CertificatePath) switch
        {
            (not null, _, not null) => "an API key and a client certificate",
            (not null, _, _) => "an API key",
            (_, not null, _) => "client credentials",
            _ => "a client certificate",
        };
    }
}

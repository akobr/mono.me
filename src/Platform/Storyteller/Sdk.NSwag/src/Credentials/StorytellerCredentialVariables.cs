using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;

namespace _42.Platform.Storyteller.Sdk.Credentials;

// The credential the STORYTELLER_* variables describe, read and checked once; what FromEnvironment and sform share. It
// exposes the kind and the names that are set, never a secret: the secrets stay internal for CreateCredential.
// ENDPOINT, API_URL and KEY are not read here; an unbound credential reads them when it needs them.
public sealed class StorytellerCredentialVariables
{
    private const string ApiKeyScheme = "ApiKey";

    private readonly Func<string, string?> _getVariable;

    private StorytellerCredentialVariables(Func<string, string?> getVariable, IReadOnlyList<string> names)
    {
        _getVariable = getVariable;
        Names = names.ToList().AsReadOnly();
        DeprecatedNames = names.Where(StorytellerVariables.Deprecated.ContainsKey).ToList().AsReadOnly();
    }

    public StorytellerCredentialKind Kind { get; private init; }

    // Federated only.
    public StorytellerSubjectTokenSource? SubjectTokenSource { get; private init; }

    // The credential variables that are set, as named (a deprecated name by its own name); never the GitHub Actions ones.
    public IReadOnlyList<string> Names { get; }

    // The deprecated names among Names.
    public IReadOnlyList<string> DeprecatedNames { get; }

    // The machine segment of a structured API key, 2s.{segment}.{secret}: base64url of organization:project:machineAccessId,
    // for the machine identity. Null for a key in another format. The key is a secret, so it stays internal.
    public string? ApiKeyMachine { get; private init; }

    // Of the ClientCredentials and Federated kinds.
    public string? ClientId { get; private init; }

    // The bearer token file of the TokenFile kind.
    public string? TokenFile { get; private init; }

    // As set, trimmed: the credential removes the scheme itself.
    internal string? ApiKeyAsSet { get; private init; }

    internal string? CertificateFile { get; private init; }

    internal string CertificateSource { get; private init; } = StorytellerVariables.CLIENT_CERTIFICATE;

    internal string? CertificatePassword { get; private init; }

    internal string? ClientSecret { get; private init; }

    internal Uri? TokenUrl { get; private init; }

    internal string? TokenScope { get; private init; }

    // The platform token file of the File source, and the variable it came from.
    internal string? SubjectTokenPath { get; private init; }

    internal string SubjectTokenPathSource { get; private init; } = StorytellerVariables.TOKEN_FILE;

    // The URN.
    internal string SubjectTokenType { get; private init; } = FederatedTokenOptions.JwtTokenType;

    internal Uri? GitHubRequestUrl { get; private init; }

    internal string? GitHubRequestToken { get; private init; }

    // The process environment when getVariable is null. Null when no credential variable is set (the GitHub Actions
    // variables do not count); StorytellerAuthenticationException (Configuration) when the variables do not describe
    // one credential. Messages name the variables as they were set, never a value.
    public static StorytellerCredentialVariables? Read(Func<string, string?>? getVariable = null)
    {
        getVariable ??= Environment.GetEnvironmentVariable;

        var variables = new StorytellerVariableReader(getVariable);
        var apiKey = variables.Read(StorytellerVariables.API_KEY);
        var certificateFile = variables.ReadWithFallback(StorytellerVariables.CLIENT_CERTIFICATE, StorytellerVariables.CERTIFICATE_FILE, out var certificateSource);
        var certificatePassword = variables.ReadWithFallback(StorytellerVariables.CLIENT_CERTIFICATE_PASSWORD, StorytellerVariables.CERTIFICATE_PASSWORD, out var passwordSource);
        var clientId = variables.Read(StorytellerVariables.CLIENT_ID);
        var clientSecret = variables.Read(StorytellerVariables.CLIENT_SECRET);
        var tokenUrl = variables.Read(StorytellerVariables.TOKEN_URL);
        var tokenScope = variables.Read(StorytellerVariables.TOKEN_SCOPE);
        var tokenFile = variables.Read(StorytellerVariables.TOKEN_FILE);
        var subjectTokenFile = variables.Read(StorytellerVariables.SUBJECT_TOKEN_FILE);
        var subjectTokenType = variables.Read(StorytellerVariables.SUBJECT_TOKEN_TYPE);

        // Not among the names: they never make a credential on their own.
        var gitHubVariables = new StorytellerVariableReader(getVariable);
        var gitHub = new GitHubRequest(
            gitHubVariables.Read(StorytellerVariables.ACTIONS_ID_TOKEN_REQUEST_URL),
            gitHubVariables.Read(StorytellerVariables.ACTIONS_ID_TOKEN_REQUEST_TOKEN));

        // The scheme alone, as in "ApiKey ", is no key.
        if (apiKey is not null && string.Equals(apiKey, ApiKeyScheme, StringComparison.OrdinalIgnoreCase))
        {
            throw StorytellerAuthenticationException.Configuration($"{StorytellerVariables.API_KEY} holds no key.");
        }

        var keyOrCertificate = apiKey is not null || certificateFile is not null || certificatePassword is not null;
        var clientCredentials = clientSecret is not null || tokenUrl is not null;

        // With a client id and no client-credentials variable, the token file holds the platform token of an exchange;
        // alone it is a bearer token. The deprecated subject token file is always the platform token of an exchange.
        var tokenFileIsSubject = tokenFile is not null && clientId is not null && !clientCredentials;
        var federation = tokenFileIsSubject || subjectTokenFile is not null || subjectTokenType is not null;
        var bearerTokenFile = tokenFile is not null && !tokenFileIsSubject;

        // CLIENT_ID and TOKEN_SCOPE belong to client credentials or federation; alone they are one more kind, except a
        // machine id in a GitHub Actions job. Both GitHub variables are in every job with id-token: write, so they
        // federate only an id that is a GUID, as every federated machine id is and AuthKit and Keycloak client ids are
        // not: a client-credentials job with a missing secret must not turn into an exchange.
        var sharedOnly = (clientId is not null || tokenScope is not null) && !clientCredentials && !federation;

        if (sharedOnly && gitHub.IsSet && Guid.TryParse(clientId, out _))
        {
            sharedOnly = false;
            federation = true;
        }

        var kinds = new[] { keyOrCertificate, clientCredentials, federation, bearerTokenFile, sharedOnly }.Count(isSet => isSet);

        if (kinds == 0)
        {
            return null;
        }

        if (kinds > 1)
        {
            throw StorytellerAuthenticationException.Configuration(
                $"Storyteller credential variables of different kinds are set: {string.Join(", ", variables.SetNames)}. Set the variables of one kind only.");
        }

        var names = variables.SetNames;

        if (bearerTokenFile)
        {
            return new StorytellerCredentialVariables(getVariable, names) { Kind = StorytellerCredentialKind.TokenFile, TokenFile = tokenFile };
        }

        if (federation)
        {
            return ReadFederated(getVariable, names, clientId, tokenFileIsSubject ? tokenFile : null, subjectTokenFile, subjectTokenType, tokenScope, gitHub);
        }

        if (sharedOnly)
        {
            throw StorytellerAuthenticationException.Configuration(clientId is not null
                ? $"{StorytellerVariables.CLIENT_ID} needs {StorytellerVariables.CLIENT_SECRET} for client credentials, or a platform token for a federated machine: {StorytellerVariables.TOKEN_FILE}, or a GitHub Actions job with permissions: id-token: write."
                : $"{StorytellerVariables.TOKEN_SCOPE} needs {StorytellerVariables.CLIENT_ID}, with {StorytellerVariables.CLIENT_SECRET} or {StorytellerVariables.TOKEN_FILE}.");
        }

        if (clientCredentials)
        {
            return ReadClientCredentials(getVariable, names, clientId, clientSecret, tokenUrl, tokenScope);
        }

        if (certificateFile is null && certificatePassword is not null)
        {
            throw StorytellerAuthenticationException.Configuration($"{passwordSource} is set without {StorytellerVariables.CLIENT_CERTIFICATE}.");
        }

        return new StorytellerCredentialVariables(getVariable, names)
        {
            Kind = certificateFile is null
                ? StorytellerCredentialKind.ApiKey
                : apiKey is null ? StorytellerCredentialKind.Certificate : StorytellerCredentialKind.CertificateAndApiKey,
            ApiKeyMachine = apiKey is null ? null : ReadMachine(ApiKeyCredential.Normalize(apiKey)),
            ApiKeyAsSet = apiKey,
            CertificateFile = certificateFile,
            CertificateSource = certificateSource,
            CertificatePassword = certificatePassword,
        };
    }

    // Unbound, as FromEnvironment returns it; a new credential on every call. It loads the certificate, and keeps the
    // getVariable of Read for its unbound fallbacks (STORYTELLER_ENDPOINT, STORYTELLER_KEY).
    public StorytellerCredential CreateCredential(IFileSystem? fileSystem = null, TimeProvider? timeProvider = null)
    {
        return EnvironmentCredential.Create(this, _getVariable, fileSystem ?? new FileSystem(), timeProvider);
    }

    public override string ToString()
    {
        return $"{nameof(StorytellerCredentialVariables)} {{ Kind = {Kind}, Names = {string.Join(", ", Names)} }}";
    }

    private static StorytellerCredentialVariables ReadClientCredentials(
        Func<string, string?> getVariable,
        IReadOnlyList<string> names,
        string? clientId,
        string? clientSecret,
        string? tokenUrl,
        string? tokenScope)
    {
        var missing = new (string? Value, string Name)[] { (clientId, StorytellerVariables.CLIENT_ID), (clientSecret, StorytellerVariables.CLIENT_SECRET) }
            .Where(variable => variable.Value is null)
            .Select(variable => variable.Name)
            .ToList();

        if (missing.Count > 0)
        {
            throw StorytellerAuthenticationException.Configuration(
                $"Client credentials need {StorytellerVariables.CLIENT_ID} and {StorytellerVariables.CLIENT_SECRET}; {string.Join(" and ", missing)} {(missing.Count == 1 ? "is" : "are")} missing.");
        }

        var url = tokenUrl is null ? null : StorytellerUrlRule.Require(tokenUrl, StorytellerVariables.TOKEN_URL);
        ClientCredentialsCredential.RequireTokenUrl(clientId!, url, StorytellerVariables.TOKEN_URL);

        return new StorytellerCredentialVariables(getVariable, names)
        {
            Kind = StorytellerCredentialKind.ClientCredentials,
            ClientId = clientId,
            ClientSecret = clientSecret,
            TokenUrl = url,
            TokenScope = tokenScope,
        };
    }

    private static StorytellerCredentialVariables ReadFederated(
        Func<string, string?> getVariable,
        IReadOnlyList<string> names,
        string? clientId,
        string? tokenFile,
        string? subjectTokenFile,
        string? subjectTokenType,
        string? tokenScope,
        GitHubRequest gitHub)
    {
        if (clientId is null)
        {
            throw StorytellerAuthenticationException.Configuration($"A federated machine needs {StorytellerVariables.CLIENT_ID}, the machine id.");
        }

        if (tokenFile is not null && subjectTokenFile is not null && !string.Equals(tokenFile, subjectTokenFile, StringComparison.Ordinal))
        {
            throw StorytellerAuthenticationException.Configuration(
                $"{StorytellerVariables.TOKEN_FILE} and {StorytellerVariables.SUBJECT_TOKEN_FILE} are both set, to different values; set one of them.");
        }

        var path = tokenFile ?? subjectTokenFile;

        if (path is null && !gitHub.IsSet)
        {
            throw StorytellerAuthenticationException.Configuration(
                $"A federated machine needs a platform token: set {StorytellerVariables.TOKEN_FILE} to the file with it, or run in a GitHub Actions job with permissions: id-token: write.");
        }

        // The request token goes to this URL; it has a query, so it is no endpoint.
        var gitHubRequestUrl = path is null ? StorytellerUrlRule.Require(gitHub.Url!, StorytellerVariables.ACTIONS_ID_TOKEN_REQUEST_URL) : null;

        var normalizedType = FederatedTokenCredential.NormalizeSubjectTokenType(subjectTokenType)
            ?? throw StorytellerAuthenticationException.Configuration(
                $"{StorytellerVariables.SUBJECT_TOKEN_TYPE} must be jwt, id_token or access_token, or the full urn:ietf:params:oauth:token-type: URN.");

        return new StorytellerCredentialVariables(getVariable, names)
        {
            Kind = StorytellerCredentialKind.Federated,
            SubjectTokenSource = path is null ? StorytellerSubjectTokenSource.GitHubActions : StorytellerSubjectTokenSource.File,
            ClientId = clientId,
            TokenScope = tokenScope,
            SubjectTokenPath = path,
            // The deprecated name only when it gave the path; a GitHub Actions source has none.
            SubjectTokenPathSource = tokenFile is null && subjectTokenFile is not null ? StorytellerVariables.SUBJECT_TOKEN_FILE : StorytellerVariables.TOKEN_FILE,
            SubjectTokenType = normalizedType,
            GitHubRequestUrl = gitHubRequestUrl,
            GitHubRequestToken = path is null ? gitHub.Token : null,
        };
    }

    private static string? ReadMachine(string apiKey)
    {
        var segments = apiKey.Split('.', 3);
        return segments is ["2s", { Length: > 0 } machine, { Length: > 0 }] ? machine : null;
    }

    // ACTIONS_ID_TOKEN_REQUEST_URL and ACTIONS_ID_TOKEN_REQUEST_TOKEN; a source only when both are set.
    private readonly record struct GitHubRequest(string? Url, string? Token)
    {
        public bool IsSet => Url is not null && Token is not null;

        // The request token is a secret.
        public override string ToString()
        {
            return $"{nameof(GitHubRequest)} {{ IsSet = {IsSet} }}";
        }
    }
}

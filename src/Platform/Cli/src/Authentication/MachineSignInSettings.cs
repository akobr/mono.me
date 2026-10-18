using System;
using System.Collections.Generic;
using System.Linq;

namespace _42.Platform.Cli.Authentication;

// The STORYTELLER_* variables of a non-interactive sign-in, and the GitHub Actions ones a federated
// machine can use, read once per process. Values are trimmed and an empty value counts as unset.
// A class, not a record: a generated ToString would print the secrets.
public sealed class MachineSignInSettings
{
    private const string ApiKeyScheme = "ApiKey ";
    private const string TokenTypePrefix = "urn:ietf:params:oauth:token-type:";

    private static readonly string[] SubjectTokenTypes = [TokenExchange.JwtTokenType, TokenExchange.IdTokenType, TokenExchange.AccessTokenType];

    public string? ApiUrl { get; init; }

    public string? Project { get; init; }

    public string? View { get; init; }

    public string? ApiKey { get; init; }

    public string? CertificateFile { get; init; }

    public string? CertificatePassword { get; init; }

    public string? ClientId { get; init; }

    public string? ClientSecret { get; init; }

    public string? TokenUrl { get; init; }

    public string? TokenScope { get; init; }

    public string? TokenFile { get; init; }

    public string? SubjectTokenFile { get; init; }

    public string? SubjectTokenType { get; init; }

    public string? GitHubTokenRequestUrl { get; init; }

    public string? GitHubTokenRequestToken { get; init; }

    // Both GitHub Actions variables are set; on its own this never configures a machine.
    public bool HasGitHubTokenSource => GitHubTokenRequestUrl is not null && GitHubTokenRequestToken is not null;

    // Any credential variable is set: sform signs in as that machine and never as the stored user.
    // The GitHub Actions variables are not among them: a job with id-token: write may run sform as a user.
    public bool IsConfigured => CredentialVariables.Count > 0;

    // The names of the credential variables that are set, never their values.
    public IReadOnlyList<string> CredentialVariables => GetCredentialVariables().ToList();

    public static MachineSignInSettings FromEnvironment(Func<string, string?> getVariable)
    {
        string? Read(string name)
        {
            var value = getVariable(name)?.Trim();
            return string.IsNullOrEmpty(value) ? null : value;
        }

        var apiKey = Read(StorytellerVariables.API_KEY);

        // The scheme of the Authorization header is accepted too, as curl snippets show it.
        if (apiKey is not null && apiKey.StartsWith(ApiKeyScheme, StringComparison.OrdinalIgnoreCase))
        {
            apiKey = apiKey[ApiKeyScheme.Length..].Trim();
            apiKey = apiKey.Length > 0 ? apiKey : null;
        }

        return new MachineSignInSettings
        {
            ApiUrl = Read(StorytellerVariables.API_URL),
            Project = Read(StorytellerVariables.PROJECT),
            View = Read(StorytellerVariables.VIEW),
            ApiKey = apiKey,
            CertificateFile = Read(StorytellerVariables.CERTIFICATE_FILE),
            CertificatePassword = Read(StorytellerVariables.CERTIFICATE_PASSWORD),
            ClientId = Read(StorytellerVariables.CLIENT_ID),
            ClientSecret = Read(StorytellerVariables.CLIENT_SECRET),
            TokenUrl = Read(StorytellerVariables.TOKEN_URL),
            TokenScope = Read(StorytellerVariables.TOKEN_SCOPE),
            TokenFile = Read(StorytellerVariables.TOKEN_FILE),
            SubjectTokenFile = Read(StorytellerVariables.SUBJECT_TOKEN_FILE),
            SubjectTokenType = Read(StorytellerVariables.SUBJECT_TOKEN_TYPE),
            GitHubTokenRequestUrl = Read(GitHubActionsVariables.ID_TOKEN_REQUEST_URL),
            GitHubTokenRequestToken = Read(GitHubActionsVariables.ID_TOKEN_REQUEST_TOKEN),
        };
    }

    // <organization>.<project>, split at the first dot like -p|--projectKey.
    public static bool TrySplitProject(string? projectKey, out string organization, out string project)
    {
        var separatorIndex = projectKey?.IndexOf('.') ?? -1;

        if (projectKey is null || separatorIndex <= 0 || separatorIndex == projectKey.Length - 1)
        {
            organization = string.Empty;
            project = string.Empty;
            return false;
        }

        organization = projectKey[..separatorIndex];
        project = projectKey[(separatorIndex + 1)..];
        return true;
    }

    // https anywhere, http only to the local host: credentials never cross a network in clear text.
    public static bool IsSecureOrLocal(Uri uri)
    {
        return uri.Scheme == Uri.UriSchemeHttps || IsLocalHttp(uri);
    }

    // http to localhost or a loopback address literal, never to a name that DNS resolves.
    public static bool IsLocalHttp(Uri uri)
    {
        return uri.Scheme == Uri.UriSchemeHttp
            && uri.IsLoopback
            && (uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6
                || string.Equals(uri.IdnHost, "localhost", StringComparison.OrdinalIgnoreCase));
    }

    // jwt, id_token or access_token (any case), or one of the three URNs; null when the value is none of them.
    public static string? NormalizeSubjectTokenType(string? value)
    {
        if (value is null)
        {
            return TokenExchange.JwtTokenType;
        }

        return SubjectTokenTypes.FirstOrDefault(type =>
            string.Equals(value, type, StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, type[TokenTypePrefix.Length..], StringComparison.OrdinalIgnoreCase));
    }

    // The source GetKind chose for a Federated machine; never a token value.
    public FederatedTokenSource GetFederatedTokenSource()
    {
        return SubjectTokenFile is null ? FederatedTokenSource.GitHubActions : FederatedTokenSource.File;
    }

    public MachineSignInKind GetKind()
    {
        var keyOrCertificate = ApiKey is not null || CertificateFile is not null || CertificatePassword is not null;
        var clientCredentials = ClientSecret is not null || TokenUrl is not null;
        var federation = SubjectTokenFile is not null || SubjectTokenType is not null;
        var tokenFile = TokenFile is not null;

        // CLIENT_ID and TOKEN_SCOPE belong to client credentials or federation, whichever is set; alone they are one more kind.
        var sharedOnly = (ClientId is not null || TokenScope is not null) && !clientCredentials && !federation;
        var kinds = new[] { keyOrCertificate, clientCredentials, federation, tokenFile, sharedOnly }.Count(isSet => isSet);

        if (kinds == 0)
        {
            throw Invalid("No machine sign-in variable is set.");
        }

        if (kinds > 1)
        {
            throw Invalid($"Machine sign-in variables of different kinds are set: {string.Join(", ", CredentialVariables)}. Set the variables of one kind only.");
        }

        RequireSecureUrl(ApiUrl, StorytellerVariables.API_URL, $"Set {StorytellerVariables.API_URL} to the API base URL, for example https://<host>/api.");

        if (Project is not null && !TrySplitProject(Project, out _, out _))
        {
            throw Invalid($"{StorytellerVariables.PROJECT} must be <organization>.<project>.");
        }

        if (tokenFile)
        {
            return MachineSignInKind.TokenFile;
        }

        // The GitHub Actions variables are in every job with id-token: write, so alone they only make a machine federated
        // whose id is a GUID. Every federated machine id is one, AuthKit and Keycloak client ids are not: a client-credentials
        // job with a missing secret must not turn into an exchange.
        if (federation || (sharedOnly && Guid.TryParse(ClientId, out _) && HasGitHubTokenSource))
        {
            return GetFederatedKind();
        }

        if (sharedOnly && ClientId is not null)
        {
            throw Invalid(
                $"{StorytellerVariables.CLIENT_ID} needs {StorytellerVariables.CLIENT_SECRET} for client credentials, or a platform token for a federated machine: {StorytellerVariables.SUBJECT_TOKEN_FILE}, or a GitHub Actions job with permissions: id-token: write.");
        }

        if (clientCredentials || sharedOnly)
        {
            var missing = new (string? Value, string Name)[] { (ClientId, StorytellerVariables.CLIENT_ID), (ClientSecret, StorytellerVariables.CLIENT_SECRET) }
                .Where(variable => variable.Value is null)
                .Select(variable => variable.Name)
                .ToList();

            if (missing.Count > 0)
            {
                throw Invalid(
                    $"Client credentials need {StorytellerVariables.CLIENT_ID} and {StorytellerVariables.CLIENT_SECRET}; {string.Join(" and ", missing)} {(missing.Count == 1 ? "is" : "are")} missing.");
            }

            if (TokenUrl is not null)
            {
                RequireSecureUrl(TokenUrl, StorytellerVariables.TOKEN_URL, null);
            }

            return MachineSignInKind.ClientCredentials;
        }

        if (CertificateFile is null)
        {
            return CertificatePassword is null
                ? MachineSignInKind.ApiKey
                : throw Invalid($"{StorytellerVariables.CERTIFICATE_PASSWORD} is set without {StorytellerVariables.CERTIFICATE_FILE}.");
        }

        return ApiKey is null ? MachineSignInKind.Certificate : MachineSignInKind.CertificateAndApiKey;
    }

    private static void RequireSecureUrl(string? value, string variable, string? missingMessage)
    {
        if (value is null)
        {
            throw Invalid(missingMessage ?? $"{variable} is missing.");
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !IsSecureOrLocal(uri))
        {
            throw Invalid($"{variable} must be an absolute https URL; http is allowed only for localhost.");
        }
    }

    private static AuthenticationException Invalid(string message)
    {
        return new AuthenticationException(AuthenticationFailureReason.Configuration, message);
    }

    private MachineSignInKind GetFederatedKind()
    {
        if (ClientId is null)
        {
            throw Invalid($"A federated machine needs {StorytellerVariables.CLIENT_ID}, the machine id.");
        }

        if (SubjectTokenFile is null)
        {
            if (!HasGitHubTokenSource)
            {
                throw Invalid(
                    $"A federated machine needs a platform token: set {StorytellerVariables.SUBJECT_TOKEN_FILE}, or run in a GitHub Actions job with permissions: id-token: write.");
            }

            // The request token goes to this URL.
            RequireSecureUrl(GitHubTokenRequestUrl, GitHubActionsVariables.ID_TOKEN_REQUEST_URL, null);
        }

        return NormalizeSubjectTokenType(SubjectTokenType) is not null
            ? MachineSignInKind.Federated
            : throw Invalid(
                $"{StorytellerVariables.SUBJECT_TOKEN_TYPE} must be jwt, id_token or access_token, or the full {TokenTypePrefix} URN.");
    }

    private IEnumerable<string> GetCredentialVariables()
    {
        var variables = new (string? Value, string Name)[]
        {
            (ApiKey, StorytellerVariables.API_KEY),
            (CertificateFile, StorytellerVariables.CERTIFICATE_FILE),
            (CertificatePassword, StorytellerVariables.CERTIFICATE_PASSWORD),
            (ClientId, StorytellerVariables.CLIENT_ID),
            (ClientSecret, StorytellerVariables.CLIENT_SECRET),
            (TokenUrl, StorytellerVariables.TOKEN_URL),
            (TokenScope, StorytellerVariables.TOKEN_SCOPE),
            (SubjectTokenFile, StorytellerVariables.SUBJECT_TOKEN_FILE),
            (SubjectTokenType, StorytellerVariables.SUBJECT_TOKEN_TYPE),
            (TokenFile, StorytellerVariables.TOKEN_FILE),
        };

        return variables.Where(variable => variable.Value is not null).Select(variable => variable.Name);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace _42.Platform.Cli.Authentication;

// The STORYTELLER_* variables of a non-interactive sign-in, read once per process. Values are
// trimmed and an empty value counts as unset. A class, not a record: a generated ToString would
// print the secrets.
public sealed class MachineSignInSettings
{
    private const string ApiKeyScheme = "ApiKey ";

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

    // Any credential variable is set: sform signs in as that machine and never as the stored user.
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
        return uri.Scheme == Uri.UriSchemeHttps
            || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback);
    }

    public MachineSignInKind GetKind()
    {
        var keyOrCertificate = ApiKey is not null || CertificateFile is not null || CertificatePassword is not null;
        var clientCredentials = ClientId is not null || ClientSecret is not null || TokenUrl is not null || TokenScope is not null;
        var tokenFile = TokenFile is not null;
        var kinds = new[] { keyOrCertificate, clientCredentials, tokenFile }.Count(isSet => isSet);

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

        if (clientCredentials)
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
            (TokenFile, StorytellerVariables.TOKEN_FILE),
        };

        return variables.Where(variable => variable.Value is not null).Select(variable => variable.Name);
    }
}

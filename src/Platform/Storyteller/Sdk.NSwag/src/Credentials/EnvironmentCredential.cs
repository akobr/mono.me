using System;
using System.IO.Abstractions;
using System.Linq;

namespace _42.Platform.Storyteller.Sdk.Credentials;

// StorytellerCredential.FromEnvironment: the issue's names, with the sform names as fallbacks where they mean the same,
// so one set of variables drives sform and the SDK. The values are never part of a message.
internal static class EnvironmentCredential
{
    public static StorytellerCredential? Read(Func<string, string?> getVariable, IFileSystem fileSystem, TimeProvider? timeProvider)
    {
        var variables = new EnvironmentVariables(getVariable);
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

        // The scheme alone, as in "ApiKey ", is no key.
        if (apiKey is not null && string.Equals(apiKey, "ApiKey", StringComparison.OrdinalIgnoreCase))
        {
            throw StorytellerAuthenticationException.Configuration($"{StorytellerVariables.API_KEY} holds no key.");
        }

        var keyOrCertificate = apiKey is not null || certificateFile is not null || certificatePassword is not null;
        var clientCredentials = clientSecret is not null || tokenUrl is not null;

        // With a client id and no client-credentials variable, the token file holds the platform token of an exchange;
        // alone it is a bearer token, as in sform, where the combination is an error.
        var tokenFileIsSubject = tokenFile is not null && clientId is not null && !clientCredentials;
        var federation = tokenFileIsSubject || subjectTokenFile is not null || subjectTokenType is not null;
        var bearerTokenFile = tokenFile is not null && !tokenFileIsSubject;

        // CLIENT_ID and TOKEN_SCOPE belong to client credentials or federation; alone they are one more kind.
        var sharedOnly = (clientId is not null || tokenScope is not null) && !clientCredentials && !federation;
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

        if (bearerTokenFile)
        {
            return new TokenFileCredential(tokenFile!, fileSystem, StorytellerVariables.TOKEN_FILE);
        }

        if (federation)
        {
            return ReadFederated(getVariable, fileSystem, timeProvider, clientId, tokenFileIsSubject ? tokenFile : null, subjectTokenFile, subjectTokenType, tokenScope);
        }

        if (sharedOnly)
        {
            throw StorytellerAuthenticationException.Configuration(clientId is not null
                ? $"{StorytellerVariables.CLIENT_ID} needs {StorytellerVariables.CLIENT_SECRET} for client credentials, or {StorytellerVariables.TOKEN_FILE} with the platform token of a federated machine."
                : $"{StorytellerVariables.TOKEN_SCOPE} needs {StorytellerVariables.CLIENT_ID}, with {StorytellerVariables.CLIENT_SECRET} or {StorytellerVariables.TOKEN_FILE}.");
        }

        if (clientCredentials)
        {
            return ReadClientCredentials(getVariable, timeProvider, clientId, clientSecret, tokenUrl, tokenScope);
        }

        if (certificateFile is null)
        {
            return certificatePassword is null
                ? new ApiKeyCredential(apiKey!, null)
                : throw StorytellerAuthenticationException.Configuration($"{passwordSource} is set without {StorytellerVariables.CLIENT_CERTIFICATE}.");
        }

        var certificate = StorytellerClientCertificate.Load(certificateFile, certificatePassword, fileSystem, certificateSource);
        return apiKey is null
            ? new CertificateCredential(certificate)
            : new ApiKeyCredential(apiKey, certificate);
    }

    private static StorytellerCredential ReadClientCredentials(
        Func<string, string?> getVariable,
        TimeProvider? timeProvider,
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

        var options = new ClientCredentialsOptions
        {
            TokenUrl = tokenUrl is null ? null : LocalUrlRule.Require(tokenUrl, StorytellerVariables.TOKEN_URL),
            ClientId = clientId!,
            ClientSecret = clientSecret!,
            Scope = tokenScope,
            TimeProvider = timeProvider,
        };

        return ClientCredentialsCredential.Create(options, getVariable, StorytellerVariables.TOKEN_URL);
    }

    private static StorytellerCredential ReadFederated(
        Func<string, string?> getVariable,
        IFileSystem fileSystem,
        TimeProvider? timeProvider,
        string? clientId,
        string? tokenFile,
        string? subjectTokenFile,
        string? subjectTokenType,
        string? tokenScope)
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

        var path = tokenFile ?? subjectTokenFile
            ?? throw StorytellerAuthenticationException.Configuration(
                $"A federated machine needs {StorytellerVariables.TOKEN_FILE}, the file with the platform token to exchange.");

        if (FederatedTokenCredential.NormalizeSubjectTokenType(subjectTokenType) is null)
        {
            throw StorytellerAuthenticationException.Configuration(
                $"{StorytellerVariables.SUBJECT_TOKEN_TYPE} must be jwt, id_token or access_token, or the full urn:ietf:params:oauth:token-type: URN.");
        }

        var options = new FederatedTokenOptions
        {
            ClientId = clientId,
            SubjectTokenPath = path,
            SubjectTokenType = subjectTokenType ?? FederatedTokenOptions.JwtTokenType,
            Scope = tokenScope,
            FileSystem = fileSystem,
            TimeProvider = timeProvider,
        };

        var pathSource = tokenFile is not null ? StorytellerVariables.TOKEN_FILE : StorytellerVariables.SUBJECT_TOKEN_FILE;
        return FederatedTokenCredential.Create(options, pathSource, getVariable);
    }
}

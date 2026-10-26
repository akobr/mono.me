using System;
using System.IO.Abstractions;
using System.Security.Cryptography.X509Certificates;

namespace _42.Platform.Storyteller.Sdk.Credentials;

// Builds the credential that StorytellerCredentialVariables read and checked, unbound; the variables of the shared
// integration contract, with the deprecated sform names still read. The values are never part of a message.
internal static class EnvironmentCredential
{
    public static StorytellerCredential Create(
        StorytellerCredentialVariables variables,
        Func<string, string?> getVariable,
        IFileSystem fileSystem,
        TimeProvider? timeProvider)
    {
        return variables.Kind switch
        {
            StorytellerCredentialKind.TokenFile => new TokenFileCredential(variables.TokenFile!, fileSystem, StorytellerVariables.TOKEN_FILE),
            StorytellerCredentialKind.Federated => CreateFederated(variables, getVariable, fileSystem, timeProvider),
            StorytellerCredentialKind.ClientCredentials => ClientCredentialsCredential.Create(
                new ClientCredentialsOptions
                {
                    TokenUrl = variables.TokenUrl,
                    ClientId = variables.ClientId!,
                    ClientSecret = variables.ClientSecret!,
                    Scope = variables.TokenScope,
                    TimeProvider = timeProvider,
                },
                getVariable,
                StorytellerVariables.TOKEN_URL),
            StorytellerCredentialKind.ApiKey => new ApiKeyCredential(variables.ApiKeyAsSet!, null),
            StorytellerCredentialKind.Certificate => new CertificateCredential(LoadCertificate(variables, fileSystem)),
            StorytellerCredentialKind.CertificateAndApiKey => new ApiKeyCredential(variables.ApiKeyAsSet!, LoadCertificate(variables, fileSystem)),
            _ => throw new InvalidOperationException($"No credential of the kind {variables.Kind}."),
        };
    }

    private static StorytellerCredential CreateFederated(
        StorytellerCredentialVariables variables,
        Func<string, string?> getVariable,
        IFileSystem fileSystem,
        TimeProvider? timeProvider)
    {
        if (variables.SubjectTokenSource == StorytellerSubjectTokenSource.GitHubActions)
        {
            return FederatedTokenCredential.CreateGitHubActions(
                variables.ClientId!,
                variables.SubjectTokenType,
                variables.TokenScope,
                variables.GitHubRequestUrl!,
                variables.GitHubRequestToken!,
                timeProvider,
                getVariable);
        }

        var options = new FederatedTokenOptions
        {
            ClientId = variables.ClientId!,
            SubjectTokenPath = variables.SubjectTokenPath!,
            SubjectTokenType = variables.SubjectTokenType,
            Scope = variables.TokenScope,
            FileSystem = fileSystem,
            TimeProvider = timeProvider,
        };

        return FederatedTokenCredential.Create(options, variables.SubjectTokenPathSource, getVariable);
    }

    private static X509Certificate2 LoadCertificate(StorytellerCredentialVariables variables, IFileSystem fileSystem)
    {
        return StorytellerClientCertificate.Load(variables.CertificateFile!, variables.CertificatePassword, fileSystem, variables.CertificateSource);
    }
}

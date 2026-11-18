using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Net.Http;

namespace _42.Platform.Storyteller.Sdk.Credentials;

// An RFC 8693 exchange of a platform token (Kubernetes, Azure, AWS, ...) for a token of a Federated machine. The platform
// token has exactly one source: SubjectTokenPath, SubjectTokenProvider or AzureManagedIdentity.
public sealed record FederatedTokenOptions
{
    public const string JwtTokenType = "urn:ietf:params:oauth:token-type:jwt";
    public const string IdTokenType = "urn:ietf:params:oauth:token-type:id_token";
    public const string AccessTokenType = "urn:ietf:params:oauth:token-type:access_token";

    // null until an initializer sets it: the default then follows the source.
    private readonly string? _subjectTokenType;

    // The endpoint, organization and project of the binding when unset.
    public Uri? Endpoint { get; init; }

    public string? Organization { get; init; }

    public string? Project { get; init; }

    // The machine id.
    public required string ClientId { get; init; }

    // Read again, and trimmed, before every exchange: the platform renews the file.
    public string? SubjectTokenPath { get; init; }

    // Asked again before every exchange, and its token trimmed: any platform, from code.
    public StorytellerSubjectTokenProvider? SubjectTokenProvider { get; init; }

    // The managed identity of the Azure host, asked again before every exchange.
    public AzureManagedIdentityTokenOptions? AzureManagedIdentity { get; init; }

    // A URN, or its short form jwt, id_token or access_token. Unset or blank, it is the access token for a managed
    // identity, whose token is always one, and jwt for the other sources.
    public string SubjectTokenType
    {
        get => string.IsNullOrWhiteSpace(_subjectTokenType) ? (AzureManagedIdentity is null ? JwtTokenType : AccessTokenType) : _subjectTokenType;
        init => _subjectTokenType = value;
    }

    public string? Scope { get; init; }

    public IFileSystem? FileSystem { get; init; }

    public Action<SocketsHttpHandler>? ConfigureHandler { get; init; }

    public TimeProvider? TimeProvider { get; init; }

    // A managed identity token is an access token, of the app registration that stands for Storyteller (resource,
    // api://<appId>); managedIdentityClientId picks a user-assigned identity.
    public static FederatedTokenOptions ForAzureManagedIdentity(string clientId, string resource, string? managedIdentityClientId = null)
    {
        return new FederatedTokenOptions
        {
            ClientId = clientId,
            AzureManagedIdentity = new AzureManagedIdentityTokenOptions { Resource = resource, ManagedIdentityClientId = managedIdentityClientId },
            SubjectTokenType = AccessTokenType,
        };
    }

    public override string ToString()
    {
        return $"{nameof(FederatedTokenOptions)} {{ Endpoint = {StorytellerUrlRule.Describe(Endpoint)}, Organization = {Organization}, Project = {Project}, ClientId = {ClientId}, {DescribeSource()}, SubjectTokenType = {SubjectTokenType}, Scope = {Scope} }}";
    }

    // Options are printed before they are checked, so every source that is set is named; a provider by its kind only, as
    // a delegate has no text. Without a source, the path prints empty, as it always did.
    private string DescribeSource()
    {
        var sources = new List<string>();

        if (SubjectTokenPath is not null || (SubjectTokenProvider is null && AzureManagedIdentity is null))
        {
            sources.Add($"{nameof(SubjectTokenPath)} = {SubjectTokenPath}");
        }

        if (AzureManagedIdentity is { } managedIdentity)
        {
            sources.Add($"SubjectTokenSource = {StorytellerSubjectTokenSource.AzureManagedIdentity}, Resource = {managedIdentity.Resource}, ManagedIdentityClientId = {managedIdentity.ManagedIdentityClientId}");
        }

        if (SubjectTokenProvider is not null)
        {
            sources.Add($"SubjectTokenSource = {StorytellerSubjectTokenSource.Provider}");
        }

        return string.Join(", ", sources);
    }
}

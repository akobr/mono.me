using System;
using System.IO.Abstractions;
using System.Net.Http;

namespace _42.Platform.Storyteller.Sdk.Credentials;

// An RFC 8693 exchange of a platform token (Kubernetes, Azure, AWS, ...) for a token of a Federated machine.
public sealed record FederatedTokenOptions
{
    public const string JwtTokenType = "urn:ietf:params:oauth:token-type:jwt";
    public const string IdTokenType = "urn:ietf:params:oauth:token-type:id_token";
    public const string AccessTokenType = "urn:ietf:params:oauth:token-type:access_token";

    // The endpoint, organization and project of the binding when unset.
    public Uri? Endpoint { get; init; }

    public string? Organization { get; init; }

    public string? Project { get; init; }

    // The machine id.
    public required string ClientId { get; init; }

    // Read again, and trimmed, before every exchange: the platform renews the file.
    public required string SubjectTokenPath { get; init; }

    // A URN, or its short form jwt, id_token or access_token.
    public string SubjectTokenType { get; init; } = JwtTokenType;

    public string? Scope { get; init; }

    public IFileSystem? FileSystem { get; init; }

    public Action<SocketsHttpHandler>? ConfigureHandler { get; init; }

    public TimeProvider? TimeProvider { get; init; }

    public override string ToString()
    {
        return $"{nameof(FederatedTokenOptions)} {{ Endpoint = {StorytellerUrlRule.Describe(Endpoint)}, Organization = {Organization}, Project = {Project}, ClientId = {ClientId}, SubjectTokenPath = {SubjectTokenPath}, SubjectTokenType = {SubjectTokenType}, Scope = {Scope} }}";
    }
}

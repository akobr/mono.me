using System;
using System.IO.Abstractions;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace _42.Platform.Storyteller.Sdk.Credentials;

// A bearer token that something else keeps current in a file; read again before every request.
internal sealed class TokenFileCredential : StorytellerCredential
{
    private const string Scheme = "Bearer";

    private readonly string _path;
    private readonly IFileSystem _fileSystem;
    private readonly string? _source;

    public TokenFileCredential(string path, IFileSystem fileSystem, string? source)
    {
        _path = path;
        _fileSystem = fileSystem;
        _source = source;
    }

    public override ValueTask<AuthenticationHeaderValue?> GetAuthorizationAsync(AuthenticationHeaderValue? rejected, CancellationToken cancellationToken)
    {
        var content = CredentialFiles.ReadText(_fileSystem, _path, _source, "token file");

        // "Bearer <token>" as an Authorization header value works too.
        if (content.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase)
            && (content.Length == Scheme.Length || char.IsWhiteSpace(content[Scheme.Length])))
        {
            content = content[Scheme.Length..].Trim();
        }

        return content.Length > 0
            ? ValueTask.FromResult<AuthenticationHeaderValue?>(new AuthenticationHeaderValue(Scheme, content))
            : throw StorytellerAuthenticationException.Configuration($"The token file {_path}{(_source is null ? string.Empty : $" in {_source}")} holds no token.");
    }

    public override string ToString()
    {
        return $"{nameof(StorytellerCredential)} {{ Kind = TokenFile, Path = {_path} }}";
    }
}

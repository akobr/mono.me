using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller;

// AuthKit publishes a bare JWKS document, not OpenID metadata.
internal sealed class JsonWebKeySetRetriever : IConfigurationRetriever<JsonWebKeySet>
{
    public async Task<JsonWebKeySet> GetConfigurationAsync(string address, IDocumentRetriever retriever, CancellationToken cancel)
    {
        var document = await retriever.GetDocumentAsync(address, cancel);
        return new JsonWebKeySet(document);
    }
}

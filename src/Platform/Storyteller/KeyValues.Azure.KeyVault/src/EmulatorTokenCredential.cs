using Azure.Core;

namespace _42.Platform.Storyteller.KeyValues;

// Lowkey Vault checks that a bearer token is present and ignores its value.
internal sealed class EmulatorTokenCredential : TokenCredential
{
    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        return new AccessToken("lowkey-vault-emulator", DateTimeOffset.MaxValue);
    }

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        return new ValueTask<AccessToken>(GetToken(requestContext, cancellationToken));
    }
}

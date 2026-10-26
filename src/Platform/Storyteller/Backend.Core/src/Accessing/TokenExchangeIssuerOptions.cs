namespace _42.Platform.Storyteller.Accessing;

// An issuer of Auth:TokenExchange:AdditionalIssuers. Both URLs are operator configuration; keys never come from a URL
// that a token names.
public class TokenExchangeIssuerOptions
{
    // The exact iss of its tokens; only a trailing '/' may differ.
    public string Issuer { get; set; } = string.Empty;

    public string JwksUri { get; set; } = string.Empty;
}

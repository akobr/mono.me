using System.Diagnostics.CodeAnalysis;

namespace _42.Platform.Storyteller;

// Where the next page of an annotation list starts: the annotation type and the Cosmos DB token within that type's
// query. The text is base64, so it travels in a query string and a client treats it as opaque.
public class ContinuationToken
{
    private const string FIRST_PAGE_TOKEN = "first";
    private const char TOKEN_SEPARATOR = '|';

    public ContinuationToken(string type, string cosmosToken)
    {
        Type = type;
        CosmosToken = cosmosToken;
    }

    public string Type { get; set; }

    public string CosmosToken { get; set; }

    // The next page starts at the first page of the type: there is no Cosmos DB token to pass on.
    public bool IsFirstPage => CosmosToken == FIRST_PAGE_TOKEN;

    // The form TryParse reads back.
    public override string ToString()
    {
        return $"{Type}{TOKEN_SEPARATOR}{CosmosToken}".ToBase64();
    }

    public static ContinuationToken CreateFirstPage(string type)
    {
        return new ContinuationToken(type, FIRST_PAGE_TOKEN);
    }

    // Only the first separator splits: a Cosmos DB token is opaque and may contain one.
    public static bool TryParse(string? token, [MaybeNullWhen(false)] out ContinuationToken result)
    {
        result = null;

        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        if (!token.TryFromBase64(out var decodedBody))
        {
            return false;
        }

        var segments = decodedBody.Split(TOKEN_SEPARATOR, 2);

        if (segments.Length != 2)
        {
            return false;
        }

        var cosmosToken = segments[1];
        cosmosToken = string.IsNullOrWhiteSpace(cosmosToken) ? FIRST_PAGE_TOKEN : cosmosToken;
        result = new ContinuationToken(segments[0], cosmosToken);
        return true;
    }
}

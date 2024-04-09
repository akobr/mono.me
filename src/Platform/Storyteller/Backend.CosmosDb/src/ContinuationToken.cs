using System.Diagnostics.CodeAnalysis;

namespace _42.Platform.Storyteller;

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

    public bool IsFirstPage => CosmosToken == FIRST_PAGE_TOKEN;

    public override string ToString()
    {
        return $"{Type}{TOKEN_SEPARATOR}{CosmosToken}";
    }

    public static ContinuationToken CreateFirstPage(string type)
    {
        return new ContinuationToken(type, FIRST_PAGE_TOKEN);
    }

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

        var segments = decodedBody.Split(TOKEN_SEPARATOR);

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

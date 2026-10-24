using System.Text;

using Newtonsoft.Json;

namespace _42.Platform.Storyteller.Mcp.Tools;

// Pages an API list in the host. An API page holds up to 1,000 items, far more than an MCP result should; the host
// hands out `limit` items at a time and keeps its place in an opaque continuation token: the API's continuation token
// of the page being read and the offset into it.
public static class HostPage
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 200;

    public static int ClampLimit(int? limit)
    {
        return limit is null ? DefaultLimit : Math.Clamp(limit.Value, 1, MaxLimit);
    }

    public static Cursor Decode(string? continuationToken)
    {
        if (string.IsNullOrWhiteSpace(continuationToken))
        {
            return new Cursor(null, 0);
        }

        try
        {
            var padded = continuationToken.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '=');
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            var cursor = JsonConvert.DeserializeObject<Cursor>(json);
            return cursor is { Offset: >= 0 } ? cursor : throw new FormatException();
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            throw new ToolInputException("'continuationToken' is not a token of this tool; pass the ContinuationToken of the previous result as it is.");
        }
    }

    public static string Encode(Cursor cursor)
    {
        var json = JsonConvert.SerializeObject(cursor);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    // Takes the next items of one API page. apiItems is the page the cursor's ApiToken points at; apiNext the API's
    // token of the page after it.
    public static Page<T> Take<T>(IReadOnlyList<T> apiItems, string? apiNext, Cursor cursor, int limit)
    {
        var items = apiItems.Skip(cursor.Offset).Take(limit).ToList();
        var consumed = cursor.Offset + items.Count;
        string? next = null;

        if (consumed < apiItems.Count)
        {
            next = Encode(cursor with { Offset = consumed });
        }
        else if (!string.IsNullOrEmpty(apiNext))
        {
            next = Encode(new Cursor(apiNext, 0));
        }

        return new Page<T>(items, next);
    }

    public sealed record Cursor(string? ApiToken, int Offset);

    public sealed record Page<T>(IReadOnlyList<T> Items, string? ContinuationToken)
    {
        public int Count => Items.Count;
    }
}

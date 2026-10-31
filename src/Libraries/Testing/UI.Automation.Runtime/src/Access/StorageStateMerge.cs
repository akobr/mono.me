using System.Text.Json.Nodes;

namespace _42.Testing.UI.Automation.Runtime.Access;

// Merges the storage state a test's context ended with into the cached one (Playwright's JSON: cookies, and origins
// with their localStorage items): the cookies come from the context, the sign-in's items of each origin from the
// context, every other item and origin property from the cache.
internal static class StorageStateMerge
{
    // isSignInItem: (origin, name). Null when the cached state had sign-in items and the context has none of them left,
    // i.e. the application signed the user out.
    public static string? Merge(string cached, string current, Func<string, string, bool> isSignInItem)
    {
        ArgumentNullException.ThrowIfNull(cached);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(isSignInItem);

        var result = JsonNode.Parse(cached)?.AsObject() ?? new JsonObject();
        var source = JsonNode.Parse(current)?.AsObject() ?? new JsonObject();

        var signInItems = new Dictionary<string, List<JsonObject>>(StringComparer.Ordinal);
        foreach (var (origin, item) in Items(source))
        {
            if (isSignInItem(origin, Name(item)))
            {
                if (!signInItems.TryGetValue(origin, out var items))
                {
                    signInItems[origin] = items = [];
                }

                items.Add(item);
            }
        }

        var hadSignInItems = Items(result).Any(entry => isSignInItem(entry.Origin, Name(entry.Item)));
        if (hadSignInItems && signInItems.Count == 0)
        {
            return null;
        }

        var origins = new JsonArray();
        var merged = new HashSet<string>(StringComparer.Ordinal);
        foreach (var originState in (result["origins"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
        {
            var copy = originState.DeepClone().AsObject();
            if ((string?)copy["origin"] is { } origin)
            {
                var localStorage = new JsonArray();
                foreach (var item in (copy["localStorage"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().Where(item => !isSignInItem(origin, Name(item))))
                {
                    localStorage.Add(item.DeepClone());
                }

                foreach (var item in signInItems.GetValueOrDefault(origin) ?? [])
                {
                    localStorage.Add(item.DeepClone());
                }

                copy["localStorage"] = localStorage;
                merged.Add(origin);
            }

            origins.Add(copy);
        }

        foreach (var (origin, items) in signInItems.Where(entry => !merged.Contains(entry.Key)))
        {
            origins.Add(new JsonObject
            {
                ["origin"] = origin,
                ["localStorage"] = new JsonArray(items.Select(item => item.DeepClone()).ToArray()),
            });
        }

        result["cookies"] = source["cookies"]?.DeepClone() ?? new JsonArray();
        result["origins"] = origins;
        return result.ToJsonString();
    }

    private static IEnumerable<(string Origin, JsonObject Item)> Items(JsonObject state)
    {
        foreach (var originState in (state["origins"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
        {
            if ((string?)originState["origin"] is not { } origin)
            {
                continue;
            }

            foreach (var item in (originState["localStorage"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
            {
                yield return (origin, item);
            }
        }
    }

    private static string Name(JsonObject item)
    {
        return (string?)item["name"] ?? string.Empty;
    }
}

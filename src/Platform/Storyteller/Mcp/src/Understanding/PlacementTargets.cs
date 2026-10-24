using _42.Platform.Storyteller.Mcp.Tools;

using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Mcp.Understanding;

// The values to place: the pointers, and the content a candidate gets in a replay. Every scalar is replaced by a marker
// that no stored document holds, so a replay tells a placed value from an equal value set elsewhere: the value is
// effective only where its marker survives. Arrays keep their items, because arrays merge by union: they are effective
// when every item arrives.
public sealed class PlacementTargets
{
    public const int MaxTargets = 20;

    private const string MarkerPrefix = "\u0000placement-marker:";

    private readonly List<JToken> _expected;

    private PlacementTargets(List<string> pointers, List<JToken> expected, JObject content, JObject? fragment, bool ignoresNulls)
    {
        Pointers = pointers;
        _expected = expected;
        Content = content;
        Fragment = fragment;
        IgnoresNulls = ignoresNulls;
    }

    public IReadOnlyList<string> Pointers { get; }

    public JObject Content { get; }

    // The fragment as given, for the schema preview; null when only pointers were given.
    public JObject? Fragment { get; }

    public bool IgnoresNulls { get; }

    public static PlacementTargets FromPointers(IEnumerable<string?> pointers)
    {
        var parsed = pointers
            .Select(pointer => JsonPointer.Parse(pointer, "pointers"))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (parsed.Count == 0 || parsed.Any(pointer => pointer.Length == 0))
        {
            throw new ToolInputException("'pointers' names the values to place, for example [\"/database/host\"]; the root (\"\") is the whole document.");
        }

        if (parsed.Count > MaxTargets)
        {
            throw new ToolInputException($"'pointers' holds {parsed.Count} pointers; place at most {MaxTargets} values at a time.");
        }

        foreach (var pointer in parsed)
        {
            if (parsed.FirstOrDefault(other => other != pointer && JsonPointer.IsAtOrUnder(other, pointer)) is { } nested)
            {
                throw new ToolInputException($"'pointers' holds '{pointer}' and '{nested}' under it; name each value once.");
            }
        }

        var content = new JObject();
        var expected = new List<JToken>();

        foreach (var pointer in parsed)
        {
            var marker = Marker(expected.Count);
            JsonPointer.Set(content, pointer, marker);
            expected.Add(marker);
        }

        return new PlacementTargets(parsed, expected, content, null, ignoresNulls: false);
    }

    // The leaves of the fragment: scalars and arrays. A null places nothing (a write never erases with it), and an empty
    // object holds no value.
    public static PlacementTargets FromFragment(JObject fragment)
    {
        if (fragment.Property("$remove", StringComparison.Ordinal) is not null || fragment.Property("$patch", StringComparison.Ordinal) is not null)
        {
            throw new ToolInputException("'fragment' holds the values to place; $remove and $patch remove values and are not placed.");
        }

        var pointers = new List<string>();
        var expected = new List<JToken>();
        var content = new JObject();
        var ignoresNulls = false;

        Collect(fragment, content, string.Empty);

        if (pointers.Count == 0)
        {
            throw new ToolInputException("'fragment' holds no value to place: give at least one scalar or array, for example {\"database\": {\"host\": \"db.example\"}}.");
        }

        if (pointers.Count > MaxTargets)
        {
            throw new ToolInputException($"'fragment' holds {pointers.Count} values; place at most {MaxTargets} values at a time.");
        }

        return new PlacementTargets(pointers, expected, content, (JObject)fragment.DeepClone(), ignoresNulls);

        void Collect(JObject source, JObject target, string pointer)
        {
            foreach (var property in source.Properties())
            {
                var path = JsonPointer.Append(pointer, property.Name);

                switch (property.Value)
                {
                    case JObject { HasValues: true } child:
                    {
                        var nested = new JObject();
                        target[property.Name] = nested;
                        Collect(child, nested, path);
                        break;
                    }

                    case JObject:
                        target[property.Name] = new JObject();
                        break;

                    case JArray array:
                    {
                        var items = (JArray)array.DeepClone();
                        target[property.Name] = items;
                        pointers.Add(path);
                        expected.Add(items);
                        break;
                    }

                    case { Type: JTokenType.Null }:
                        ignoresNulls = true;
                        break;

                    default:
                    {
                        var marker = Marker(expected.Count);
                        target[property.Name] = marker;
                        pointers.Add(path);
                        expected.Add(marker);
                        break;
                    }
                }
            }
        }
    }

    // The value the fragment places at the target; null when only pointers were given.
    public JToken? ValueAt(int target)
    {
        return Fragment is null ? null : JsonPointer.Find(Fragment, Pointers[target]);
    }

    // Whether the replayed value at the target's pointer is the placed one.
    public bool IsEffective(int target, JToken? replayed)
    {
        return _expected[target] switch
        {
            JArray items => replayed is JArray array && items.All(item => array.Any(existing => JToken.DeepEquals(existing, item))),
            var marker => replayed is JValue value && JToken.DeepEquals(value, marker),
        };
    }

    private static JValue Marker(int index)
    {
        return new JValue($"{MarkerPrefix}{index}");
    }
}

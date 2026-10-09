using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Binding.Object.UnitTests;

internal static class Fixtures
{
    public static JObject Parse(string json)
    {
        using var reader = new Newtonsoft.Json.JsonTextReader(new StringReader(json))
        {
            DateParseHandling = Newtonsoft.Json.DateParseHandling.None,
        };
        return JObject.Load(reader);
    }

    /// <summary>
    /// Builds <c>{ "value": { "$binding": kind, "$definition": definition, "$context": context } }</c>.
    /// </summary>
    public static JObject Envelope(string kind, JToken definition, JObject? context = null)
    {
        return new JObject { ["value"] = EnvelopeToken(kind, definition, context) };
    }

    public static JObject EnvelopeToken(string kind, JToken definition, JObject? context = null)
    {
        var envelope = new JObject
        {
            ["$binding"] = kind,
            ["$definition"] = definition,
        };
        if (context is not null)
        {
            envelope["$context"] = context;
        }

        return envelope;
    }

    public static JArray NumberArray(int count, int value = 0)
    {
        var items = new JArray();
        for (var index = 0; index < count; index++)
        {
            items.Add(value);
        }

        return items;
    }

    public static JArray Sequence(int count)
    {
        var items = new JArray();
        for (var index = 0; index < count; index++)
        {
            items.Add(index);
        }

        return items;
    }

    public static string Repeat(string term, int count, string separator)
    {
        return string.Join(separator, Enumerable.Repeat(term, count));
    }

    public static JToken DoublingLets(int doublings)
    {
        JToken current = new JObject { ["$eval"] = "s" };
        for (var index = 0; index < doublings; index++)
        {
            current = new JObject
            {
                ["$let"] = new JObject
                {
                    ["s"] = new JObject { ["$eval"] = "s+s" },
                },
                ["in"] = current,
            };
        }

        return new JObject
        {
            ["$let"] = new JObject { ["s"] = "a" },
            ["in"] = current,
        };
    }

    /// <summary>
    /// A JSON-e chain of envelopes; each level emits the next one with escaped <c>$$</c> keys.
    /// </summary>
    public static JToken Chain(int evaluations)
    {
        JToken current = new JObject
        {
            ["$binding"] = "jlogic",
            ["$definition"] = "done",
        };

        for (var wrap = 1; wrap < evaluations; wrap++)
        {
            current = new JObject
            {
                ["$binding"] = "jsone",
                ["$definition"] = EscapeDollars(current),
            };
        }

        return current;
    }

    /// <summary>
    /// Envelopes that emit two copies of the next level: <c>2^levels - 1</c> evaluations.
    /// </summary>
    public static JToken FanOut(int levels)
    {
        JToken current = new JObject
        {
            ["$binding"] = "jlogic",
            ["$definition"] = "done",
        };

        for (var level = 1; level < levels; level++)
        {
            current = new JObject
            {
                ["$binding"] = "jsone",
                ["$definition"] = new JArray(EscapeDollars(current), EscapeDollars(current)),
            };
        }

        return current;
    }

    /// <summary>
    /// Escapes every <c>$</c> key once, so one JSON-e pass emits the token unchanged.
    /// </summary>
    public static JToken EscapeDollars(JToken token)
    {
        switch (token)
        {
            case JObject obj:
            {
                var copy = new JObject();
                foreach (var property in obj.Properties())
                {
                    var name = property.Name.StartsWith('$') ? "$" + property.Name : property.Name;
                    copy[name] = EscapeDollars(property.Value);
                }

                return copy;
            }

            case JArray array:
            {
                var copy = new JArray();
                foreach (var item in array)
                {
                    copy.Add(EscapeDollars(item));
                }

                return copy;
            }

            default:
                return token.DeepClone();
        }
    }
}

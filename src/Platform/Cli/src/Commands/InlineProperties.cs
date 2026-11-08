using System;
using System.Collections.Generic;
using System.Globalization;
using _42.Platform.Cli.Output.Exceptions;
using _42.Platform.Storyteller.ConfigurationFormats;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Cli.Commands;

// The -x|--properties values of config set and template set: path=value, where the path is in the syntax of JsonPaths
// (db.port, hosts[0], Logging.LogLevel['Microsoft.AspNetCore'], optionally led by $), and the value is the rest of the
// argument, typed by PropertyValueParser. Each value is written into the document at its path, in command-line order,
// creating the objects and arrays on the way. The messages name the path, never a value, which may be a secret.
public static class InlineProperties
{
    private const string NameHint = "write a name with other characters than ASCII letters, digits, '_' and '-' as ['name']";

    // Writes the value of every path=value into document; throws WrongInputException naming the path.
    public static void Apply(JObject document, IReadOnlyList<string> inlineProperties)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(inlineProperties);

        for (var index = 0; index < inlineProperties.Count; index++)
        {
            var subject = string.Create(
                CultureInfo.InvariantCulture,
                $"The inline property {index + 1} of {inlineProperties.Count}");

            Apply(document, inlineProperties[index], subject);
        }
    }

    private static void Apply(JObject document, string inlineProperty, string subject)
    {
        var (segments, value) = Read(inlineProperty, subject);

        if (segments.Count == 0)
        {
            throw new WrongInputException($"{subject} names no property: its path is {JsonPaths.Root}.");
        }

        if (segments[0].IsIndex)
        {
            throw new WrongInputException(
                $"{subject} starts with an array index, {JsonPaths.Of([segments[0]])}, but the document is an object.");
        }

        // The provider refuses an empty name (empty-name), and "a['$binding']=x" in double quotes reaches sform as a['']=x.
        foreach (var segment in segments)
        {
            if (segment.Name is { Length: 0 })
            {
                throw new WrongInputException(
                    $"{subject} cannot set {JsonPaths.Of(segments)}: a name is empty, which a configuration cannot hold. A shell may have expanded a $name of the argument: in bash and zsh write \\$ inside the double quotes, in PowerShell use single quotes and double the quotes of ['…'].");
            }
        }

        // Every container on the way fits its next step: Enter checks it before the walk goes in.
        JToken container = document;

        for (var index = 0; index < segments.Count; index++)
        {
            var segment = segments[index];
            var isLast = index == segments.Count - 1;

            if (segment.IsIndex)
            {
                var array = (JArray)container;
                var count = array.Count;

                if (segment.Index > count)
                {
                    var items = count == 1 ? "1 item" : $"{count.ToString(CultureInfo.InvariantCulture)} items";

                    throw new WrongInputException(
                        $"{subject} cannot set {JsonPaths.Of(segments)}: {JsonPaths.Of(Take(segments, index))} has {items}, so its index can be at most {count.ToString(CultureInfo.InvariantCulture)}.");
                }

                var item = segment.Index < count ? array[segment.Index] : null;
                var next = isLast ? value : Enter(item, segments, index, subject);

                // An existing container is entered as it is: setting it again would put a copy in its place.
                if (!ReferenceEquals(next, item))
                {
                    Put(array, segment.Index, next);
                }

                container = next;
            }
            else
            {
                var obj = (JObject)container;
                var child = Find(obj, segment.Name!)?.Value;
                var next = isLast ? value : Enter(child, segments, index, subject);

                if (!ReferenceEquals(next, child))
                {
                    Put(obj, segment.Name!, next);
                }

                container = next;
            }
        }
    }

    // Replaces the item at index, or appends at the end. Not through the indexer, which keeps an item it deems equal:
    // Newtonsoft compares a date with only the wall-clock time of a date with an offset, so another instant was dropped.
    private static void Put(JArray array, int index, JToken token)
    {
        if (index < array.Count)
        {
            array.RemoveAt(index);
            array.Insert(index, token);
        }
        else
        {
            array.Add(token);
        }
    }

    // Replaces the property of the name in place, keeping the spelling it has, or adds it at the end; not through the
    // indexer, as for an item.
    private static void Put(JObject obj, string name, JToken token)
    {
        var property = Find(obj, name);

        if (property is null)
        {
            obj.Add(name, token);
        }
        else
        {
            property.Replace(new JProperty(property.Name, token));
        }
    }

    // The property of the name: the exact spelling first, else one that differs only in case, as IConfiguration compares
    // keys case-insensitively; -x Logging.x over a file's "logging" must not add a second spelling of one key.
    private static JProperty? Find(JObject obj, string name)
    {
        return obj.Property(name, StringComparison.Ordinal) ?? obj.Property(name, StringComparison.OrdinalIgnoreCase);
    }

    // The path, then '=', then the value: a bracketed name may hold '=', so the path is read before the '=' is looked for.
    private static (IReadOnlyList<DocumentSegment> Segments, JValue Value) Read(string inlineProperty, string subject)
    {
        IReadOnlyList<DocumentSegment> segments;
        int length;

        try
        {
            segments = JsonPaths.ParsePrefix(inlineProperty, out length);
        }
        catch (JsonPathFormatException exception)
        {
            throw new WrongInputException(
                $"{subject} has a wrong path at character {Character(exception.Position)}. {exception.Message}{QuotesHint(inlineProperty, exception.Position)}");
        }

        // Without the path: an argument that is a name alone may be a token passed without its name= ("$API_KEY").
        if (length >= inlineProperty.Length || inlineProperty[length] != '=')
        {
            var hint = length < inlineProperty.Length && inlineProperty[length] == ':'
                ? "':' does not separate names: write a.b, or ['a:b'] for a name with a colon"
                : NameHint;

            throw new WrongInputException(
                $"{subject} has no '=' after its path, at character {Character(length)}. Write it as path=value; {hint}.");
        }

        return (segments, new JValue(PropertyValueParser.ParseValue(inlineProperty[(length + 1)..])));
    }

    // The container that the step after segments[index] goes into, given the token at segments[index]: the token itself
    // when it fits that step, a new object or array for an absent token or a null (which makes no key and never erases
    // in a merge either), else a wrong input that names the path to the token.
    private static JToken Enter(JToken? token, IReadOnlyList<DocumentSegment> segments, int index, string subject)
    {
        var nextIsIndex = segments[index + 1].IsIndex;

        if (token is null || token.Type == JTokenType.Null)
        {
            return nextIsIndex ? new JArray() : new JObject();
        }

        if (nextIsIndex ? token is JArray : token is JObject)
        {
            return token;
        }

        throw new WrongInputException(
            $"{subject} cannot set {JsonPaths.Of(segments)}: {JsonPaths.Of(Take(segments, index + 1))} is {Describe(token.Type)}, not {(nextIsIndex ? "an array" : "an object")}.");
    }

    private static string Describe(JTokenType type)
    {
        return type switch
        {
            JTokenType.Object => "an object",
            JTokenType.Array => "an array",
            JTokenType.String => "a string",
            JTokenType.Integer or JTokenType.Float => "a number",
            JTokenType.Boolean => "a boolean",
            JTokenType.Date => "a date",
            JTokenType.TimeSpan => "a time span",
            _ => "a value",
        };
    }

    private static IEnumerable<DocumentSegment> Take(IReadOnlyList<DocumentSegment> segments, int count)
    {
        for (var index = 0; index < count; index++)
        {
            yield return segments[index];
        }
    }

    // a[b.c]=1 is what bash and PowerShell pass for an unquoted a['b.c']=1: the shell removed the quotes.
    private static string QuotesHint(string inlineProperty, int position)
    {
        var isStrippedQuote = position > 0
            && position < inlineProperty.Length
            && inlineProperty[position - 1] == '['
            && inlineProperty[position] != '\''
            && !char.IsAsciiDigit(inlineProperty[position]);

        return isStrippedQuote
            ? " Quote the whole argument: a shell removes the quotes of ['name'] otherwise."
            : string.Empty;
    }

    // Counted from 1 for people.
    private static string Character(int position)
    {
        return (position + 1).ToString(CultureInfo.InvariantCulture);
    }
}

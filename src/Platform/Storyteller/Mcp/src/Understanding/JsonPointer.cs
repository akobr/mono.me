using System.Globalization;

using _42.Platform.Storyteller.Mcp.Tools;

using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Mcp.Understanding;

// RFC 6901 pointers as compositions use them: "" is the root, every other pointer starts with '/', and '~' and '/' in a
// name are escaped as ~0 and ~1.
public static class JsonPointer
{
    public static string Append(string pointer, string name)
    {
        return $"{pointer}/{name.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal)}";
    }

    public static string Append(string pointer, int index)
    {
        return $"{pointer}/{index.ToString(CultureInfo.InvariantCulture)}";
    }

    public static IReadOnlyList<string> Split(string pointer)
    {
        return pointer.Length == 0
            ? []
            : pointer[1..].Split('/').Select(segment => segment.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal)).ToList();
    }

    // A pointer argument of a tool: omitted or empty is the root.
    public static string Parse(string? pointer, string parameter)
    {
        if (string.IsNullOrEmpty(pointer))
        {
            return string.Empty;
        }

        return pointer[0] == '/'
            ? pointer
            : throw new ToolInputException($"'{parameter}' must be a JSON pointer (RFC 6901) that starts with '/', for example /database/host; '{pointer}' is not.");
    }

    public static bool IsAtOrUnder(string path, string prefix)
    {
        return prefix.Length == 0
            || string.Equals(path, prefix, StringComparison.Ordinal)
            || path.StartsWith(prefix + "/", StringComparison.Ordinal);
    }

    // The value at the pointer: object members by name, array items by index.
    public static JToken? Find(JToken? root, string pointer)
    {
        var current = root;

        foreach (var segment in Split(pointer))
        {
            current = Child(current, segment);

            if (current is null)
            {
                return null;
            }
        }

        return current;
    }

    // The value at the pointer through object members only: a source document has its own array positions.
    public static JToken? FindInObjects(JToken? root, string pointer)
    {
        var current = root;

        foreach (var segment in Split(pointer))
        {
            current = current is JObject obj ? obj.Property(segment, StringComparison.Ordinal)?.Value : null;

            if (current is null)
            {
                return null;
            }
        }

        return current;
    }

    // The deepest value along the pointer, so a replaced container can be told from a missing member.
    public static JToken FindDeepest(JToken root, string pointer)
    {
        var current = root;

        foreach (var segment in Split(pointer))
        {
            var child = Child(current, segment);

            if (child is null)
            {
                return current;
            }

            current = child;
        }

        return current;
    }

    // Whether an array lies on the way to the value (the value itself excluded).
    public static bool IsInArray(JToken root, string pointer)
    {
        var current = root;

        foreach (var segment in Split(pointer))
        {
            if (current is JArray)
            {
                return true;
            }

            current = Child(current, segment);

            if (current is null)
            {
                return false;
            }
        }

        return false;
    }

    // Sets the value at the pointer, creating the objects on the way (a value that is not an object is replaced).
    public static void Set(JObject root, string pointer, JToken value)
    {
        var segments = Split(pointer);
        var current = root;

        for (var index = 0; index < segments.Count - 1; index++)
        {
            if (current[segments[index]] is not JObject next)
            {
                next = new JObject();
                current[segments[index]] = next;
            }

            current = next;
        }

        current[segments[^1]] = value;
    }

    private static JToken? Child(JToken? token, string segment)
    {
        return token switch
        {
            JObject obj => obj.Property(segment, StringComparison.Ordinal)?.Value,
            JArray array when IsIndex(segment, out var index) && index < array.Count => array[index],
            _ => null,
        };
    }

    private static bool IsIndex(string segment, out int index)
    {
        index = -1;
        return segment.Length > 0
            && (segment == "0" || segment[0] != '0')
            && segment.All(char.IsAsciiDigit)
            && int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out index);
    }
}

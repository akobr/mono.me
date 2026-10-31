using TUnit.Core;

namespace _42.Testing.UI.Automation.Runtime.TUnit;

// The --test-parameter values for the settings loader. TUnit cuts a value at its second '=' (Target:BaseUrl=https://x/?a=b
// arrives as https://x/?a), so the raw command line wins over TUnit's parsed values; those stay for hosts that pass the
// parameters another way. A repeated key keeps its last value.
internal static class TestParameters
{
    public const string Option = "--test-parameter";

    public static IReadOnlyList<KeyValuePair<string, string?>> Read()
    {
        return Merge(TestContext.Parameters, Environment.GetCommandLineArgs());
    }

    public static IReadOnlyList<KeyValuePair<string, string?>> Merge(IReadOnlyDictionary<string, List<string>> parsed, IReadOnlyList<string> arguments)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, list) in parsed)
        {
            if (list.Count > 0)
            {
                values[key] = list[^1];
            }
        }

        foreach (var (key, value) in ParseCommandLine(arguments))
        {
            values[key] = value;
        }

        return values.ToArray();
    }

    // "--test-parameter A=1 B=x=y --other" gives A=1 and B=x=y; "--test-parameter=A=1" and "--test-parameter:A=1" too.
    public static IEnumerable<KeyValuePair<string, string>> ParseCommandLine(IReadOnlyList<string> arguments)
    {
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (string.Equals(argument, Option, StringComparison.Ordinal))
            {
                for (var next = index + 1; next < arguments.Count && !arguments[next].StartsWith("--", StringComparison.Ordinal); next++)
                {
                    if (Split(arguments[next]) is { } pair)
                    {
                        yield return pair;
                    }

                    index = next;
                }
            }
            else if (argument.Length > Option.Length
                && argument.StartsWith(Option, StringComparison.Ordinal)
                && argument[Option.Length] is '=' or ':'
                && Split(argument[(Option.Length + 1)..]) is { } inline)
            {
                yield return inline;
            }
        }
    }

    private static KeyValuePair<string, string>? Split(string value)
    {
        var separator = value.IndexOf('=', StringComparison.Ordinal);
        return separator > 0 ? new KeyValuePair<string, string>(value[..separator].Trim(), value[(separator + 1)..]) : null;
    }
}

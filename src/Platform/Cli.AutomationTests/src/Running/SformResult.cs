using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace _42.Platform.Cli.AutomationTests.Running;

// What one finished run of sform gave: its exit code, the whole standard output and error as they were written (StdOut,
// StdErr) and as text (PlainStdOut, PlainStdErr: without escape sequences, the standard output also with the 80-column
// folds of WriteJson joined, WrappedJson), both in the order they arrived (Timeline, Output), and how long it took. A run
// in a pseudo terminal has one screen: StdOut holds it and StdErr is empty.
//
// The assertions (ShouldSucceed, ShouldExitWith, ShouldContain, …) throw an SformAssertionException whose message has
// the command line, the exit code, what the assertion looked for and the output, with every known secret as *** (the
// settings' secrets, the pool e-mails and what the test registered). They return the result, so they chain:
//   var get = (await Sform(InProject("story", "get", key))).ShouldSucceed();
//   var json = (await Sform(InProject("story", "config", key, "--content-only")).ShouldSucceed()).Json<MyContent>();
// ShouldSucceed, ShouldExitWith and ShouldFail also work on the task (SformResultTaskExtensions).
//
// JSON (JsonNode, Json<T>): sform prints a whole document only with --content-only, --value-only, --format json/--json,
// and into a file (-e/--export, -o, the kv export); the read commands (story get, story set, config, config set, kv get,
// machine create, …) print their model through WriteJson, with a line-number gutter (" 1 │ {") and colours, rendered at
// 80 columns: a longer value is folded onto the next lines, which PlainStdOut joins again (WrappedJson). A --format other
// than json prints its text with the gutter too (WriteNumberedText, which writes a "#" as \u0023), unless --content-only.
// JsonNode takes, in this order: the whole plain standard output when it is a JSON document; else the first block of
// numbered lines (the gutter removed, a block starts at line 1); else the first balanced {…} or […] in it.
public sealed partial class SformResult
{
    private readonly Func<string?, string> redact;
    private string? plainStdOut;
    private string? plainStdErr;

    public SformResult(
        IReadOnlyList<string> arguments,
        string commandLine,
        int exitCode,
        string stdOut,
        string stdErr,
        IReadOnlyList<SformLine> timeline,
        TimeSpan duration,
        bool isTerminal,
        string workingDirectory,
        string home,
        Func<string?, string>? redact = null)
    {
        Arguments = arguments;
        CommandLine = commandLine;
        RawExitCode = exitCode;
        ExitCode = ExitCodeOf(exitCode, OperatingSystem.IsWindows());
        StdOut = stdOut;
        StdErr = stdErr;
        Timeline = timeline;
        Duration = duration;
        IsTerminal = isTerminal;
        WorkingDirectory = workingDirectory;
        Home = home;
        this.redact = redact ?? (static text => text ?? string.Empty);
    }

    // The arguments after sform.
    public IReadOnlyList<string> Arguments { get; }

    // "sform <arguments>", quoted as a POSIX shell would need them; for messages.
    public string CommandLine { get; }

    // The exit code as sform returned it: -1 (InputParsing) also where an exit status is a byte (Linux, macOS), which
    // reports it as 255 (RawExitCode, ExitCodeOf).
    public int ExitCode { get; }

    // The exit code as the operating system reported it (255 for sform's -1 on Linux).
    public int RawExitCode { get; }

    // The exit code as CliExit; null for a code sform does not define.
    public CliExit? Exit => Enum.IsDefined(typeof(CliExit), ExitCode) ? (CliExit)ExitCode : null;

    public bool Succeeded => ExitCode == 0;

    // As written, escape sequences included.
    public string StdOut { get; }

    public string StdErr { get; }

    // The standard output without escape sequences and with the folds of WriteJson's 80 columns joined (WrappedJson), so
    // a long key or value is on one line again; AnsiText.Plain(StdOut) is the text exactly as printed.
    public string PlainStdOut => plainStdOut ??= WrappedJson.Join(AnsiText.Plain(StdOut));

    public string PlainStdErr => plainStdErr ??= AnsiText.Plain(StdErr);

    // The lines of PlainStdOut (the screen in a terminal).
    public IReadOnlyList<string> Lines => SplitLines(PlainStdOut);

    public IReadOnlyList<string> ErrorLines => AnsiText.Lines(StdErr);

    // Every plain line of both streams, in the order the lines arrived.
    public IReadOnlyList<SformLine> Timeline { get; }

    // The timeline as text, one line each, the lines of the standard error marked with "[stderr] ".
    public string Output => string.Join('\n', Timeline.Select(line => line.Stream == SformStream.StdErr ? $"[stderr] {line.Text}" : line.Text));

    public TimeSpan Duration { get; }

    public bool IsTerminal { get; }

    public string WorkingDirectory { get; }

    // The SFORM_HOME of the run.
    public string Home { get; }

    // The plain text of a stream (a terminal's screen for any stream of a run in a terminal).
    public string Text(SformStream stream)
    {
        return stream switch
        {
            SformStream.StdErr when !IsTerminal => PlainStdErr,
            SformStream.Terminal when !IsTerminal => Output,
            _ => PlainStdOut,
        };
    }

    // The JSON of the run's standard output; see the class comment for where it is taken from. Throws an
    // SformAssertionException with the output when there is none.
    public JsonNode JsonNode()
    {
        return TryJsonNode() ?? throw Fail("printed no JSON document on the standard output");
    }

    public T Json<T>(JsonSerializerOptions? options = null)
    {
        var node = JsonNode();
        try
        {
            return node.Deserialize<T>(options ?? JsonDefaults.Web) ?? throw Fail($"printed JSON that is null as {typeof(T).Name}");
        }
        catch (JsonException exception)
        {
            throw Fail($"printed JSON that is no {typeof(T).Name}: {exception.Message}");
        }
    }

    public JsonNode? TryJsonNode()
    {
        var plain = PlainStdOut.Trim();
        if (TryParse(plain) is { } whole)
        {
            return whole;
        }

        foreach (var block in NumberedBlocks(PlainStdOut))
        {
            if (TryParse(block) is { } numbered)
            {
                return numbered;
            }
        }

        return FirstBalanced(plain) is { } balanced ? TryParse(balanced) : null;
    }

    // The blocks of numbered lines (" 1 │ {" … " 9 │ }") in the text, the gutter removed; WriteJson and
    // WriteNumberedText print them.
    public static IReadOnlyList<string> NumberedBlocks(string plainText)
    {
        var blocks = new List<string>();
        StringBuilder? current = null;
        var expected = 1;
        foreach (var line in plainText.Split('\n'))
        {
            var match = GutterPattern().Match(line);
            if (match.Success && int.TryParse(match.Groups["n"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            {
                if (number == 1)
                {
                    Close();
                    current = new StringBuilder();
                    expected = 1;
                }

                if (current is not null && number == expected)
                {
                    current.Append(match.Groups["text"].Value.TrimEnd()).Append('\n');
                    expected++;
                    continue;
                }
            }

            Close();
        }

        Close();
        return blocks;

        void Close()
        {
            if (current is { Length: > 0 })
            {
                blocks.Add(current.ToString());
            }

            current = null;
        }
    }

    public SformResult ShouldSucceed()
    {
        return ExitCode == 0 ? this : throw Fail("did not succeed", expected: "0 (Success)");
    }

    public SformResult ShouldExitWith(CliExit expected)
    {
        return ExitCode == (int)expected ? this : throw Fail("ended with another code", expected: $"{(int)expected} ({expected})");
    }

    public SformResult ShouldExitWith(int expected)
    {
        return ExitCode == expected ? this : throw Fail("ended with another code", expected: Describe(expected));
    }

    public SformResult ShouldExitWithAnyOf(params CliExit[] expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        return expected.Any(code => (int)code == ExitCode)
            ? this
            : throw Fail("ended with another code", expected: string.Join(" or ", expected.Select(code => $"{(int)code} ({code})")));
    }

    // sform's code from the code the operating system reported: on Linux and macOS an exit status is a byte, so the -1 of
    // ExitCodes.ERROR_INPUT_PARSING arrives as 255 (also through util-linux script -e). sform defines no 255 and no other
    // negative code, so only 255 is read back as -1; on Windows the code is the one sform returned.
    public static int ExitCodeOf(int reported, bool windows)
    {
        return !windows && reported == 255 ? (int)CliExit.InputParsing : reported;
    }

    // A non-zero code that is not a crash: "fails" of the catalogue.
    public SformResult ShouldFail()
    {
        return ExitCode is not (0 or 1) ? this : throw Fail(ExitCode == 0 ? "succeeded" : "crashed", expected: "a non-zero code other than 1 (Crash)");
    }

    public SformResult ShouldContain(string text, SformStream stream = SformStream.StdOut, StringComparison comparison = StringComparison.Ordinal)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Text(stream).Contains(text, comparison) ? this : throw Fail($"did not write '{text}' to {Name(stream)}");
    }

    public SformResult ShouldNotContain(string text, SformStream stream = SformStream.StdOut, StringComparison comparison = StringComparison.Ordinal)
    {
        ArgumentNullException.ThrowIfNull(text);
        return !Text(stream).Contains(text, comparison) ? this : throw Fail($"wrote '{text}' to {Name(stream)}, which it must not");
    }

    public SformResult ShouldMatch(Regex pattern, SformStream stream = SformStream.StdOut)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        return pattern.IsMatch(Text(stream)) ? this : throw Fail($"wrote nothing that matches /{pattern}/ to {Name(stream)}");
    }

    // A line of the stream equal to text after trimming both.
    public SformResult ShouldHaveLine(string text, SformStream stream = SformStream.StdOut)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lines = AnsiText.Lines(Text(stream));
        return lines.Any(line => string.Equals(line.Trim(), text.Trim(), StringComparison.Ordinal))
            ? this
            : throw Fail($"wrote no line '{text}' to {Name(stream)}");
    }

    // An assertion failure about this run: "<command line> <what>[, expected <expected>]." and the run's output, all of it
    // redacted (what may hold the very secret a ShouldNotContain guards).
    public SformAssertionException Fail(string what, string? expected = null)
    {
        var message = new StringBuilder()
            .Append(redact(CommandLine)).Append(' ').Append(redact(what))
            .Append(expected is null ? string.Empty : $": exit {Describe(ExitCode)}, expected {redact(expected)}")
            .Append('.').Append('\n')
            .Append(Describe());
        return new SformAssertionException(message.ToString());
    }

    // The run as text, redacted: the exit code, the duration, the standard output and error.
    public string Describe()
    {
        var builder = new StringBuilder()
            .Append("exit ").Append(Describe(ExitCode)).Append(" after ").Append(Duration.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)).Append(" s")
            .Append(IsTerminal ? " (in a terminal)" : string.Empty).Append('\n')
            .Append(IsTerminal ? "--- terminal ---" : "--- stdout ---").Append('\n')
            .Append(Cut(PlainStdOut)).Append('\n');
        if (!IsTerminal)
        {
            builder.Append("--- stderr ---").Append('\n').Append(Cut(PlainStdErr)).Append('\n');
        }

        return redact(builder.ToString());
    }

    public override string ToString()
    {
        return $"{redact(CommandLine)}: exit {Describe(ExitCode)}";
    }

    internal static string Describe(int exitCode)
    {
        return Enum.IsDefined(typeof(CliExit), exitCode) ? $"{exitCode} ({(CliExit)exitCode})" : exitCode.ToString(CultureInfo.InvariantCulture);
    }

    private static IReadOnlyList<string> SplitLines(string plain)
    {
        if (plain.Length == 0)
        {
            return [];
        }

        var lines = plain.Split('\n');
        return lines[^1].Length == 0 ? lines[..^1] : lines;
    }

    private static string Name(SformStream stream)
    {
        return stream switch
        {
            SformStream.StdErr => "the standard error",
            SformStream.Terminal => "the output",
            _ => "the standard output",
        };
    }

    // Long outputs keep their start and their end in a message; the attachment has all of it.
    private static string Cut(string text, int max = 6000)
    {
        return text.Length <= max ? text : $"{text[..(max / 2)]}\n… ({text.Length - max} characters, the whole output is in the attachment) …\n{text[^(max / 2)..]}";
    }

    private static JsonNode? TryParse(string text)
    {
        if (text.Length == 0 || (text[0] != '{' && text[0] != '['))
        {
            return null;
        }

        try
        {
            return System.Text.Json.Nodes.JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // The first {…} or […] whose brackets balance, strings respected.
    private static string? FirstBalanced(string text)
    {
        var start = text.IndexOfAny(['{', '[']);
        while (start >= 0)
        {
            var depth = 0;
            var inString = false;
            for (var index = start; index < text.Length; index++)
            {
                var character = text[index];
                if (inString)
                {
                    if (character == '\\')
                    {
                        index++;
                    }
                    else if (character == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                switch (character)
                {
                    case '"':
                        inString = true;
                        break;
                    case '{' or '[':
                        depth++;
                        break;
                    case '}' or ']':
                        depth--;
                        if (depth == 0)
                        {
                            var candidate = text[start..(index + 1)];
                            if (TryParse(candidate) is not null)
                            {
                                return candidate;
                            }

                            index = text.Length;
                        }

                        break;
                }
            }

            start = text.IndexOfAny(['{', '['], start + 1);
        }

        return null;
    }

    // "  12 │ text": the number, a space, the bar (│, or | / ? when an encoding lost it), an optional space, the text.
    [GeneratedRegex(@"^\s*(?<n>\d+) [│|?�] ?(?<text>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex GutterPattern();
}

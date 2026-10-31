using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using _42.CLI.Toolkit.Output;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Output;

// Output that programs read. The payload and the workflow commands go to the standard output of the inner console as
// they are, with LF line ends; every message goes to its error output as one plain line. Nothing here renders through
// Alba or Spectre or writes to System.Console directly, so a redirected standard output holds the payload alone.
public static class PlainOutputExtensions
{
    // Files and the console are written without a byte order mark.
    public static readonly UTF8Encoding Utf8 = new(false);

    public static void WriteRaw(this IExtendedConsole @this, string text)
    {
        var output = @this.Console.Out;
        output.Write(text);
        output.Flush();
    }

    public static void WriteErrorLine(this IExtendedConsole @this, string message)
    {
        var error = @this.Console.Error;
        error.Write(ToPlainLine(message));
        error.Write('\n');
        error.Flush();
    }

    // The message and the code of an API error as plain lines, and the exit code WriteApiError would return.
    public static int WriteApiErrorLine(this IExtendedConsole @this, ApiException exception)
    {
        var (message, errorCode) = KeyValueConsoleExtensions.ReadError(exception);
        var status = (HttpStatusCode)exception.StatusCode;

        @this.WriteErrorLine(string.IsNullOrEmpty(message) ? $"The request failed with {(int)status} {status}." : message);

        if (!string.IsNullOrEmpty(errorCode))
        {
            @this.WriteErrorLine($"Error code: {errorCode}");
        }

        if (string.Equals(errorCode, "MtlsRequired", StringComparison.Ordinal))
        {
            @this.WriteErrorLine(KeyValueConsoleExtensions.MtlsRequiredHint);
        }

        return KeyValueConsoleExtensions.ExitCodeOf(status);
    }

    // The text with the line-number gutter of WriteJson, without colours, for a reader in a terminal. Every # is shown as
    // #, so a line cannot hold the ##[command] the runner of GitHub Actions finds anywhere in a line; the gutter
    // keeps :: from starting one. --content-only writes the text as it is.
    public static void WriteNumberedText(this IExtendedConsole @this, string text)
    {
        var lines = text.Split('\n');
        var count = lines[^1].Length == 0 ? lines.Length - 1 : lines.Length;
        var width = count.ToString(CultureInfo.InvariantCulture).Length;
        var output = new StringBuilder("\n");

        for (var index = 0; index < count; index++)
        {
            output.Append((index + 1).ToString(CultureInfo.InvariantCulture).PadLeft(width)).Append(" │ ").Append(lines[index].Replace("#", "\\u0023", StringComparison.Ordinal)).Append('\n');
        }

        @this.WriteRaw(output.ToString());
    }

    // UTF-8 without a byte order mark on the physical console until disposed: Console.Out writes in the console's code
    // page otherwise (437 on many Windows machines), which turns every other character into '?'. The code page is set
    // back afterwards, as it belongs to the terminal. Another console, as in the tests, is left alone.
    public static IDisposable UseUtf8Output(this IExtendedConsole @this)
    {
        if (@this.Console is not PhysicalConsole)
        {
            return EncodingScope.None;
        }

        try
        {
            var original = System.Console.OutputEncoding;

            if (original.CodePage == Utf8.CodePage)
            {
                return EncodingScope.None;
            }

            System.Console.OutputEncoding = Utf8;
            return new EncodingScope(original);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // No console to set, for example a process without one: its writers fall back to UTF-8.
            return EncodingScope.None;
        }
    }

    // Control characters, U+2028 and U+2029 become spaces, so a message stays one line and cannot start a workflow
    // command of GitHub Actions on a line of its own. # becomes \u0023, as in a path, so a message cannot hold the
    // ##[command] of the runner's older syntax either, which the runner finds anywhere in a line; a message of an older
    // API, whose paths keep #, is covered too.
    internal static string ToPlainLine(string message)
    {
        var builder = new StringBuilder(message.Length);

        foreach (var character in message)
        {
            if (character == '#')
            {
                builder.Append("\\u0023");
                continue;
            }

            builder.Append(char.IsControl(character) || character is '\u2028' or '\u2029' ? ' ' : character);
        }

        // A message that starts with :: would be a command (::add-mask::, ::stop-commands::); the masks sform writes on
        // purpose go through WriteRaw, never through here.
        var start = 0;

        while (start < builder.Length && char.IsWhiteSpace(builder[start]))
        {
            start++;
        }

        if (start + 1 < builder.Length && builder[start] == ':' && builder[start + 1] == ':')
        {
            builder.Remove(start, 1).Insert(start, "\\u003A");
        }

        return builder.ToString();
    }

    private sealed class EncodingScope : IDisposable
    {
        public static readonly IDisposable None = new EncodingScope(null);

        private readonly Encoding? _original;

        public EncodingScope(Encoding? original)
        {
            _original = original;
        }

        public void Dispose()
        {
            if (_original is null)
            {
                return;
            }

            try
            {
                System.Console.OutputEncoding = _original;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // the output is written; a code page that cannot be set back is left as it is
            }
        }
    }
}

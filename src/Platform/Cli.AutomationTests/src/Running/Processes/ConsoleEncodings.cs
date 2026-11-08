using System.Runtime.InteropServices;
using System.Text;

namespace _42.Platform.Cli.AutomationTests.Running.Processes;

// The encodings of a .NET child's redirected standard streams on Windows. A child started without a window (CreateNoWindow)
// gets a hidden console of its own, whose input and output code page is the system's OEM code page (437 on an en-US
// system, 852 on a Czech one), and .NET writes Console.Out and reads Console.In in that code page also when the streams
// are redirected: sform's "│" of the gutter arrives as the single byte 0xB3, a character the code page lacks as its
// best fit or "?". sform switches its output to UTF-8 only around a formatted payload (config --format/--content-only,
// PlainOutputExtensions.UseUtf8Output), so one run can hold both. Elsewhere (Linux, macOS) everything is UTF-8.
//   - Output: valid UTF-8 is read as UTF-8, every other byte in the OEM code page (ConsoleOutputDecoder).
//   - Input: written in the OEM code page, so the child's Console.In reads what the test sent; a character the code page
//     lacks becomes "?" (a CLI that set its console encodings to UTF-8 when redirected would lose nothing).
internal static partial class ConsoleEncodings
{
    private static readonly Lazy<Encoding> Oem = new(CreateOem);

    public static UTF8Encoding Utf8 { get; } = new(encoderShouldEmitUTF8Identifier: false);

    // The code page of a hidden console on this system: the OEM code page on Windows, UTF-8 elsewhere.
    public static Encoding ChildConsole => OperatingSystem.IsWindows() ? Oem.Value : Utf8;

    // The encoding of a code page, through the code pages provider of .NET (UTF-8 for 65001, and when it is unknown).
    public static Encoding OfCodePage(int codePage)
    {
        if (codePage is 0 or 65001)
        {
            return Utf8;
        }

        try
        {
            return CodePagesEncodingProvider.Instance.GetEncoding(codePage) ?? Encoding.GetEncoding(codePage);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return Utf8;
        }
    }

    private static Encoding CreateOem()
    {
        return OperatingSystem.IsWindows() ? OfCodePage((int)GetOEMCP()) : Utf8;
    }

    [LibraryImport("kernel32.dll")]
    private static partial uint GetOEMCP();
}

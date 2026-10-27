using System.IO;
using System.Linq;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using McMaster.Extensions.CommandLineUtils;
using Moq;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Output;

// The output that programs read: the payload as it is, and each message as one plain line on the error output.
public class PlainOutputExtensionsTests
{
    private readonly StringWriter _out = new();
    private readonly StringWriter _error = new();
    private readonly Mock<IExtendedConsole> _console = new(MockBehavior.Strict);

    public PlainOutputExtensionsTests()
    {
        var inner = new Mock<IConsole>();
        inner.SetupGet(console => console.Out).Returns(_out);
        inner.SetupGet(console => console.Error).Returns(_error);
        _console.SetupGet(console => console.Console).Returns(inner.Object);
    }

    [Fact]
    public void WriteErrorLine_TurnsLineBreaksAndControlCharactersIntoSpaces()
    {
        var separators = new string(new[] { (char)0x2028, (char)0x2029, (char)0x85 });

        _console.Object.WriteErrorLine($"one\r\ntwo\n::add-mask::x\t{separators}end");

        _error.ToString().ShouldBe("one  two ::add-mask::x    end\n");
        _out.ToString().ShouldBeEmpty();
    }

    // The runner reads ##[command] anywhere in a line of the error output; an ignorable character between the two #
    // does not stop its culture-sensitive search, so no # is left at all.
    [Fact]
    public void WriteErrorLine_EscapesEveryHash()
    {
        _console.Object.WriteErrorLine("at $['##[set-output name=target;]prod'] and #\u200B#[error]x");

        _error.ToString().ShouldBe("at $['\\u0023\\u0023[set-output name=target;]prod'] and \\u0023\u200B\\u0023[error]x\n");
        _error.ToString().ShouldNotContain("#");
    }

    [Fact]
    public void WriteRaw_WritesTheTextAsItIs()
    {
        _console.Object.WriteRaw("A='x y'\nB=\"a\\nb\"\n");

        _out.ToString().ShouldBe("A='x y'\nB=\"a\\nb\"\n");
    }

    [Fact]
    public void WriteNumberedText_AlignsTheNumbers_AndKeepsAnEmptyLastLine()
    {
        var text = string.Concat(Enumerable.Range(1, 10).Select(index => $"K{index}={index}\n"));

        _console.Object.WriteNumberedText(text);

        var lines = _out.ToString().Split('\n');
        lines[0].ShouldBeEmpty();
        lines[1].ShouldBe(" 1 │ K1=1");
        lines[10].ShouldBe("10 │ K10=10");
        lines.Length.ShouldBe(12);
    }

    [Fact]
    public void UseUtf8Output_OfAnotherConsole_ChangesNothing()
    {
        using (_console.Object.UseUtf8Output())
        {
            _console.Object.WriteRaw("Žluťoučký kůň\n");
        }

        _out.ToString().ShouldBe("Žluťoučký kůň\n");
    }
}

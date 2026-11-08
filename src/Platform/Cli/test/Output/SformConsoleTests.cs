using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using McMaster.Extensions.CommandLineUtils;
using Moq;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Output;

// Without any console (all three standard streams redirected on Windows), the themed output is plain text at a fixed
// width instead of a crash at Console.BufferWidth; each row is a line without trailing spaces.
public class SformConsoleTests
{
    private const string LongAddress = "https://auth.example.com/device?user_code=BCDF-GHJK&client_id=client_01JAAAAAAAAAAAAAAAAAAAAAAA&state=0123456789";

    private readonly StringWriter _out = new();
    private readonly Dictionary<string, string> _environment = new();

    [Fact]
    public void WriteLine_IsPlainText_WithTheMarginOfTheToolkit()
    {
        var console = Create();

        console.WriteLine("To sign in, open ", "https://auth.example.com/device".ThemedHighlight(console.Theme), " and enter the code ", "BCDF-GHJK".ThemedHighlight(console.Theme), ".");

        _out.ToString().ShouldBe("  To sign in, open https://auth.example.com/device and enter the code BCDF-GHJK." + Environment.NewLine);
    }

    [Fact]
    public void HeaderAndImportantLines_EndWithOneLineBreak()
    {
        var console = Create();

        console.WriteHeader("Machine sign-in");
        console.WriteImportant("No default project is set.");
        console.WriteLine();

        Lines().ShouldBe(["# Machine sign-in", "! No default project is set.", string.Empty]);
    }

    // The default width keeps an address on its line.
    [Fact]
    public void ALongLine_IsNotWrappedAtTheDefaultWidth()
    {
        Create().WriteLine("Or open ", LongAddress, " with the code filled in.");

        Lines().ShouldBe([$"  Or open {LongAddress} with the code filled in."]);
    }

    [Fact]
    public void Columns_SetTheWidth_AndNoLineHasTrailingSpaces()
    {
        _environment["COLUMNS"] = "40";

        Create().WriteLine("The code expires in 10 minute(s). Open the address in a browser and enter the code there.");

        var lines = Lines();
        lines.Count.ShouldBeGreaterThan(2);
        lines.ShouldAllBe(line => line.Length <= 40 && line == line.TrimEnd());
        string.Join(" ", lines.Select(line => line.Trim())).ShouldBe("The code expires in 10 minute(s). Open the address in a browser and enter the code there.");
    }

    [Fact]
    public void Table_HasTheHeadersAndTheCells()
    {
        Create().WriteTable(
            [new[] { "org1.proj1", "Owner" }, new[] { "org1.proj2", "Reader" }],
            row => row,
            headers: new[] { "Access point", "Role" });

        var text = _out.ToString();
        text.ShouldContain("Access point");
        text.ShouldContain("Role");
        Lines().ShouldContain(line => line.Contains("org1.proj2") && line.Contains("Reader"));
        Lines().ShouldAllBe(line => line == line.TrimEnd());
    }

    [Fact]
    public void Write_ContinuesTheLine()
    {
        var console = Create();

        console.Write("You account is not registered, call ", "sform account register".ThemedHighlight(console.Theme));
        console.WriteLine();

        Lines().ShouldBe(["  You account is not registered, call sform account register"]);
    }

    [Theory]
    [InlineData(null, 250)]
    [InlineData("100", 100)]
    [InlineData(" 80 ", 80)]
    [InlineData("5", 20)]
    [InlineData("0", 250)]
    [InlineData("-3", 250)]
    [InlineData("wide", 250)]
    public void Columns_AreReadAsAWidth(string? columns, int expected)
    {
        SformConsole.ReadColumns(name => name == "COLUMNS" ? columns : null).ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void WithoutAConsoleWidth_ThereIsNoConsole(int? width)
    {
        new SformConsole(Mock.Of<IConsole>(), () => width, _ => null).HasConsole.ShouldBeFalse();
    }

    [Fact]
    public void WithAConsoleWidth_TheToolkitRenders()
    {
        new SformConsole(Mock.Of<IConsole>(), () => 120, _ => null).HasConsole.ShouldBeTrue();
    }

    private SformConsole Create()
    {
        var console = new Mock<IConsole>();
        console.SetupGet(c => c.Out).Returns(_out);
        console.SetupGet(c => c.Error).Returns(TextWriter.Null);
        return new SformConsole(console.Object, () => null, name => _environment.GetValueOrDefault(name));
    }

    // The text before the last line break, split into lines.
    private List<string> Lines()
    {
        var text = _out.ToString();
        text.ShouldEndWith(Environment.NewLine);
        return text[..^Environment.NewLine.Length].Split(Environment.NewLine).ToList();
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Commands.KeyValues;
using _42.Platform.Cli.Output;
using _42.Platform.Cli.Services.Projects;
using _42.Platform.Cli.UnitTests.Commands;
using _42.Platform.Cli.UnitTests.Output;
using _42.Platform.Storyteller;
using Alba.CsConsoleFormat;
using Moq;
using Shouldly;
using Xunit;
using Appearance = _42.Platform.Storyteller.ProjectTypeAppearance;

namespace _42.Platform.Cli.UnitTests.Services.Projects;

// How the console output shows a project's types and dates (spec section 7.2): today's text without settings, the
// project's names, colors, icons and date format with them.
public class ProjectDisplayTests
{
    private static readonly DateTimeOffset Instant = new(2026, 3, 5, 9, 7, 0, TimeSpan.Zero);
    private static readonly string[] EnglishMonths = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    // The output of sform before project settings, byte for byte.
    [Fact]
    public void BuiltIn_FormatsTheDatesAsBefore()
    {
        var display = ProjectDisplay.BuiltIn;

        display.FormatDateTime(Instant).ShouldBe(Instant.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
        display.FormatDate(Instant).ShouldBe(Instant.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        display.FormatDay(Instant).ShouldBe($"{Instant:yyyy-MM-dd}");
        AuditConsoleExtensions.FormatTime(Instant).ShouldBe(display.FormatDateTime(Instant));
        AuditConsoleExtensions.FormatDate(Instant).ShouldBe(display.FormatDate(Instant));
        KeyValueTimesFormat(null).ShouldBe("—");
    }

    [Fact]
    public void BuiltIn_HasTheBuiltInNames_AndNoColorsOrIcons()
    {
        var display = ProjectDisplay.BuiltIn;

        display.IsBuiltIn.ShouldBeTrue();
        display.HasTypes.ShouldBeFalse();
        display.Colors.ShouldBe(TypeColorMode.None);
        display.ShowsIcons.ShouldBeFalse();
        display.PluralOf(AnnotationTypeCodes.Subject).ShouldBe("Subjects");
        display.PluralOf(AnnotationTypeCodes.Responsibility).ShouldBe("Responsibilities");
        display.PluralOf(AnnotationTypeCodes.Usage).ShouldBe("Usages");
        display.PluralOf(AnnotationTypeCodes.Context).ShouldBe("Contexts");
        display.PluralOf(AnnotationTypeCodes.Execution).ShouldBe("Executions");
        display.LabelOf(AnnotationTypeCodes.Subject).ShouldBeNull();
        display.NameOr(AnnotationTypeCodes.Unit, "Unit").ShouldBe("Unit");
        display.ColorOf(AnnotationTypeCodes.Subject).ShouldBeNull();
        display.SymbolOf(AnnotationTypeCodes.Subject).ShouldBeNull();
    }

    // A project that set nothing, or only the defaults, shows what BuiltIn shows, even on a console with every capability.
    [Theory]
    [InlineData(null)]
    [InlineData("yyyy-mm-dd")]
    [InlineData("YYYY-MM-DD")]
    [InlineData("a format of a newer catalog")]
    public void WithoutSettings_TheDisplayIsTheBuiltInOne(string? dateFormat)
    {
        var display = ProjectDisplay.Create(new Dictionary<string, Appearance> { ["sbt"] = new() { Label = "  " } }, dateFormat, TypeColorMode.TrueColor, showsIcons: true);

        display.IsBuiltIn.ShouldBeTrue();
        display.Colors.ShouldBe(TypeColorMode.None);
        display.ShowsIcons.ShouldBeFalse();
        display.FormatDateTime(Instant).ShouldBe(ProjectDisplay.BuiltIn.FormatDateTime(Instant));
        Render(display.TreeTag("sbt", "subject", string.Empty)).ShouldBe(Render(ProjectDisplay.BuiltIn.TreeTag("sbt", "subject", string.Empty)));
    }

    [Theory]
    [InlineData("yyyy-mm-dd", "{0:0000}-{1:00}-{2:00}")]
    [InlineData("dd.mm.yyyy", "{2:00}.{1:00}.{0:0000}")]
    [InlineData("dd/mm/yyyy", "{2:00}/{1:00}/{0:0000}")]
    [InlineData("mm/dd/yyyy", "{1:00}/{2:00}/{0:0000}")]
    [InlineData("DD.MM.YYYY", "{2:00}.{1:00}.{0:0000}")]
    public void Dates_FollowTheProjectsFormat_TheTimeStaysAsItIs(string dateFormat, string pattern)
    {
        var display = ProjectDisplay.Create(null, dateFormat, TypeColorMode.Console, showsIcons: false);
        var local = Instant.ToLocalTime();
        var date = string.Format(CultureInfo.InvariantCulture, pattern, local.Year, local.Month, local.Day);

        display.FormatDate(Instant).ShouldBe(date);
        display.FormatDateTime(Instant).ShouldBe($"{date} {local.ToString("HH:mm", CultureInfo.InvariantCulture)}");
    }

    // English months whatever the culture of the machine, and the day without a leading zero.
    [Fact]
    public void Dates_WithMonthNames_AreInEnglish()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("cs-CZ");

        try
        {
            var display = ProjectDisplay.Create(null, "d mmm yyyy", TypeColorMode.Console, showsIcons: false);
            var local = Instant.ToLocalTime();

            display.DateFormat.ShouldBe("d mmm yyyy");
            display.FormatDate(Instant).ShouldBe($"{local.Day} {EnglishMonths[local.Month - 1]} {local.Year}");
            display.FormatDateTime(Instant).ShouldEndWith($" {local.Year} {local.ToString("HH:mm", CultureInfo.InvariantCulture)}");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    // The days of a report are dates, not instants: shown as they are, in the project's format.
    [Fact]
    public void Days_AreNotConvertedToLocalTime()
    {
        var day = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

        ProjectDisplay.Create(null, "dd.mm.yyyy", TypeColorMode.None, false).FormatDay(day).ShouldBe("01.11.2026");
        ProjectDisplay.Create(null, "d mmm yyyy", TypeColorMode.None, false).FormatDay(day).ShouldBe("1 Nov 2026");
    }

    [Fact]
    public void Names_AreTheProjects_PluralsDerivedWhenMissing()
    {
        var display = Customized(TypeColorMode.Console, showsIcons: false);

        display.LabelOf("sbt").ShouldBe("Company");
        display.PluralOf("sbt").ShouldBe("Companies");
        display.LabelOf("exe").ShouldBe("Job");
        display.PluralOf("exe").ShouldBe("Jobs");
        display.PluralOf("rst").ShouldBe("Policies");
        display.LabelOf("cnt").ShouldBeNull();
        display.PluralOf("cnt").ShouldBe("Contexts");
        display.NameOr("cnt", "Context").ShouldBe("Context");
        display.NameOr("uxe", "UnitOfExecution").ShouldBe("UnitOfExecution");
    }

    // Codes in any case; the values trimmed; an entry of an unknown code, or without a value, is no type of the project.
    [Fact]
    public void Types_AreReadAsTheCatalogNamesThem()
    {
        var display = ProjectDisplay.Create(
            new Dictionary<string, Appearance>
            {
                ["SBT"] = new() { Label = " Company " },
                ["xyz"] = new() { Label = "Nothing" },
                ["cnt"] = new(),
            },
            null,
            TypeColorMode.Console,
            showsIcons: true);

        display.HasTypes.ShouldBeTrue();
        display.LabelOf("sbt").ShouldBe("Company");
        display.LabelOf("cnt").ShouldBeNull();
    }

    // Names reach a terminal: escape sequences, line breaks, separators and bidirectional controls are left out, also
    // from settings that an older API or a cache file held.
    [Theory]
    [InlineData("Com\u001b[31mpany", "Com[31mpany")]
    [InlineData("Com\npany", "Company")]
    [InlineData("Com\r\npany", "Company")]
    [InlineData("Com\u2028pany\u2029", "Company")]
    [InlineData("\u202ECompany\u202C", "Company")]
    [InlineData("\u2066Comp\u0085any\u2069", "Company")]
    public void Names_LeaveOutTerminalControls(string label, string shown)
    {
        var display = ProjectDisplay.Create(
            new Dictionary<string, Appearance> { ["sbt"] = new() { Label = label, Plural = label + "s" } },
            null,
            TypeColorMode.None,
            showsIcons: false);

        display.LabelOf("sbt").ShouldBe(shown);
        display.PluralOf("sbt").ShouldBe(shown + "s");
    }

    [Fact]
    public void Name_OfControlsOnly_IsNoName()
    {
        var display = ProjectDisplay.Create(
            new Dictionary<string, Appearance> { ["sbt"] = new() { Label = "\u001b\n\u2028" } },
            null,
            TypeColorMode.None,
            showsIcons: false);

        display.LabelOf("sbt").ShouldBeNull();
    }

    // Every type gets a color and an icon once the project changes one: its own, else the built-in one.
    [Fact]
    public void ColorsAndIcons_AreTheProjects_ElseTheBuiltInOnes()
    {
        var display = Customized(TypeColorMode.Console, showsIcons: true);

        display.ColorOf("sbt").ShouldBe("pink");
        display.SymbolOf("sbt").ShouldBe("\U0001F3E2");
        display.ColorOf("cnt").ShouldBe("cyan");
        display.SymbolOf("cnt").ShouldBe("\U0001F4DA");
        display.ColorOf("rst").ShouldBe("violet");
        display.SymbolOf("rst").ShouldBe("\U0001F3AF");
    }

    [Fact]
    public void Icons_AreLeftOut_WhenTheTerminalDoesNotShowThem_OrTheIconIsUnknown()
    {
        Customized(TypeColorMode.Console, showsIcons: false).SymbolOf("sbt").ShouldBeNull();

        var newer = ProjectDisplay.Create(new Dictionary<string, Appearance> { ["sbt"] = new() { Icon = "i-lucide-of-a-newer-catalog" } }, null, TypeColorMode.Console, true);
        newer.SymbolOf("sbt").ShouldBeNull();
        newer.SymbolOf("rst").ShouldBe("\U0001F3AF");
    }

    [Fact]
    public void TreeTag_BuiltIn_IsTodaysTagInTheLowlightColor()
    {
        var tag = ProjectDisplay.BuiltIn.TreeTag(AnnotationTypeCodes.Subject, "subject", " [disabled]");

        tag.ShouldBeOfType<ConsoleOutputThemedText>();
        var console = Write(tag);
        console.Text.ShouldBe(" [subject] [disabled]");
        console.Segments.ShouldBe([(" [subject] [disabled]", ConsoleColor.DarkGray)]);
    }

    [Fact]
    public void TreeTag_WithSettings_HasTheIconTheNameAndTheNearestColor()
    {
        var console = Write(Customized(TypeColorMode.Console, showsIcons: true).TreeTag("sbt", "subject", " [disabled]"));

        console.Text.ShouldBe(" \U0001F3E2 [Company] [disabled]");
        console.ColorOf("[Company]").ShouldBe(ConsoleColor.Magenta);
        console.ColorOf("[disabled]").ShouldBe(ConsoleColor.DarkGray);
        console.ColorOf("\U0001F3E2").ShouldBe(ConsoleColor.Gray);
    }

    // A type the project does not name keeps today's tag, in its built-in color.
    [Fact]
    public void TreeTag_OfATypeWithoutAName_KeepsTodaysTag()
    {
        var console = Write(Customized(TypeColorMode.Console, showsIcons: false).TreeTag("cnt", "context", string.Empty));

        console.Text.ShouldBe(" [context]");
        console.ColorOf("[context]").ShouldBe(ConsoleColor.Cyan);
    }

    [Fact]
    public void TreeTag_InTrueColor_WritesTheShadeAsEscapeCodes()
    {
        var console = Write(Customized(TypeColorMode.TrueColor, showsIcons: false).TreeTag("sbt", "subject", string.Empty));

        console.Text.ShouldBe(" \u001b[38;2;246;51;154m[Company]\u001b[39m");
        console.Segments.ShouldAllBe(segment => segment.Color == ConsoleColor.Gray);
    }

    // NO_COLOR: the names and icons, and the lowlight color of today's tags.
    [Fact]
    public void TreeTag_WithoutColors_HasTheLowlightColor()
    {
        var console = Write(Customized(TypeColorMode.None, showsIcons: true).TreeTag("sbt", "subject", string.Empty));

        console.Text.ShouldBe(" \U0001F3E2 [Company]");
        console.ColorOf("[Company]").ShouldBe(ConsoleColor.DarkGray);
        console.Text.ShouldNotContain("\u001b");
    }

    [Fact]
    public void TypeCell_BuiltIn_IsTheCellOfTheTableOfTexts()
    {
        var cell = ProjectDisplay.BuiltIn.TypeCell("uxe", "UnitOfExecution");

        RecordingConsole.ToText(cell).ShouldBe("UnitOfExecution");
        cell.Children.Count.ShouldBe(1);
        cell.Stroke.ShouldBe(LineThickness.None);
        cell.Margin.ShouldBe(new Thickness(1, 0));
    }

    [Fact]
    public void TypeCell_WithSettings_HasTheIconAndTheNameInTheNearestColor()
    {
        var cell = Customized(TypeColorMode.Console, showsIcons: true).TypeCell("sbt", "Subject");
        var spans = cell.Children.OfType<Span>().ToList();

        spans.Select(span => span.Text).ShouldBe(["\U0001F3E2 ", "Company"]);
        spans[1].Color.ShouldBe(ConsoleColor.Magenta);

        var plain = Customized(TypeColorMode.None, showsIcons: false).TypeCell("uxe", "UnitOfExecution").Children.OfType<Span>().Single();
        plain.Text.ShouldBe("UnitOfExecution");
        plain.Color.ShouldBeNull();
    }

    // An icon is two UTF-16 code units, the two cells it takes: the columns after it stay aligned.
    [Fact]
    public void TypeCell_WithAnIcon_KeepsTheColumnsAligned()
    {
        var display = Customized(TypeColorMode.Console, showsIcons: true);
        var grid = new Grid { Stroke = LineThickness.None };
        grid.Columns.Add(GridLength.Auto, GridLength.Auto);
        grid.Children.Add(display.TypeCell("sbt", "Subject"), new Cell("end") { Stroke = LineThickness.None });
        grid.Children.Add(display.TypeCell("cnt", "Context"), new Cell("end") { Stroke = LineThickness.None });
        grid.Children.Add(display.TypeCell("exe", "Execution"), new Cell("end") { Stroke = LineThickness.None });

        using var target = new TextRenderTarget(new StringWriter(CultureInfo.InvariantCulture));
        var lines = ConsoleRenderer.RenderDocumentToText(new Document(grid), target, new Rect(0, 0, 80, Size.Infinity))
            .Split('\n')
            .Where(line => line.Trim().Length > 0)
            .ToList();

        lines.Count.ShouldBe(3);
        lines[0].ShouldStartWith(" \U0001F3E2 Company");
        lines.Select(line => line.IndexOf("end", StringComparison.Ordinal)).Distinct().Count().ShouldBe(1);
    }

    internal static ProjectDisplay Customized(TypeColorMode colors, bool showsIcons, string? dateFormat = null)
    {
        return ProjectDisplay.Create(
            new Dictionary<string, Appearance>
            {
                ["sbt"] = new() { Label = "Company", Plural = "Companies", Color = "pink", Icon = "i-lucide-building-2" },
                ["exe"] = new() { Label = "Job" },
                ["rst"] = new() { Label = "Policy" },
                ["cnt"] = new() { Color = "cyan" },
            },
            dateFormat,
            colors,
            showsIcons);
    }

    // What an output writes on a console with the default theme.
    internal static ColorRecordingConsole Write(IConsoleOutput output)
    {
        var inner = new ColorRecordingConsole();
        var console = new Mock<IExtendedConsole>();
        console.SetupGet(target => target.Console).Returns(inner);
        console.SetupGet(target => target.Theme).Returns(new ConsoleTheme
        {
            ForegroundColor = ConsoleColor.Gray,
            HeaderColor = ConsoleColor.White,
            HighlightColor = ConsoleColor.Magenta,
            LowlightColor = ConsoleColor.DarkGray,
            ErrorColor = ConsoleColor.Red,
        });
        output.WriteTo(console.Object);
        return inner;
    }

    private static List<(string Text, ConsoleColor Color)> Render(IConsoleOutput output)
    {
        return [.. Write(output).Segments];
    }

    private static string KeyValueTimesFormat(DateTimeOffset? time)
    {
        return KeyValueTimes.Format(time);
    }
}

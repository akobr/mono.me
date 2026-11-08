using System.Text;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.ConfigurationFormats.UnitTests;

// JsonPaths.Parse and ParsePrefix read the syntax that Property and Item print, so a printed path is valid input.
public class JsonPathsParseTests
{
    // Names the printer treats differently: plain, bracketed, escaped with a backslash, as \n, \r, \t or \uXXXX, # as
    // \u0023, and the characters it keeps (C1 controls, U+2028, U+2029, surrogate pairs, a lone surrogate).
    public static readonly TheoryData<string> Names = new()
    {
        "db",
        "_a-b",
        "a1",
        "a.b",
        "0",
        "0a",
        "-a",
        string.Empty,
        " ",
        "a b",
        "it's",
        "C:\\x",
        "\\'",
        "'",
        "\\",
        "a=b",
        "=",
        "[0]",
        "]",
        "['x']",
        "$",
        "$schema",
        "\"q\"",
        "\u010Das",
        "Microsoft.AspNetCore",
        "a\nb",
        "a\rb",
        "a\tb",
        "a\u0000b",
        "a\u001Bb",
        "a\u007Fb",
        "C#",
        "##[set-output name=target;]prod",
        "x\n::warning::y",
        "a\u0085b",
        "a\u009Bb",
        "a\u2028b",
        "a\u2029b",
        "\U0001F600",
        "\uD800",
        "\uDC00x",
    };

    [Theory]
    [InlineData("db", new[] { "db" })]
    [InlineData("$.db", new[] { "db" })]
    [InlineData("db.port", new[] { "db", "port" })]
    [InlineData("$.db.port", new[] { "db", "port" })]
    [InlineData("_a-b.c_1", new[] { "_a-b", "c_1" })]
    [InlineData("['my.key'].x", new[] { "my.key", "x" })]
    [InlineData("$['my.key'].x", new[] { "my.key", "x" })]
    [InlineData("Logging.LogLevel['Microsoft.AspNetCore']", new[] { "Logging", "LogLevel", "Microsoft.AspNetCore" })]
    [InlineData("['a']['b']", new[] { "a", "b" })]
    [InlineData("['']", new[] { "" })]
    [InlineData("['a=b']", new[] { "a=b" })]
    [InlineData("['a]b[c']", new[] { "a]b[c" })]
    [InlineData("['\"x\"']", new[] { "\"x\"" })]
    [InlineData("['C#']", new[] { "C#" })]
    public void APath_OfNames_IsItsSteps(string path, string[] names)
    {
        JsonPaths.Parse(path).ShouldBe(names.Select(DocumentSegment.Property).ToList());
    }

    [Fact]
    public void AnIndex_IsAnItemStep()
    {
        JsonPaths.Parse("hosts[0]").ShouldBe([DocumentSegment.Property("hosts"), DocumentSegment.Item(0)]);
        JsonPaths.Parse("$.list[1].name").ShouldBe(
            [DocumentSegment.Property("list"), DocumentSegment.Item(1), DocumentSegment.Property("name")]);
        JsonPaths.Parse("m[10][2147483647]").ShouldBe(
            [DocumentSegment.Property("m"), DocumentSegment.Item(10), DocumentSegment.Item(int.MaxValue)]);
    }

    [Fact]
    public void AnIndex_AsTheFirstStep_Parses_TheCallerDecidesWhetherTheRootIsAnArray()
    {
        JsonPaths.Parse("[0]").ShouldBe([DocumentSegment.Item(0)]);
        JsonPaths.Parse("$[0].a").ShouldBe([DocumentSegment.Item(0), DocumentSegment.Property("a")]);
    }

    [Fact]
    public void TheRootAlone_HasNoSteps()
    {
        JsonPaths.Parse(JsonPaths.Root).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("['it\\'s']", "it's")]
    [InlineData("['C:\\\\x']", "C:\\x")]
    [InlineData("['a\\nb']", "a\nb")]
    [InlineData("['a\\rb']", "a\rb")]
    [InlineData("['a\\tb']", "a\tb")]
    [InlineData("['\\u0023']", "#")]
    [InlineData("['\\u001B\\u007f']", "\u001B\u007F")]
    [InlineData("['\\u00e9\\u00C9']", "\u00E9\u00C9")]
    [InlineData("['\\uD83D\\uDE00']", "\U0001F600")]
    public void AnEscape_InAQuotedName_IsReadBack(string path, string name)
    {
        JsonPaths.Parse(path).ShouldBe([DocumentSegment.Property(name)]);
    }

    // Beyond the \' and \\ of the issue: a backslash before n, r or t is an escape, so a literal backslash is always \\.
    [Theory]
    [InlineData("['C:\\temp']", "C:\temp")]
    [InlineData("['C:\\new']", "C:\new")]
    [InlineData("['C:\\\\temp']", "C:\\temp")]
    public void ABackslashBeforeNROrT_IsAnEscape(string path, string name)
    {
        JsonPaths.Parse(path).ShouldBe([DocumentSegment.Property(name)]);
    }

    [Fact]
    public void ANameThatStartsWithTheRoot_IsWrittenInBrackets()
    {
        var exception = Should.Throw<JsonPathFormatException>(() => JsonPaths.ParsePrefix("$schema=x", out _));

        exception.Position.ShouldBe(1);
        exception.Message.ShouldContain("['$name']");
        JsonPaths.Parse("['$schema']").ShouldBe([DocumentSegment.Property("$schema")]);
        JsonPaths.Parse("$['$schema']").ShouldBe([DocumentSegment.Property("$schema")]);
    }

    [Fact]
    public void Of_RefusesANegativeIndex()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => JsonPaths.Of([DocumentSegment.Item(-1)]));
    }

    [Fact]
    public void Of_PrintsASegmentWithANameAsAProperty_WhateverItsIndex()
    {
        JsonPaths.Of([new DocumentSegment("a", 5)]).ShouldBe("$.a");
    }

    [Theory]
    [InlineData("['a\nb']", "a\nb")]
    [InlineData("['a\u0000b']", "a\u0000b")]
    [InlineData("['##[warning]x']", "##[warning]x")]
    public void ACharacterThePrinterEscapes_IsAlsoAcceptedRaw(string path, string name)
    {
        JsonPaths.Parse(path).ShouldBe([DocumentSegment.Property(name)]);
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData(".a", 0)]
    [InlineData("0a", 0)]
    [InlineData("-a", 0)]
    [InlineData(" a", 0)]
    [InlineData("\u010Das", 0)]
    [InlineData("=1", 0)]
    [InlineData("a.", 2)]
    [InlineData("a..b", 2)]
    [InlineData("a.0", 2)]
    [InlineData("a.\u010D", 2)]
    [InlineData("a.['b']", 2)]
    [InlineData("a.[0]", 2)]
    [InlineData("$a", 1)]
    [InlineData("$$", 1)]
    [InlineData("$schema", 1)]
    [InlineData("$_x", 1)]
    [InlineData("$-", 1)]
    [InlineData("$0", 1)]
    [InlineData(".db", 0)]
    [InlineData("a[\u0663]", 2)]
    [InlineData("['C:\\x']", 5)]
    [InlineData("a b", 1)]
    [InlineData("a:b", 1)]
    [InlineData("a[", 2)]
    [InlineData("a[]", 2)]
    [InlineData("a[-1]", 2)]
    [InlineData("a[ 0]", 2)]
    [InlineData("a[\"b\"]", 2)]
    [InlineData("a[b]", 2)]
    [InlineData("a[0", 3)]
    [InlineData("a[0 ]", 3)]
    [InlineData("a[01]", 2)]
    [InlineData("a[00]", 2)]
    [InlineData("a[2147483648]", 2)]
    [InlineData("a[99999999999999999999]", 2)]
    [InlineData("a['b", 4)]
    [InlineData("a['b'", 5)]
    [InlineData("a['b'x]", 5)]
    [InlineData("a['b\\']", 7)]
    [InlineData("a['b\\x']", 5)]
    [InlineData("a['b\\", 5)]
    [InlineData("a['\\u12']", 7)]
    [InlineData("a['\\u12g4']", 7)]
    [InlineData("a['\\U0023']", 4)]
    [InlineData("a['b']c", 6)]
    [InlineData("a['b'].", 7)]
    public void AWrongPath_FailsAtTheCharacterWhereItIsWrong(string path, int position)
    {
        var exception = Should.Throw<JsonPathFormatException>(() => JsonPaths.Parse(path));

        exception.Position.ShouldBe(position);
        exception.ShouldBeAssignableTo<FormatException>();
    }

    [Theory]
    [InlineData("s3cret b")]
    [InlineData("s3cret.")]
    [InlineData("a['s3cret")]
    [InlineData("a['s3\\cret']")]
    [InlineData("a[s3cret]")]
    [InlineData("s3cret[01]")]
    [InlineData("s3cret\n[x]")]
    public void TheMessage_NeverHoldsTheText(string path)
    {
        var exception = Should.Throw<JsonPathFormatException>(() => JsonPaths.Parse(path));

        exception.Message.ShouldNotContain("s3");
        exception.Message.ShouldNotContain("\n");
    }

    [Theory]
    [InlineData("a=1", 1)]
    [InlineData("a.b=1", 3)]
    [InlineData("$=1", 1)]
    [InlineData("hosts[0]=a", 8)]
    [InlineData("['a=b']=c=d", 7)]
    [InlineData("a b", 1)]
    [InlineData("a:b", 1)]
    [InlineData("a", 1)]
    [InlineData("$", 1)]
    public void APrefix_StopsBeforeTheFirstCharacterThatCannotContinueThePath(string text, int length)
    {
        JsonPaths.ParsePrefix(text, out var read);

        read.ShouldBe(length);
    }

    [Fact]
    public void APrefix_KeepsAnEqualsSignOfABracketedName()
    {
        JsonPaths.ParsePrefix("x['a=b']=c=d", out var length).ShouldBe([DocumentSegment.Property("x"), DocumentSegment.Property("a=b")]);
        length.ShouldBe(8);
    }

    [Fact]
    public void APrefix_StillFailsOnAStepThatIsNotComplete()
    {
        Should.Throw<JsonPathFormatException>(() => JsonPaths.ParsePrefix("a.=1", out _)).Position.ShouldBe(2);
        Should.Throw<JsonPathFormatException>(() => JsonPaths.ParsePrefix("a['b=1", out _)).Position.ShouldBe(6);
        Should.Throw<JsonPathFormatException>(() => JsonPaths.ParsePrefix("=1", out _)).Position.ShouldBe(0);
        Should.Throw<JsonPathFormatException>(() => JsonPaths.ParsePrefix("2fa=1", out _)).Position.ShouldBe(0);
        Should.Throw<JsonPathFormatException>(() => JsonPaths.ParsePrefix(string.Empty, out _)).Position.ShouldBe(0);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void APrintedName_ParsesBack(string name)
    {
        var path = JsonPaths.Property(JsonPaths.Root, name);

        JsonPaths.Parse(path).ShouldBe([DocumentSegment.Property(name)]);
        JsonPaths.Of(JsonPaths.Parse(path)).ShouldBe(path);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void APrintedName_ParsesBack_DeepInAPath(string name)
    {
        var path = JsonPaths.Property(JsonPaths.Item(JsonPaths.Property(JsonPaths.Property(JsonPaths.Root, "a"), name), 3), name);

        JsonPaths.Parse(path).ShouldBe(
            [DocumentSegment.Property("a"), DocumentSegment.Property(name), DocumentSegment.Item(3), DocumentSegment.Property(name)]);
    }

    [Fact]
    public void Of_PrintsTheStepsAsPropertyAndItemDo()
    {
        JsonPaths.Of([]).ShouldBe("$");
        JsonPaths.Of([DocumentSegment.Property("Logging"), DocumentSegment.Property("Microsoft.AspNetCore")])
            .ShouldBe("$.Logging['Microsoft.AspNetCore']");
        JsonPaths.Of([DocumentSegment.Property("list"), DocumentSegment.Item(1), DocumentSegment.Property("name")])
            .ShouldBe("$.list[1].name");
    }

    [Theory]
    [InlineData("db.port", "$.db.port")]
    [InlineData("$['db']['port']", "$.db.port")]
    [InlineData("['a\\u0023']", "$['a\\u0023']")]
    [InlineData("['a#']", "$['a\\u0023']")]
    [InlineData("['\\u001b']", "$['\\u001B']")]
    public void AnotherSpelling_PrintsAsTheCanonicalPath(string path, string canonical)
    {
        JsonPaths.Of(JsonPaths.Parse(path)).ShouldBe(canonical);
    }

    // Every path of an error that the conformance suite pins is a valid input, and prints back to itself.
    [Fact]
    public void EveryPathOfTheConformanceSuite_ParsesAndPrintsBack()
    {
        var paths = new[] { ConformanceFiles.Flat, ConformanceFiles.Properties, ConformanceFiles.Env, ConformanceFiles.Formats }
            .SelectMany(file => ConformanceFiles.Read(file).Descendants().OfType<JProperty>())
            .Where(property => property.Name == "path" && property.Value.Type == JTokenType.String && property.Parent is JObject error && error.ContainsKey("rule"))
            .Select(property => property.Value.Value<string>()!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        paths.Count.ShouldBeGreaterThan(10);

        foreach (var path in paths)
        {
            JsonPaths.Of(JsonPaths.Parse(path)).ShouldBe(path);
        }
    }

    // Seeded, so a failure repeats: random steps of every character class the printer treats differently.
    [Fact]
    public void RandomSteps_PrintAndParseBack()
    {
        var random = new Random(424213);
        char[] pool = ['a', 'Z', '_', '-', '0', '9', '.', ' ', '=', '[', ']', '\'', '\\', '"', '$', '#', ':', '\n', '\r', '\t', '\u0000', '\u001F', '\u007F', '\u0085', '\u2028', '\u010D', '\uD83D', '\uDE00'];

        for (var run = 0; run < 2000; run++)
        {
            var steps = new List<DocumentSegment>();
            var count = random.Next(0, 6);

            for (var step = 0; step < count; step++)
            {
                if (random.Next(4) == 0)
                {
                    steps.Add(DocumentSegment.Item(random.Next(3) == 0 ? int.MaxValue - random.Next(3) : random.Next(0, 1000)));
                    continue;
                }

                var name = new StringBuilder();
                var length = random.Next(0, 8);

                for (var character = 0; character < length; character++)
                {
                    name.Append(pool[random.Next(pool.Length)]);
                }

                steps.Add(DocumentSegment.Property(name.ToString()));
            }

            var path = JsonPaths.Of(steps);

            JsonPaths.Parse(path).ShouldBe(steps, customMessage: path);
            JsonPaths.Of(JsonPaths.Parse(path)).ShouldBe(path);
        }
    }
}

namespace _42.Testing.UI.Automation.Reporting.Testiny.UnitTests;

public sealed class TestinyMarkdownTests
{
    [Fact]
    public void Document_StartsWithTheMarkdownHeaderLine()
    {
        TestinyMarkdown.Document("body").ShouldBe("<!--markdown-->\nbody");
    }

    [Fact]
    public void Text_EscapesTheMarkdownCharacters_OnOneLine()
    {
        TestinyMarkdown.Text("a_b *c* [d]\n<e> | `f` #g").ShouldBe("a\\_b \\*c\\* \\[d\\] \\<e\\> \\| \\`f\\` \\#g");
    }

    [Fact]
    public void Fence_IsLongerThanAnyRunOfBackticksInTheContent()
    {
        TestinyMarkdown.Fence("plain").ShouldBe("```text\nplain\n```");
        TestinyMarkdown.Fence("a ```` b\r\n").ShouldBe("`````text\na ```` b\n`````");
    }

    [Theory]
    [InlineData(812, "812 B")]
    [InlineData(34_238, "33.4 KB")]
    [InlineData(26_214_400, "25.0 MB")]
    public void Size_IsReadable(long bytes, string text)
    {
        TestinyMarkdown.Size(bytes).ShouldBe(text);
    }

    [Fact]
    public void Duration_IsInSeconds_OrMinutesAndSeconds()
    {
        TestinyMarkdown.Duration(TimeSpan.FromMilliseconds(840)).ShouldBe("0.84 s");
        TestinyMarkdown.Duration(TimeSpan.FromSeconds(125)).ShouldBe("2 min 5.0 s");
    }
}

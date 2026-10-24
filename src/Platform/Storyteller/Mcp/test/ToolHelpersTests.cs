using System.Text.Json;

using _42.Platform.Storyteller.Mcp.Tools;
using _42.Platform.Storyteller.Mcp.UnitTests.Support;

using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Mcp.UnitTests;

public class ToolHelpersTests
{
    [Fact]
    public void Json_Object_IsTextAndStructuredContent()
    {
        var result = ToolResults.Json(new Sdk.View { Name = "default", IsDefault = true });

        var text = ((ModelContextProtocol.Protocol.TextContentBlock)result.Content.ShouldHaveSingleItem()).Text;
        text.ShouldBe("""{"Name":"default","IsDefault":true,"IsRegistered":false}""");
        result.StructuredContent!.Value.GetProperty("Name").GetString().ShouldBe("default");
        result.IsError.ShouldBeNull();
    }

    [Fact]
    public void Json_Array_IsWrappedInItems()
    {
        var result = ToolResults.Json(new[] { "a", "b" });

        result.StructuredContent!.Value.GetProperty("Items").GetArrayLength().ShouldBe(2);
    }

    [Fact]
    public void Json_Notice_ComesFirstInTheText()
    {
        var result = ToolResults.Json(new JObject { ["AccessKey"] = "2s.key" }, "Copy it now.");

        var text = ((ModelContextProtocol.Protocol.TextContentBlock)result.Content.ShouldHaveSingleItem()).Text;
        text.ShouldStartWith("Copy it now.\n{");
    }

    [Fact]
    public void Error_IsAnErrorWithTheMessage()
    {
        var result = ToolResults.Error("Nope.");

        result.IsError.ShouldBe(true);
        result.StructuredContent!.Value.GetProperty("Message").GetString().ShouldBe("Nope.");
    }

    [Fact]
    public void Error_DetailsWithoutAMessage_GetTheText()
    {
        var result = ToolResults.Error("Nope.", new { Status = 404 });

        result.StructuredContent!.Value.GetProperty("Message").GetString().ShouldBe("Nope.");
        result.StructuredContent.Value.GetProperty("Status").GetInt32().ShouldBe(404);
    }

    [Fact]
    public void Error_DetailsWithTheirOwnMessage_KeepIt_AndTheTextStaysComposed()
    {
        var result = ToolResults.Error("Storyteller API answered 412: Changed. Hint: Read it again.", new JObject { ["Status"] = 412, ["Message"] = "Changed." });

        result.StructuredContent!.Value.GetProperty("Message").GetString().ShouldBe("Changed.");
        result.Text().ShouldBe("Storyteller API answered 412: Changed. Hint: Read it again.");
    }

    [Fact]
    public void Arguments_DocumentKeepsDateStringsAndNumbers()
    {
        using var document = JsonDocument.Parse("""{"at":"2026-10-24T10:00:00Z","n":1.50,"list":[1,"x"]}""");

        var obj = ToolArguments.ToObject(document.RootElement, "content");

        obj["at"]!.Type.ShouldBe(JTokenType.String);
        obj["at"]!.Value<string>().ShouldBe("2026-10-24T10:00:00Z");
        obj["list"]!.Type.ShouldBe(JTokenType.Array);
    }

    [Fact]
    public void Arguments_NotAnObject_IsAnInputError()
    {
        using var document = JsonDocument.Parse("[1,2]");

        Should.Throw<ToolInputException>(() => ToolArguments.ToObject(document.RootElement, "content")).Message.ShouldContain("'content' must be a JSON object");
    }

    [Fact]
    public void HostPage_PagesOneApiPageThenTheNext()
    {
        var api = Enumerable.Range(0, 120).ToList();

        var first = HostPage.Take(api, "api-next", HostPage.Decode(null), 50);
        first.Items.ShouldBe(Enumerable.Range(0, 50));
        first.ContinuationToken.ShouldNotBeNull();

        var cursor = HostPage.Decode(first.ContinuationToken);
        cursor.ApiToken.ShouldBeNull();
        cursor.Offset.ShouldBe(50);

        var third = HostPage.Take(api, "api-next", new HostPage.Cursor(null, 100), 50);
        third.Items.ShouldBe(Enumerable.Range(100, 20));
        HostPage.Decode(third.ContinuationToken).ShouldBe(new HostPage.Cursor("api-next", 0));
    }

    [Fact]
    public void HostPage_LastPage_HasNoToken()
    {
        var page = HostPage.Take(new[] { 1, 2 }, null, HostPage.Decode(null), 50);

        page.ContinuationToken.ShouldBeNull();
        page.Count.ShouldBe(2);
    }

    [Theory]
    [InlineData("not-a-token")]
    [InlineData("eyJBcGlUb2tlbiI6bnVsbCwiT2Zmc2V0IjotMX0")]
    public void HostPage_ForeignToken_IsAnInputError(string token)
    {
        Should.Throw<ToolInputException>(() => HostPage.Decode(token));
    }

    [Theory]
    [InlineData(null, 50)]
    [InlineData(0, 1)]
    [InlineData(500, 200)]
    [InlineData(20, 20)]
    public void HostPage_Limit_IsClamped(int? limit, int expected)
    {
        HostPage.ClampLimit(limit).ShouldBe(expected);
    }
}

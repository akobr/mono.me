using System;
using System.IO.Abstractions;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Json;
using _42.Platform.Cli.Output.Exceptions;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests;

public class JsonPatchDocumentReaderTests
{
    [Fact]
    public async Task TwoOperations_AreReturnedUnchanged()
    {
        const string json = """
            [
              { "op": "test", "path": "/retries", "value": 3, "note": "keep" },
              { "op": "move", "from": "/a", "path": "/b" }
            ]
            """;

        var patch = await ReadAsync(json);

        JToken.DeepEquals(patch, JArray.Parse(json)).ShouldBeTrue();
    }

    [Fact]
    public async Task EmptyArray_IsAccepted()
    {
        var patch = await ReadAsync("[]");

        patch.ShouldBeEmpty();
    }

    [Fact]
    public async Task TrailingWhitespace_IsAccepted()
    {
        var patch = await ReadAsync("[]  \r\n\t");

        patch.ShouldBeEmpty();
    }

    [Fact]
    public async Task SecondJsonValue_IsRejected()
    {
        var exception = await Should.ThrowAsync<WrongInputException>(() => ReadAsync("[] []"));

        exception.Message.ShouldStartWith("Invalid JSON Patch document:");
        exception.Message.ShouldContain("Additional text encountered after finished reading JSON content");
    }

    [Fact]
    public async Task TrailingComment_IsRejected()
    {
        var exception = await Should.ThrowAsync<WrongInputException>(() => ReadAsync("[] // trailing"));

        exception.Message.ShouldStartWith("Invalid JSON Patch document:");
    }

    [Fact]
    public async Task InvalidTextAfterTheArray_IsRejected()
    {
        var exception = await Should.ThrowAsync<WrongInputException>(() => ReadAsync("[] nope"));

        exception.Message.ShouldStartWith("Invalid JSON Patch document:");
        exception.InnerException.ShouldBeOfType<JsonReaderException>();
    }

    [Fact]
    public async Task JsonObject_IsRejected()
    {
        var exception = await Should.ThrowAsync<WrongInputException>(() => ReadAsync("""{ "op": "add", "path": "/a", "value": 1 }"""));

        exception.Message.ShouldBe("The patch file must be a JSON array of operations (RFC 6902).");
    }

    [Fact]
    public async Task JsonScalar_IsRejected()
    {
        var exception = await Should.ThrowAsync<WrongInputException>(() => ReadAsync("1"));

        exception.Message.ShouldBe("The patch file must be a JSON array of operations (RFC 6902).");
    }

    [Fact]
    public async Task InvalidJson_IncludesTheReaderMessage()
    {
        var exception = await Should.ThrowAsync<WrongInputException>(() => ReadAsync("{"));

        exception.Message.ShouldStartWith("Invalid JSON Patch document:");
        exception.InnerException.ShouldBeOfType<JsonReaderException>();
        exception.Message.ShouldContain(exception.InnerException.Message);
    }

    [Fact]
    public async Task MissingOp_NamesTheIndex()
    {
        var exception = await Should.ThrowAsync<WrongInputException>(() => ReadAsync("""[{ "path": "/a" }]"""));

        exception.Message.ShouldContain("0");
        exception.Message.ShouldContain("op");
    }

    [Fact]
    public async Task MissingPath_NamesTheIndex()
    {
        var exception = await Should.ThrowAsync<WrongInputException>(() => ReadAsync("""[{ "op": "remove" }]"""));

        exception.Message.ShouldContain("0");
        exception.Message.ShouldContain("path");
    }

    [Fact]
    public async Task MissingValue_NamesTheIndex()
    {
        var exception = await Should.ThrowAsync<WrongInputException>(() => ReadAsync("""[{ "op": "add", "path": "/a" }]"""));

        exception.Message.ShouldContain("0");
        exception.Message.ShouldContain("value");
    }

    [Fact]
    public async Task MissingFrom_NamesTheIndex()
    {
        var exception = await Should.ThrowAsync<WrongInputException>(() => ReadAsync("""
            [
              { "op": "remove", "path": "/a" },
              { "op": "copy", "path": "/c" }
            ]
            """));

        exception.Message.ShouldContain("1");
        exception.Message.ShouldContain("from");
    }

    [Fact]
    public async Task UpperCaseOp_IsRejected()
    {
        var exception = await Should.ThrowAsync<WrongInputException>(() => ReadAsync("""[{ "op": "Add", "path": "/a", "value": 1 }]"""));

        exception.Message.ShouldContain("0");
        exception.Message.ShouldContain("Add");
    }

    [Fact]
    public async Task Comments_AreIgnored()
    {
        var patch = await ReadAsync("""
            [
              // keep the retries check
              { "op": "test", "path": "/retries", "value": 3 }
            ]
            """);

        patch.Count.ShouldBe(1);
        ((string)patch[0]["op"]!).ShouldBe("test");
        ((string)patch[0]["path"]!).ShouldBe("/retries");
    }

    [Fact]
    public async Task MissingFile_ThrowsWrongInputException()
    {
        var fileSystem = new FileSystem();
        var path = fileSystem.Path.Combine(fileSystem.Path.GetTempPath(), $"sform-patch-missing-{Guid.NewGuid():N}.json");

        var exception = await Should.ThrowAsync<WrongInputException>(() => JsonPatchDocumentReader.ReadAsync(CreateConsole(), fileSystem, path));

        exception.Message.ShouldBe($"The file '{fileSystem.Path.GetFullPath(path)}' does not exist.");
    }

    private static async Task<JArray> ReadAsync(string json)
    {
        var fileSystem = new FileSystem();
        var path = fileSystem.Path.Combine(fileSystem.Path.GetTempPath(), $"sform-patch-{Guid.NewGuid():N}.json");
        await fileSystem.File.WriteAllTextAsync(path, json);

        try
        {
            return await JsonPatchDocumentReader.ReadAsync(CreateConsole(), fileSystem, path);
        }
        finally
        {
            if (fileSystem.File.Exists(path))
            {
                fileSystem.File.Delete(path);
            }
        }
    }

    private static IExtendedConsole CreateConsole()
    {
        var theme = new Mock<IConsoleTheme>();
        theme.Setup(item => item.HighlightColor).Returns(ConsoleColor.Yellow);

        var console = new Mock<IExtendedConsole>();
        console.Setup(item => item.Theme).Returns(theme.Object);
        return console.Object;
    }
}

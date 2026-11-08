using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Cli.Commands;
using _42.Platform.Cli.Commands.Configuration;
using _42.Platform.Cli.Output.Exceptions;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Commands;

// -x|--properties of config set and template set: path=value, written into the built document at the path, in order.
// A wrong input is one plain line on standard error, which names the path but never a value.
public class InlinePropertiesTests
{
    private const string NameHint = "write a name with other characters than ASCII letters, digits, '_' and '-' as ['name']";

    private static readonly string FilePath = MockUnixSupport.Path(@"C:\work\config.json");

    private readonly RecordingConsole _console = new();
    private readonly StringWriter _error = new();
    private readonly MockFileSystem _fileSystem = new(new Dictionary<string, MockFileData>(), MockUnixSupport.Path(@"C:\work"));

    public InlinePropertiesTests()
    {
        var inner = new Mock<IConsole>();
        inner.SetupGet(console => console.Error).Returns(_error);
        _console.Mock.SetupGet(console => console.Console).Returns(inner.Object);
    }

    [Fact]
    public async Task ADottedPath_SetsANestedValue()
    {
        var document = await BuildAsync(null, "a.b=1");

        Json(document).ShouldBe("""{"a":{"b":1}}""");
    }

    [Fact]
    public async Task PathsWithASharedPrefix_MergeIntoOneObject()
    {
        var document = await BuildAsync(null, "db.host=x", "db.port=5432", "name=app");

        Json(document).ShouldBe("""{"db":{"host":"x","port":5432},"name":"app"}""");
    }

    [Fact]
    public async Task AQuotedName_KeepsItsDotsAndItsEqualsSigns()
    {
        var document = await BuildAsync(
            null,
            "Logging.LogLevel['Microsoft.AspNetCore']=Warning",
            "['a.b']=1",
            "['x=y']=c=d",
            "['it\\'s']['C:\\\\x']=ok",
            "['$schema']=https://json-schema.org/draft/2020-12/schema");

        Json(document).ShouldBe(
            """{"Logging":{"LogLevel":{"Microsoft.AspNetCore":"Warning"}},"a.b":1,"x=y":"c=d","it's":{"C:\\x":"ok"},"$schema":"https://json-schema.org/draft/2020-12/schema"}""");
    }

    [Fact]
    public async Task APrintedPath_WithTheRoot_IsTheSamePath()
    {
        var document = await BuildAsync(null, "$.db.port=1", "$['my.key'].x=2", "$['a\\u0023'][0]=3");

        Json(document).ShouldBe("""{"db":{"port":1},"my.key":{"x":2},"a#":[3]}""");
    }

    [Fact]
    public async Task Values_AreTypedAsBefore()
    {
        var document = await BuildAsync(null, "a.bool=true", "a.long=42", "a.double=1.5", "a.text=hello", "a.empty=", "a.null=null");

        document.SelectToken("a.bool")!.Type.ShouldBe(JTokenType.Boolean);
        document.SelectToken("a.long")!.Type.ShouldBe(JTokenType.Integer);
        document.SelectToken("a.double")!.Type.ShouldBe(JTokenType.Float);
        document.SelectToken("a.text")!.Type.ShouldBe(JTokenType.String);
        document.SelectToken("a.empty")!.Value<string>().ShouldBe(string.Empty);
        document.SelectToken("a.null")!.Value<string>().ShouldBe("null");
    }

    // A plain name: an ASCII letter or _, then letters, digits, _ and -.
    [Theory]
    [InlineData("name=value")]
    [InlineData("name=a=b")]
    [InlineData("retries=3")]
    [InlineData("flag=false")]
    [InlineData("rate=0.25")]
    [InlineData("when=2026-11-08")]
    [InlineData("_a-b=x")]
    public async Task APlainName_GivesTheDocumentOfTheOldMerge(string inlineProperty)
    {
        var parts = inlineProperty.Split('=', 2);
        var old = new JObject { [parts[0]] = new JValue(PropertyValueParser.ParseValue(parts[1])) };

        var document = await BuildAsync(null, inlineProperty);

        JToken.DeepEquals(document, old).ShouldBeTrue();
    }

    // These were literal top-level names before; now they need ['name'].
    [Theory]
    [InlineData("$schema=x")]
    [InlineData("my key=x")]
    [InlineData("2fa=true")]
    [InlineData("-x=1")]
    [InlineData("a:b=1")]
    [InlineData("na\u00EFve=1")]
    [InlineData("a/b=1")]
    [InlineData("C#=1")]
    [InlineData("=1")]
    public async Task ANameThatIsNotPlain_IsAWrongInput(string inlineProperty)
    {
        var message = await FailAsync(null, inlineProperty);

        message.ShouldStartWith("The inline property 1 of 1 ");
        message.ShouldContain("['");
    }

    [Fact]
    public async Task Indexes_AppendInCommandLineOrder()
    {
        var document = await BuildAsync(null, "hosts[0]=a", "hosts[1]=b", "list[0].name=x", "list[0].port=1", "list[1].name=y", "m[0][0]=z");

        Json(document).ShouldBe("""{"hosts":["a","b"],"list":[{"name":"x","port":1},{"name":"y"}],"m":[["z"]]}""");
    }

    [Fact]
    public async Task AnIndex_ReplacesAnExistingItem()
    {
        var document = await BuildAsync(null, "hosts[0]=a", "hosts[1]=b", "hosts[0]=c");

        Json(document).ShouldBe("""{"hosts":["c","b"]}""");
    }

    [Fact]
    public async Task InlineValues_AreWrittenOverTheFile()
    {
        AddFile("""{"db":{"host":"file","port":1},"hosts":["a","b"],"list":[{"name":"x","keep":true}],"other":"kept"}""");

        var document = await BuildAsync(FilePath, "db.port=5432", "hosts[0]=c", "hosts[2]=d", "list[0].name=y", "list[1].name=z");

        Json(document).ShouldBe(
            """{"db":{"host":"file","port":5432},"hosts":["c","b","d"],"list":[{"name":"y","keep":true},{"name":"z"}],"other":"kept"}""");
    }

    [Fact]
    public async Task ANullOfTheFile_IsReplacedByTheContainerOfThePath()
    {
        AddFile("""{"a":null,"list":[null]}""");

        var document = await BuildAsync(FilePath, "a.b=1", "list[0].name=x");

        Json(document).ShouldBe("""{"a":{"b":1},"list":[{"name":"x"}]}""");
    }

    [Fact]
    public async Task TheLastStep_ReplacesAnObjectOrAnArray()
    {
        AddFile("""{"a":{"b":1},"hosts":["x"]}""");

        var document = await BuildAsync(FilePath, "a=flat", "hosts=none");

        Json(document).ShouldBe("""{"a":"flat","hosts":"none"}""");
    }

    [Fact]
    public async Task AnIndexPastTheEnd_IsAWrongInput_SoTheReverseOrderFails()
    {
        var message = await FailAsync(null, "hosts[1]=b", "hosts[0]=a");

        message.ShouldBe("The inline property 1 of 2 cannot set $.hosts[1]: $.hosts has 0 items, so its index can be at most 0.");
    }

    [Fact]
    public async Task AnIndexPastTheEndOfTheFilesArray_NamesItsLength()
    {
        AddFile("""{"hosts":["a"]}""");

        var message = await FailAsync(FilePath, "hosts[3]=x");

        message.ShouldBe("The inline property 1 of 1 cannot set $.hosts[3]: $.hosts has 1 item, so its index can be at most 1.");
    }

    [Fact]
    public async Task APathThatCrossesAScalar_IsAWrongInput()
    {
        var message = await FailAsync(null, "a=1", "a.b=2");

        message.ShouldBe("The inline property 2 of 2 cannot set $.a.b: $.a is a number, not an object.");
    }

    [Fact]
    public async Task APathThatCrossesAScalarOfTheFile_IsAWrongInput()
    {
        AddFile("""{"db":{"host":"file"}}""");

        var message = await FailAsync(FilePath, "db.host.name=x");

        message.ShouldBe("The inline property 1 of 1 cannot set $.db.host.name: $.db.host is a string, not an object.");
    }

    [Theory]
    [InlineData("a.b=1", "a[0]=2", "The inline property 2 of 2 cannot set $.a[0]: $.a is an object, not an array.")]
    [InlineData("a[0]=1", "a.b=2", "The inline property 2 of 2 cannot set $.a.b: $.a is an array, not an object.")]
    [InlineData("a[0]=true", "a[0].b=x", "The inline property 2 of 2 cannot set $.a[0].b: $.a[0] is a boolean, not an object.")]
    [InlineData("a[0].b=1", "a[0][0]=x", "The inline property 2 of 2 cannot set $.a[0][0]: $.a[0] is an object, not an array.")]
    [InlineData("a=2026-11-08", "a.b=1", "The inline property 2 of 2 cannot set $.a.b: $.a is a date, not an object.")]
    public async Task AnObjectUsedAsAnArray_OrTheOtherWayRound_IsAWrongInput(string first, string second, string expected)
    {
        var message = await FailAsync(null, first, second);

        message.ShouldBe(expected);
    }

    [Theory]
    [InlineData("""{"a":{"b":1}}""", "a[0]=x", "The inline property 1 of 1 cannot set $.a[0]: $.a is an object, not an array.")]
    [InlineData("""{"a":[1]}""", "a.b=x", "The inline property 1 of 1 cannot set $.a.b: $.a is an array, not an object.")]
    [InlineData("""{"a":[1]}""", "a[0].b=x", "The inline property 1 of 1 cannot set $.a[0].b: $.a[0] is a number, not an object.")]
    public async Task AnObjectOfTheFileUsedAsAnArray_OrTheOtherWayRound_IsAWrongInput(string file, string inlineProperty, string expected)
    {
        AddFile(file);

        var message = await FailAsync(FilePath, inlineProperty);

        message.ShouldBe(expected);
    }

    [Fact]
    public async Task AnIndexAsTheFirstStep_IsAWrongInput()
    {
        var message = await FailAsync(null, "[0]=x");

        message.ShouldBe("The inline property 1 of 1 starts with an array index, $[0], but the document is an object.");
    }

    [Fact]
    public async Task TheRootAlone_IsAWrongInput()
    {
        var message = await FailAsync(null, "$=x");

        message.ShouldBe("The inline property 1 of 1 names no property: its path is $.");
    }

    // What bash and PowerShell pass for "retries['$binding']=jlogic" in double quotes.
    [Fact]
    public async Task AnEmptyName_IsAWrongInput_WithTheHintOfTheShell()
    {
        var message = await FailAsync(null, "retries['']=jlogic");

        message.ShouldBe(
            "The inline property 1 of 1 cannot set $.retries['']: a name is empty, which a configuration cannot hold. A shell may have expanded a $name inside double quotes; write \\$ there, or use single quotes.");
    }

    [Fact]
    public async Task AWrongStep_NamesTheCharacter()
    {
        var message = await FailAsync(null, "ok=1", "a..b=s3cret");

        message.ShouldBe(
            "The inline property 2 of 2 has a wrong path at character 3. After '.', a name of ASCII letters, digits, '_' and '-' that starts with an ASCII letter or '_' is expected; write any other name as ['name'].");
    }

    // What bash and PowerShell pass for an unquoted a['b.c']=1.
    [Fact]
    public async Task ABracketWithoutQuotes_IsAWrongInput_WithTheHintToQuoteTheArgument()
    {
        var message = await FailAsync(null, "a[b.c]=1");

        message.ShouldBe(
            "The inline property 1 of 1 has a wrong path at character 3. After '[', an index (digits) or a quoted name ('name') is expected. Quote the whole argument: a shell removes the quotes of ['name'] otherwise.");
    }

    [Fact]
    public async Task AnIndexWithALeadingZero_HasNoHintOfTheShell()
    {
        var message = await FailAsync(null, "a[01]=1");

        message.ShouldBe("The inline property 1 of 1 has a wrong path at character 3. An index has no leading zero.");
    }

    [Fact]
    public async Task ANameThatStartsWithTheRoot_IsAWrongInput_WithTheBracketForm()
    {
        var message = await FailAsync(null, "$schema=x");

        message.ShouldBe(
            "The inline property 1 of 1 has a wrong path at character 2. After $, '.', '[' or the end of the path is expected; a name that starts with $ is written ['$name'].");
    }

    [Fact]
    public async Task APathWithoutAnEqualsSign_IsAWrongInput()
    {
        var message = await FailAsync(null, "a.b");

        message.ShouldBe($"The inline property 1 of 1 has no '=' after its path, at character 4. Write it as path=value; {NameHint}.");
    }

    [Fact]
    public async Task ASpaceInAName_IsAWrongInput_WithTheHintOfTheBracketForm()
    {
        var message = await FailAsync(null, "my key=s3cret");

        message.ShouldBe($"The inline property 1 of 1 has no '=' after its path, at character 3. Write it as path=value; {NameHint}.");
    }

    [Fact]
    public async Task AColon_IsAWrongInput_WithTheHintOfTheDot()
    {
        var message = await FailAsync(null, "Logging:LogLevel=s3cret");

        message.ShouldBe(
            "The inline property 1 of 1 has no '=' after its path, at character 8. Write it as path=value; ':' does not separate names: write a.b, or ['a:b'] for a name with a colon.");
    }

    [Theory]
    [InlineData("a b c=s3cret")]
    [InlineData("a.b.c")]
    [InlineData("=s3cret")]
    [InlineData("['s3cret=x")]
    [InlineData("a=1\n::warning::s3cret")]
    [InlineData("sk_live_s3cret")]
    [InlineData("s3cret")]
    [InlineData("s3cret+abc/def==")]
    public async Task AMessage_NeverHoldsTheValue(string inlineProperty)
    {
        var exception = await Should.ThrowAsync<WrongInputException>(() => BuildAsync(null, "a=1", inlineProperty, "a.b.c=s3cret"));

        exception.Message.ShouldNotContain("s3");
        exception.Message.ShouldNotContain("\n");
        _error.ToString().ShouldNotContain("s3");
    }

    [Fact]
    public async Task AWrongInput_IsOnePlainLineOnStandardError_AndFailsWithTheExitCodeOfAWrongInput()
    {
        var exception = await Should.ThrowAsync<WrongInputException>(() => BuildAsync(null, "a=1", "a.b=2"));

        exception.ExitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _error.ToString().ShouldBe("The inline property 2 of 2 cannot set $.a.b: $.a is a number, not an object.\n");
        _console.Lines.ShouldBeEmpty();
    }

    // A name the printer keeps raw is written as a space, and a # as \u0023, as every plain line of sform.
    [Fact]
    public async Task APathInTheMessage_StaysOneLineWithoutAHash()
    {
        await Should.ThrowAsync<WrongInputException>(() => BuildAsync(null, "['a\u2028b##[x]']=1", "['a\u2028b##[x]'].c=2"));

        _error.ToString().ShouldBe("The inline property 2 of 2 cannot set $['a b\\u0023\\u0023[x]'].c: $['a b\\u0023\\u0023[x]'] is a number, not an object.\n");
    }

    [Fact]
    public async Task AMissingFile_IsOnePlainLineOnStandardError()
    {
        await Should.ThrowAsync<WrongInputException>(() => BuildAsync(FilePath, "a=1"));

        _error.ToString().ShouldBe($"The file '{FilePath}' does not exist.\n");
        _console.Lines.ShouldBeEmpty();
    }

    [Fact]
    public async Task ConfigSet_SendsTheNestedDocument()
    {
        var fixture = new AnnotationCommandFixture();
        var api = new Mock<IConfigurationsApiClient>(MockBehavior.Strict);
        JObject? sent = null;
        api
            .Setup(client => client.SetConfigurationAsync("org1", "proj1", "default", "exe.app", false, It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, string, bool?, object, CancellationToken>((_, _, _, _, _, body, _) => sent = (JObject)body)
            .ReturnsAsync(new Storyteller.Sdk.Configuration());

        var exitCode = await new ConfigSetCommand(fixture.Console.Object, fixture.Context.Object, api.Object, _fileSystem)
        {
            AnnotationKey = "exe.app",
            InlineProperties = ["db.host=x", "db.port=5432", "Logging.LogLevel['Microsoft.AspNetCore']=Warning"],
        }.OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        Json(sent!).ShouldBe("""{"db":{"host":"x","port":5432},"Logging":{"LogLevel":{"Microsoft.AspNetCore":"Warning"}}}""");
    }

    [Fact]
    public async Task ConfigSet_WithAWrongPath_SendsNothing()
    {
        var fixture = new AnnotationCommandFixture();
        var error = new StringWriter();
        fixture.InnerConsole.SetupGet(console => console.Error).Returns(error);
        var api = new Mock<IConfigurationsApiClient>(MockBehavior.Strict);

        var exitCode = await new ConfigSetCommand(fixture.Console.Object, fixture.Context.Object, api.Object, _fileSystem)
        {
            AnnotationKey = "exe.app",
            InlineProperties = ["a=1", "a.b=2"],
        }.OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        error.ToString().ShouldBe("The inline property 2 of 2 cannot set $.a.b: $.a is a number, not an object.\n");
        api.VerifyNoOtherCalls();
    }

    private static string Json(JToken token)
    {
        return token.ToString(Formatting.None);
    }

    private void AddFile(string content)
    {
        _fileSystem.AddFile(FilePath, new MockFileData(content));
    }

    private Task<JObject> BuildAsync(string? importFilePath, params string[] inlineProperties)
    {
        return JsonInputBuilder.BuildAsync(_console.Object, _fileSystem, importFilePath, inlineProperties);
    }

    // The message of the exception, which is also the one line on standard error.
    private async Task<string> FailAsync(string? importFilePath, params string[] inlineProperties)
    {
        var exception = await Should.ThrowAsync<WrongInputException>(() => BuildAsync(importFilePath, inlineProperties));

        _error.ToString().ShouldBe($"{exception.Message}\n");
        return exception.Message;
    }
}

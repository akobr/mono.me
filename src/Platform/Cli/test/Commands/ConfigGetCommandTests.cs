using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Cli.Commands.Configuration;
using _42.Platform.Storyteller.ConfigurationFormats;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Shouldly;
using Xunit;
using ConfigurationModel = _42.Platform.Storyteller.Sdk.Configuration;

namespace _42.Platform.Cli.UnitTests.Commands;

// sform story config <key>: the model as before, and the content alone in a format or as JSON. The payload goes to the
// inner console's Out, every message of --content-only to its Error, and no value ever reaches the error output. The
// export into a CI job is sform story config export, see ConfigExportCommandTests.
public class ConfigGetCommandTests
{
    private const string Key = "exe.northwind.invoicing.prod";

    // Strings, a number, a boolean, arrays and non-ASCII: every format writes something different.
    private const string Mixed = """{ "db": { "hosts": ["a", "b"], "port": 5432, "password": "p@ss word" }, "features": ["vat"], "ratio": 1.5, "enabled": true, "name": "Žluťoučký kůň" }""";

    private readonly AnnotationCommandFixture _fixture = new();
    private readonly Mock<IConfigurationsApiClient> _api = new(MockBehavior.Strict);
    private readonly MockFileSystem _fileSystem = new(new Dictionary<string, MockFileData>(), P(@"C:\work"));
    private readonly StringWriter _out = new();
    private readonly StringWriter _error = new();

    public ConfigGetCommandTests()
    {
        _fixture.InnerConsole.SetupGet(console => console.Out).Returns(_out);
        _fixture.InnerConsole.SetupGet(console => console.Error).Returns(_error);
    }

    public static TheoryData<string[], string> InvalidCommandLines => new()
    {
        { ["not-a-key", "--content-only"], "Please specify a valid annotation key; 'sform assist key' explains them." },
        { [Key, "-f", "xml", "--content-only", "-e", "app.env"], "Unknown format 'xml'; use json, flat, properties, env or yaml." },
        { [Key, "-r", "--version", "2", "--content-only"], "Cannot specify both --version and --resolved." },
        { [Key, "--env-prefix", "APP_", "-f", "yaml", "--content-only"], "--env-prefix applies to --format env only." },
        { [Key, "-r", "--env-prefix", "APP_", "--content-only"], "--env-prefix applies to --format env only." },
        { [Key, "--env-prefix", "APP_", "-f", "json", "--content-only", "-e", "app.json"], "--env-prefix applies to --format env only." },
        { [Key, "--env-prefix", "1APP", "-f", "env", "--content-only"], "--env-prefix must start with an ASCII letter and hold only ASCII letters, digits and '_', for example APP_." },
        { [Key, "--env-prefix", "APP-", "-f", "env", "--content-only"], "--env-prefix must start with an ASCII letter and hold only ASCII letters, digits and '_', for example APP_." },
    };

    // A name that holds a command of the runner's older syntax, in each message of a read that names a path: an invalid
    // document and the warning of an unresolved binding.
    public static TheoryData<string, string?, string[]> CommandNames => new()
    {
        { """{ "##[set-output name=target;]prod y": "s3cr3t-value" }""", null, ["-f", "env", "--content-only"] },
        { """{ "##[error title=Deploy approved]x": "@(x, vault)" }""", """{ "##[error title=Deploy approved]x": "@(x, vault)" }""", ["-f", "yaml", "--content-only"] },
    };

    // The options of sform story config export, which story config no longer takes.
    public static TheoryData<string[]> RemovedExportOptions => new()
    {
        { ["-r", "--github-env", "--env-prefix", "APP_"] },
        { ["-r", "--mask", "all", "-e", "app.env"] },
        { ["-r", "--mask"] },
        { ["-r", "--allow-unprefixed"] },
        { ["-r", "--allow-unresolved"] },
        { ["-r", "-f", "env", "--prefix", "APP_"] },
    };

    // Each format of the effective, the resolved and a version read, with and without --content-only.
    public static TheoryData<string[], string> Exports
    {
        get
        {
            var exports = new TheoryData<string[], string>();
            string[][] reads = [[], ["-r"], ["--version", "2"]];

            foreach (var read in reads)
            {
                foreach (var format in new[] { "flat", "properties", "env", "yaml" })
                {
                    exports.Add([.. read, "-f", format], format);
                    exports.Add([.. read, "-f", format, "--content-only"], format);
                }
            }

            return exports;
        }
    }

    [Fact]
    public async Task Default_DisplaysTheModel_AndWritesNothingToTheInnerConsole()
    {
        SetupEffective(Envelope(Mixed));

        var exitCode = await Execute(Key);

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _out.ToString().ShouldBeEmpty();
        _error.ToString().ShouldBeEmpty();
        _api.Verify(api => api.GetConfigurationConditionalAsync("org1", "proj1", "default", Key, null, It.IsAny<CancellationToken>()), Times.Once);
        _api.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("flat")]
    [InlineData("properties")]
    [InlineData("env")]
    [InlineData("yaml")]
    public async Task ContentOnly_OfTheEffectiveRead_IsTheTextOfTheFormatter(string format)
    {
        SetupEffective(Envelope(Mixed));

        var exitCode = await Execute(Key, "--format", format, "--content-only");

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _out.ToString().ShouldBe(Formatted(Mixed, format));
        _error.ToString().ShouldBeEmpty();
        ShouldWriteOnlyThroughTheInnerConsole();
    }

    [Theory]
    [InlineData("FLAT")]
    [InlineData("Properties")]
    [InlineData("env")]
    [InlineData("yaml")]
    public async Task ContentOnly_OfTheResolvedRead_IsTheTextOfTheFormatter(string format)
    {
        SetupResolved(Envelope(Mixed));

        var exitCode = await Execute(Key, "-r", "-f", format, "--content-only");

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _out.ToString().ShouldBe(Formatted(Mixed, format));
        _error.ToString().ShouldBeEmpty();
        _api.Verify(api => api.GetResolvedConfigurationConditionalAsync("org1", "proj1", "default", Key, null, It.IsAny<CancellationToken>()), Times.Once);
        _api.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("flat")]
    [InlineData("properties")]
    [InlineData("env")]
    [InlineData("yaml")]
    public async Task ContentOnly_OfAVersion_IsTheTextOfTheFormatter(string format)
    {
        _api.Setup(api => api.GetConfigurationVersionAsync("org1", "proj1", "default", Key, 2)).ReturnsAsync(Envelope(Mixed, version: 2));

        var exitCode = await Execute(Key, "--version", "2", "-f", format, "--content-only");

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _out.ToString().ShouldBe(Formatted(Mixed, format));
        _error.ToString().ShouldBeEmpty();
    }

    // The API formats what it holds, which the SDK reads differently (a date token, a decimal); both format the same.
    [Theory]
    [InlineData("flat")]
    [InlineData("properties")]
    [InlineData("env")]
    [InlineData("yaml")]
    public async Task ContentOnly_WritesTheBytesOfTheApi_ForADateAndADecimal(string format)
    {
        const string stored = """{ "at": "2026-10-20T12:00:00.123+02:00", "price": 1.50 }""";
        var server = JToken.ReadFrom(new JsonTextReader(new StringReader(stored)) { DateParseHandling = DateParseHandling.DateTime, FloatParseHandling = FloatParseHandling.Decimal });
        var envelope = JsonConvert.SerializeObject(new { AnnotationKey = Key, Version = 4, Hash = "h4", Content = server });
        var client = JsonConvert.DeserializeObject<ConfigurationModel>(envelope, new JsonSerializerSettings { DateParseHandling = DateParseHandling.None })!;
        SetupEffective(client);
        ConfigurationFormatNames.TryParse(format, out var parsed).ShouldBeTrue();

        await Execute(Key, "-f", format, "--content-only");

        _out.ToString().ShouldBe(ConfigurationFormatter.Format(server, parsed).Text);
    }

    [Fact]
    public async Task ContentOnly_InJson_IsTheContentAlone_WithTwoSpacesAndLf()
    {
        SetupEffective(Envelope("""{ "a": "x", "n": { "m": [1, true] } }"""));

        var exitCode = await Execute(Key, "--content-only");

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _out.ToString().ShouldBe("{\n  \"a\": \"x\",\n  \"n\": {\n    \"m\": [\n      1,\n      true\n    ]\n  }\n}\n");
        ShouldWriteOnlyThroughTheInnerConsole();
    }

    [Fact]
    public async Task ContentOnly_InJson_OfAConfigurationWithoutContent_IsAnEmptyObject()
    {
        var envelope = Envelope("{}");
        envelope.Content = null;
        SetupEffective(envelope);

        await Execute(Key, "-f", "json", "--content-only");

        _out.ToString().ShouldBe("{}\n");
    }

    [Fact]
    public async Task Format_WithoutContentOnly_PrintsTheTextWithLineNumbers()
    {
        SetupEffective(Envelope("""{ "a": "x", "b": 2 }"""));

        var exitCode = await Execute(Key, "-f", "env");

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _out.ToString().ShouldBe("\n1 │ A=x\n2 │ B=2\n");
        _error.ToString().ShouldBeEmpty();
    }

    // Alba needs a console, which a run in CI does not have; the note is as plain as the text.
    [Fact]
    public async Task Format_OfAConfigurationWithoutValues_SaysSo_WithoutTheConsoleRenderer()
    {
        SetupEffective(Envelope("{}"));

        var exitCode = await Execute(Key, "-f", "env");

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _out.ToString().ShouldBe("\nThe configuration has no values.\n");
        _error.ToString().ShouldBeEmpty();
        ShouldWriteOnlyThroughTheInnerConsole();
    }

    [Fact]
    public async Task Format_WithEnvPrefix_PrefixesEveryName()
    {
        SetupEffective(Envelope("""{ "db": { "host": "db.local" } }"""));

        await Execute(Key, "-f", "env", "--env-prefix", "app_", "--content-only");

        _out.ToString().ShouldBe("APP_DB__HOST=db.local\n");
    }

    [Theory]
    [MemberData(nameof(Exports))]
    public async Task Export_WritesTheSameBytes_WithoutAByteOrderMark_AndNothingToTheOutput(string[] options, string format)
    {
        SetupEffective(Envelope(Mixed));
        SetupResolved(Envelope(Mixed));
        _api.Setup(api => api.GetConfigurationVersionAsync("org1", "proj1", "default", Key, 2)).ReturnsAsync(Envelope(Mixed, version: 2));
        _fileSystem.AddFile(P(@"C:\work\app.conf"), new MockFileData("old"));

        var exitCode = await Execute([Key, "-e", "app.conf", .. options]);

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _fileSystem.File.ReadAllBytes(P(@"C:\work\app.conf")).ShouldBe(Encoding.UTF8.GetBytes(Formatted(Mixed, format)));
        _out.ToString().ShouldBeEmpty();
        _error.ToString().ShouldBeEmpty();
        ShouldWriteOnlyThroughTheInnerConsole();
    }

    [Fact]
    public async Task Export_InJsonWithContentOnly_WritesTheContentAlone()
    {
        SetupEffective(Envelope("""{ "a": "x" }"""));

        await Execute(Key, "--content-only", "-e", "app.json");

        _fileSystem.File.ReadAllText(P(@"C:\work\app.json")).ShouldBe("{\n  \"a\": \"x\"\n}\n");
        _out.ToString().ShouldBeEmpty();
        ShouldWriteOnlyThroughTheInnerConsole();
    }

    [Theory]
    [MemberData(nameof(InvalidCommandLines))]
    public async Task InvalidCommandLine_IsWrongInput_WithItsMessageAlone(string[] args, string message)
    {
        var exitCode = await Execute(args);

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _error.ToString().ShouldBe(message + "\n");
        _out.ToString().ShouldBeEmpty();
        _fileSystem.AllFiles.ShouldBeEmpty();
        ShouldWriteOnlyThroughTheInnerConsole();
        _api.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UnknownFormat_WithoutContentOnly_SaysSoOnTheConsole()
    {
        var exitCode = await Execute(Key, "-f", "xml");

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _fixture.Console.Lines.ShouldBe(["Unknown format 'xml'; use json, flat, properties, env or yaml."]);
        _out.ToString().ShouldBeEmpty();
        _api.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task InvalidKey_WithoutContentOnly_IsValidatedAsElsewhere()
    {
        var exitCode = await Execute("not-a-key");

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _fixture.Console.Lines[0].ShouldBe("! Please specify a valid annotation key.");
        _api.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("--content-only")]
    [InlineData("--content-only", "-e", "app.env")]
    [InlineData]
    public async Task InvalidDocument_IsWrongInput_WithTheRuleAndThePathOnTheErrorOutputAlone(params string[] options)
    {
        SetupEffective(Envelope("""{ "db": { "pass word": "s3cr3t-value" } }"""));

        var exitCode = await Execute([Key, "-f", "env", .. options]);

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _error.ToString().ShouldBe(
            "The configuration cannot be written as env: The name of the property at $.db['pass word'] cannot be part of an environment variable name: use ASCII letters, digits, '.' and '-' with single '_' between them, and start the names of the root with a letter.\n"
            + "Rule: invalid-env-name, at $.db['pass word']\n");
        _error.ToString().ShouldNotContain("s3cr3t-value");
        _out.ToString().ShouldBeEmpty();
        _fileSystem.File.Exists(P(@"C:\work\app.env")).ShouldBeFalse();
        ShouldWriteOnlyThroughTheInnerConsole();
    }

    [Fact]
    public async Task UnprocessableResolvedRead_IsWrongInput_WithTheServersMessageAndCode()
    {
        _api.Setup(api => api.GetResolvedConfigurationConditionalAsync("org1", "proj1", "default", Key, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(AnnotationCommandFixture.Error(422, "binding.evaluation", "The binding at $.db.password cannot be evaluated."));

        var exitCode = await Execute(Key, "-r", "-f", "env", "--content-only");

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _error.ToString().ShouldBe("The binding at $.db.password cannot be evaluated.\nError code: binding.evaluation\n");
        _out.ToString().ShouldBeEmpty();
        ShouldWriteOnlyThroughTheInnerConsole();
    }

    // The runner of GitHub Actions reads ##[command] anywhere in a line of a step's output, the error output too: a name
    // of the configuration never reaches it, in whichever message names its path.
    [Theory]
    [MemberData(nameof(CommandNames))]
    public async Task ANameOfTheConfiguration_NeverStartsAWorkflowCommandOnTheErrorOutput(string resolved, string? effective, string[] options)
    {
        SetupResolved(Envelope(resolved));

        if (effective is not null)
        {
            SetupEffective(Envelope(effective));
        }

        await Execute([Key, "-r", .. options]);

        _error.ToString().ShouldContain("$['\\u0023\\u0023[");
        _error.ToString().ShouldNotContain("#");
    }

    // A message of an API whose paths keep #.
    [Fact]
    public async Task AMessageOfTheApi_NeverStartsAWorkflowCommandOnTheErrorOutput()
    {
        _api.Setup(api => api.GetResolvedConfigurationConditionalAsync("org1", "proj1", "default", Key, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(AnnotationCommandFixture.Error(422, "binding.evaluation", "The binding at $['##[set-output name=target;]prod'] cannot be evaluated."));

        var exitCode = await Execute(Key, "-r", "-f", "env", "--content-only");

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _error.ToString().ShouldBe("The binding at $['\\u0023\\u0023[set-output name=target;]prod'] cannot be evaluated.\nError code: binding.evaluation\n");
    }

    [Fact]
    public async Task UnprocessableResolvedRead_OnTheConsole_IsWrongInput()
    {
        _api.Setup(api => api.GetResolvedConfigurationConditionalAsync("org1", "proj1", "default", Key, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(AnnotationCommandFixture.Error(422, "binding.limit.depth", "The bindings nest deeper than 8 levels."));

        var exitCode = await Execute(Key, "-r");

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _fixture.Console.Lines.ShouldBe(["The bindings nest deeper than 8 levels.", "Error code: binding.limit.depth"]);
    }

    [Fact]
    public async Task NotFound_IsWrongInput()
    {
        _api.Setup(api => api.GetConfigurationConditionalAsync("org1", "proj1", "default", Key, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(AnnotationCommandFixture.Error(404));

        var exitCode = await Execute(Key, "--content-only");

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _error.ToString().ShouldBe($"The configuration for '{Key}' has not been found.\n");
        _out.ToString().ShouldBeEmpty();
    }

    [Fact]
    public async Task OtherApiError_KeepsItsExitCode_OnTheErrorOutput()
    {
        _api.Setup(api => api.GetConfigurationConditionalAsync("org1", "proj1", "default", Key, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(AnnotationCommandFixture.Error(500, "Unexpected", "Unexpected error occurred on the service.\nRetry later."));

        var exitCode = await Execute(Key, "--content-only");

        // a failure of the service, not a crash of the tool (424-215)
        exitCode.ShouldBe(ExitCodes.ERROR_SERVICE_FAILURE);
        _error.ToString().ShouldBe("Unexpected error occurred on the service. Retry later.\nError code: Unexpected\n");
        ShouldWriteOnlyThroughTheInnerConsole();
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public async Task Refusal_IsLeftToProgram(int statusCode)
    {
        _api.Setup(api => api.GetConfigurationConditionalAsync("org1", "proj1", "default", Key, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(AnnotationCommandFixture.Error(statusCode));

        await Should.ThrowAsync<ApiException>(() => Execute(Key, "--content-only"));
        _out.ToString().ShouldBeEmpty();
    }

    [Fact]
    public async Task WithheldSecrets_AreAWarning_WhenPrinting()
    {
        SetupResolved(Envelope("""{ "db": { "host": "db.local" } }"""), withheld: "mtls-required");

        var exitCode = await Execute(Key, "-r", "-f", "env", "--content-only");

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _out.ToString().ShouldBe("DB__HOST=db.local\n");
        _error.ToString().ShouldBe("Warning: the API withheld the secrets of the configuration (mtls-required): the values of secret bindings are their expressions.\n");
    }

    [Fact]
    public async Task UnresolvedBindings_AreAWarningWithThePaths_WhenPrinting()
    {
        const string unresolved = """{ "db": { "host": "db.local", "password": "@(db.password, vault)" } }""";
        SetupResolved(Envelope(unresolved));
        SetupEffective(Envelope(unresolved));

        var exitCode = await Execute(Key, "-r", "-f", "env", "--content-only");

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _out.ToString().ShouldBe(Formatted(unresolved, "env"));
        _error.ToString().ShouldBe("Warning: these values are binding expressions that were not resolved: $.db.password. The caller may lack Secrets.Read.\n");
    }

    [Fact]
    public async Task ResolvedRead_WithoutAtStrings_DoesNotReadTheEffectiveDocument()
    {
        SetupResolved(Envelope("""{ "db": { "host": "db.local", "port": 5432 } }"""));

        await Execute(Key, "-r", "--content-only");

        _api.Verify(api => api.GetResolvedConfigurationConditionalAsync("org1", "proj1", "default", Key, null, It.IsAny<CancellationToken>()), Times.Once);
        _api.VerifyNoOtherCalls();
        _error.ToString().ShouldBeEmpty();
    }

    [Fact]
    public void Options_AreParsed()
    {
        var command = Parse(Key, "-r", "-f", "env", "--content-only", "--env-prefix", "APP_", "-e", "app.env");

        command.AnnotationKey.ShouldBe(Key);
        command.IsResolvedRetrievalRequested.ShouldBeTrue();
        command.Format.ShouldBe("env");
        command.IsContentOnly.ShouldBeTrue();
        command.EnvironmentPrefix.ShouldBe("APP_");
        command.ExportFilePath.ShouldBe("app.env");
        Parse(Key, "--format", "yaml").Format.ShouldBe("yaml");
        Parse(Key, "--version", "3").Version.ShouldBe(3);
        Parse(Key).Format.ShouldBeNull();
    }

    // No hidden alias: the parser refuses them before the command runs, and Program ends the run with -1.
    [Theory]
    [MemberData(nameof(RemovedExportOptions))]
    public void RemovedExportOptions_DoNotParse(string[] options)
    {
        Should.Throw<UnrecognizedCommandParsingException>(() => Parse([Key, .. options]));
        _api.VerifyNoOtherCalls();
    }

    private static string P(string path) => MockUnixSupport.Path(path);

    // As the SDK reads a body: date-like strings stay strings, numbers are doubles.
    private static JToken Document(string json)
    {
        using var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Double };
        return JToken.ReadFrom(reader);
    }

    private static ConfigurationModel Envelope(string json, long version = 4, string hash = "h4")
    {
        return Envelope(Document(json), version, hash);
    }

    private static ConfigurationModel Envelope(JToken content, long version = 4, string hash = "h4")
    {
        return new ConfigurationModel { AnnotationKey = Key, Version = version, Hash = hash, Content = content };
    }

    private static ConditionalResponse<ConfigurationModel> Ok(ConfigurationModel configuration, string? withheld = null)
    {
        var headers = new Dictionary<string, IEnumerable<string>> { ["ETag"] = ["W/\"r.0123456789abcdefghijkl\""] };

        if (withheld is not null)
        {
            headers["X-2S-Secrets-Withheld"] = [withheld];
        }

        return new ConditionalResponse<ConfigurationModel>(200, configuration, headers);
    }

    private static string Formatted(string json, string format)
    {
        ConfigurationFormatNames.TryParse(format, out var parsed).ShouldBeTrue();
        return ConfigurationFormatter.Format(Document(json), parsed).Text;
    }

    // Nothing rendered through Alba, no prompt and no table: the Out and the Error of the inner console alone.
    private void ShouldWriteOnlyThroughTheInnerConsole()
    {
        _fixture.Console.Lines.ShouldBeEmpty();
        _fixture.Console.Tables.ShouldBeEmpty();
        _fixture.Console.Mock.VerifyGet(console => console.Console, Times.AtLeastOnce());
        _fixture.InnerConsole.VerifyGet(console => console.Out, Times.Between(0, int.MaxValue, Moq.Range.Inclusive));
        _fixture.InnerConsole.VerifyGet(console => console.Error, Times.Between(0, int.MaxValue, Moq.Range.Inclusive));
        _fixture.Console.Mock.VerifyNoOtherCalls();
    }

    private void SetupEffective(ConfigurationModel configuration)
    {
        _api.Setup(api => api.GetConfigurationConditionalAsync("org1", "proj1", "default", Key, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok(configuration));
    }

    private void SetupResolved(ConfigurationModel configuration, string? withheld = null)
    {
        _api.Setup(api => api.GetResolvedConfigurationConditionalAsync("org1", "proj1", "default", Key, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok(configuration, withheld));
    }

    // The command that config selects, as the host builds it.
    private ConfigGetCommand Parse(params string[] args)
    {
        var services = new ServiceCollection()
            .AddSingleton(_fixture.Console.Object)
            .AddSingleton(_fixture.Context.Object)
            .AddSingleton(_api.Object)
            .AddSingleton<IFileSystem>(_fileSystem)
            .BuildServiceProvider();
        var app = new CommandLineApplication<ConfigGetCommand>(NullConsole.Singleton);
        app.Conventions.UseDefaultConventions().UseConstructorInjection(services);

        return app.Parse(args).SelectedCommand.ShouldBeOfType<CommandLineApplication<ConfigGetCommand>>().Model;
    }

    private Task<int> Execute(params string[] args)
    {
        return Parse(args).OnExecuteAsync();
    }
}

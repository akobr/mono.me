using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Cli.Commands.Configuration;
using _42.Platform.Cli.GitHubActions;
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

// sform story config <key>: the model as before, the content alone in a format, and the export of the resolved
// configuration into GITHUB_ENV with masks. The payload and the masks go to the inner console's Out, every message of
// those modes to its Error, and no value ever reaches the error output.
public class ConfigGetCommandTests
{
    private const string Key = "exe.northwind.invoicing.prod";
    private const string FirstDelimiter = "ghadelimiter_0123456789abcdef0123456789abcdef";
    private const string SecondDelimiter = "ghadelimiter_fedcba9876543210fedcba9876543210";

    // A machine with Secrets.Read over mTLS: the bindings of host and port resolved, the password from the vault.
    private const string Resolved = """{ "db": { "host": "db.local", "password": "s3cr3t-value", "port": 5432, "pin": "123", "tls": true } }""";
    private const string Effective = """{ "db": { "host": "db.local", "password": "@(db.password, vault)", "port": "@config(\"/port\")", "pin": "@(db.pin, vault)", "tls": "@config(\"/tls\")" } }""";

    // Strings, a number, a boolean, arrays and non-ASCII: every format writes something different.
    private const string Mixed = """{ "db": { "hosts": ["a", "b"], "port": 5432, "password": "p@ss word" }, "features": ["vat"], "ratio": 1.5, "enabled": true, "name": "Žluťoučký kůň" }""";

    private static readonly string GitHubEnv = P(@"C:\runner\_temp\set_env");

    private readonly AnnotationCommandFixture _fixture = new();
    private readonly Mock<IConfigurationsApiClient> _api = new(MockBehavior.Strict);
    private readonly MockFileSystem _fileSystem = new(new Dictionary<string, MockFileData>(), P(@"C:\work"));
    private readonly StringWriter _out = new();
    private readonly StringWriter _error = new();
    private readonly Queue<string> _delimiters = new([FirstDelimiter, SecondDelimiter]);
    private GitHubActionsSettings _gitHubActions;

    public ConfigGetCommandTests()
    {
        _fixture.InnerConsole.SetupGet(console => console.Out).Returns(_out);
        _fixture.InnerConsole.SetupGet(console => console.Error).Returns(_error);
        _fileSystem.AddFile(GitHubEnv, new MockFileData(string.Empty));
        _gitHubActions = GitHubActionsSettings.FromEnvironment(
            name => name == GitHubActionsSettings.ENV ? GitHubEnv : null,
            () => _delimiters.Dequeue());
    }

    public static TheoryData<string[], string> InvalidCommandLines => new()
    {
        { ["not-a-key", "--content-only"], "Please specify a valid annotation key; 'sform assist key' explains them." },
        { [Key, "-f", "xml", "--content-only", "-e", "app.env"], "Unknown format 'xml'; use json, flat, properties, env or yaml." },
        { [Key, "-r", "--mask", "some"], "Unknown mask 'some'; use bound or all." },
        { [Key, "-r", "--version", "2", "--content-only"], "Cannot specify both --version and --resolved." },
        { [Key, "--env-prefix", "APP_", "-f", "yaml", "--content-only"], "--env-prefix applies to --format env and --github-env only." },
        { [Key, "--env-prefix", "1APP", "-f", "env", "--content-only"], "--env-prefix must start with an ASCII letter and hold only ASCII letters, digits and '_', for example APP_." },
        { [Key, "--env-prefix", "APP-", "-f", "env", "--content-only"], "--env-prefix must start with an ASCII letter and hold only ASCII letters, digits and '_', for example APP_." },
        { [Key, "--github-env", "--env-prefix", "APP_"], "--github-env and --mask export the resolved configuration: add --resolved, without --version." },
        { [Key, "--mask", "all", "-e", "app.env"], "--github-env and --mask export the resolved configuration: add --resolved, without --version." },
        { [Key, "--mask", "bound", "--version", "2"], "--github-env and --mask export the resolved configuration: add --resolved, without --version." },
        { [Key, "-r", "--github-env", "--env-prefix", "APP_", "--content-only"], "--content-only with --github-env or --mask needs --export: their standard output is the ::add-mask:: commands." },
        { [Key, "-r", "--mask", "bound", "--content-only"], "--content-only with --github-env or --mask needs --export: their standard output is the ::add-mask:: commands." },
        { [Key, "-r", "--github-env"], "--github-env needs --env-prefix, for example APP_, or --allow-unprefixed: the names come from the configuration and reach every later step of the job." },
        { [Key, "-r", "--allow-unprefixed", "--content-only"], "--allow-unprefixed and --allow-unresolved apply to --github-env and --mask only." },
        { [Key, "-r", "--allow-unresolved", "--content-only", "-f", "env"], "--allow-unprefixed and --allow-unresolved apply to --github-env and --mask only." },
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
        _fileSystem.AllFiles.ShouldBe([GitHubEnv]);
        _fileSystem.File.ReadAllText(GitHubEnv).ShouldBeEmpty();
        ShouldWriteOnlyThroughTheInnerConsole();
        _api.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GitHubEnv_OutsideAJob_IsWrongInputWithoutACall()
    {
        _gitHubActions = GitHubActionsSettings.FromEnvironment(_ => null);

        var exitCode = await Execute(Key, "-r", "--github-env", "--env-prefix", "APP_");

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _error.ToString().ShouldBe("GITHUB_ENV is not set: --github-env runs in a step of a GitHub Actions job.\n");
        _out.ToString().ShouldBeEmpty();
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

        exitCode.ShouldBe(ExitCodes.ERROR_CRASH);
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

    [Theory]
    [InlineData("--github-env", "--env-prefix", "APP_")]
    [InlineData("--mask", "all", "-e", "app.env")]
    public async Task WithheldSecrets_StopAnExport_BeforeAnythingIsWritten(params string[] options)
    {
        SetupResolved(Envelope(Resolved), withheld: "mtls-required");

        var exitCode = await Execute([Key, "-r", .. options]);

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _error.ToString().ShouldBe("The API withheld the secrets of the configuration (mtls-required): the values of secret bindings are their expressions. Nothing was exported. A machine receives secrets over mTLS with its certificate, or from a project that does not require mTLS for secret delivery.\n");
        _out.ToString().ShouldBeEmpty();
        _fileSystem.File.ReadAllText(GitHubEnv).ShouldBeEmpty();
        _fileSystem.File.Exists(P(@"C:\work\app.env")).ShouldBeFalse();
        _api.Verify(api => api.GetConfigurationConditionalAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
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

    [Theory]
    [InlineData("--github-env", "--env-prefix", "APP_")]
    [InlineData("--mask", "bound")]
    public async Task UnresolvedBindings_StopAnExport_NamingThePaths(params string[] options)
    {
        const string unresolved = """{ "db": { "host": "db.local", "password": "@(db.password, vault)" } }""";
        SetupResolved(Envelope(unresolved));
        SetupEffective(Envelope(unresolved));

        var exitCode = await Execute([Key, "-r", .. options]);

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _error.ToString().ShouldBe("These values are binding expressions that were not resolved: $.db.password. The machine may lack Secrets.Read. Nothing was exported; --allow-unresolved exports them as they are.\n");
        _out.ToString().ShouldBeEmpty();
        _fileSystem.File.ReadAllText(GitHubEnv).ShouldBeEmpty();
    }

    [Fact]
    public async Task UnresolvedBindings_WithAllowUnresolved_AreExportedAsTheyAre()
    {
        const string unresolved = """{ "db": { "host": "db.local", "password": "@(db.password, vault)" } }""";
        SetupResolved(Envelope(unresolved));
        SetupEffective(Envelope(unresolved));

        var exitCode = await Execute(Key, "-r", "--github-env", "--env-prefix", "APP_", "--allow-unresolved");

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _out.ToString().ShouldBeEmpty();
        _fileSystem.File.ReadAllText(GitHubEnv).ShouldBe(Heredocs(FirstDelimiter, ("APP_DB__HOST", "db.local"), ("APP_DB__PASSWORD", "@(db.password, vault)")));
        ErrorLines().ShouldBe(
        [
            "Binding expressions that were not resolved, exported as they are: $.db.password.",
            "Exported 2 variable(s) to GITHUB_ENV: APP_DB__HOST, APP_DB__PASSWORD.",
            "Masked 0 value(s) (bound).",
        ]);
    }

    [Fact]
    public async Task GitHubEnv_MasksTheBoundValues_AndAppendsEveryVariableAsAHeredoc()
    {
        SetupResolved(Envelope(Resolved));
        SetupEffective(Envelope(Effective));

        var exitCode = await Execute(Key, "-r", "--github-env", "--env-prefix", "APP_");

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _out.ToString().ShouldBe("::add-mask::s3cr3t-value\n::add-mask::5432\n");
        _fileSystem.File.ReadAllText(GitHubEnv).ShouldBe(Heredocs(
            FirstDelimiter,
            ("APP_DB__HOST", "db.local"),
            ("APP_DB__PASSWORD", "s3cr3t-value"),
            ("APP_DB__PORT", "5432"),
            ("APP_DB__PIN", "123"),
            ("APP_DB__TLS", "true")));
        ErrorLines().ShouldBe(
        [
            "Exported 5 variable(s) to GITHUB_ENV: APP_DB__HOST, APP_DB__PASSWORD, APP_DB__PORT, APP_DB__PIN, APP_DB__TLS.",
            "Masked 2 value(s) (bound).",
            "Not masked, shorter than 4 characters: APP_DB__PIN.",
        ]);
        ShouldNotRevealAValue();
        ShouldWriteOnlyThroughTheInnerConsole();
    }

    [Fact]
    public async Task GitHubEnv_AppendsToWhatTheJobWroteBefore_WithoutAByteOrderMark()
    {
        _fileSystem.File.WriteAllText(GitHubEnv, "EARLIER=1\n");
        SetupResolved(Envelope("""{ "name": "Žluťoučký kůň" }"""));
        SetupEffective(Envelope("""{ "name": "Žluťoučký kůň" }"""));

        await Execute(Key, "-r", "--github-env", "--allow-unprefixed");

        _fileSystem.File.ReadAllBytes(GitHubEnv).ShouldBe(Encoding.UTF8.GetBytes("EARLIER=1\n" + Heredocs(FirstDelimiter, ("NAME", "Žluťoučký kůň"))));
    }

    // The runner would read EARLIER=1APP_NOTE<<… as one variable, and the line of the value as another, unprefixed.
    [Fact]
    public async Task GitHubEnv_StartsOnALineOfItsOwn_WhenTheJobLeftTheLastLineOpen()
    {
        _fileSystem.File.WriteAllText(GitHubEnv, "EARLIER=1");
        SetupResolved(Envelope("""{ "note": "LD_PRELOAD=/tmp/evil.so" }"""));
        SetupEffective(Envelope("""{ "note": "LD_PRELOAD=/tmp/evil.so" }"""));

        var exitCode = await Execute(Key, "-r", "--github-env", "--env-prefix", "APP_");

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _fileSystem.File.ReadAllText(GitHubEnv).ShouldBe("EARLIER=1\n" + Heredocs(FirstDelimiter, ("APP_NOTE", "LD_PRELOAD=/tmp/evil.so")));
    }

    // The runner creates the file; a file that sform creates gets no byte order mark either.
    [Fact]
    public async Task GitHubEnv_ThatDoesNotExistYet_IsWrittenWithoutAByteOrderMark()
    {
        _fileSystem.File.Delete(GitHubEnv);
        SetupResolved(Envelope("""{ "name": "Žluťoučký kůň" }"""));
        SetupEffective(Envelope("""{ "name": "Žluťoučký kůň" }"""));

        var exitCode = await Execute(Key, "-r", "--github-env", "--allow-unprefixed");

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _fileSystem.File.ReadAllBytes(GitHubEnv).ShouldBe(Encoding.UTF8.GetBytes(Heredocs(FirstDelimiter, ("NAME", "Žluťoučký kůň"))));
    }

    [Fact]
    public async Task GitHubEnv_WithMaskAll_MasksEveryStringAndNumber_ButNoBoolean()
    {
        SetupResolved(Envelope(Resolved));
        SetupEffective(Envelope(Effective));

        var exitCode = await Execute(Key, "-r", "--github-env", "--env-prefix", "APP_", "--mask", "all");

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _out.ToString().ShouldBe("::add-mask::db.local\n::add-mask::s3cr3t-value\n::add-mask::5432\n::add-mask::123\n");
        ErrorLines()[1].ShouldBe("Masked 4 value(s) (all).");
    }

    [Fact]
    public async Task GitHubEnv_WritesTheMasksBeforeGitHubEnvTheFileAndAnyMessage()
    {
        string? environmentAtFirstWrite = null;
        bool? fileAtFirstWrite = null;
        string? errorAtFirstWrite = null;
        var output = new FirstWriteProbe(() =>
        {
            environmentAtFirstWrite = _fileSystem.File.ReadAllText(GitHubEnv);
            fileAtFirstWrite = _fileSystem.File.Exists(P(@"C:\work\app.env"));
            errorAtFirstWrite = _error.ToString();
        });
        _fixture.InnerConsole.SetupGet(console => console.Out).Returns(output);
        SetupResolved(Envelope(Resolved));
        SetupEffective(Envelope(Effective));

        var exitCode = await Execute(Key, "-r", "--github-env", "--env-prefix", "APP_", "-e", "app.env", "-f", "env");

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        output.ToString().ShouldStartWith("::add-mask::");
        environmentAtFirstWrite.ShouldBe(string.Empty);
        fileAtFirstWrite.ShouldBe(false);
        errorAtFirstWrite.ShouldBe(string.Empty);
        _fileSystem.File.ReadAllText(GitHubEnv).ShouldNotBeEmpty();
        _fileSystem.File.ReadAllText(P(@"C:\work\app.env")).ShouldBe(Formatted(Resolved, "env", "APP_"));
        ErrorLines()[^1].ShouldBe("Wrote the configuration to app.env.");
    }

    [Fact]
    public async Task GitHubEnv_WithExportInJson_WritesTheContentAlone()
    {
        SetupResolved(Envelope(Resolved));
        SetupEffective(Envelope(Effective));

        await Execute(Key, "-r", "--github-env", "--env-prefix", "APP_", "-e", "app.json", "--content-only");

        _fileSystem.File.ReadAllText(P(@"C:\work\app.json")).ShouldBe(ConfigGetCommand.ToContentJson(Document(Resolved)));
        _out.ToString().ShouldBe("::add-mask::s3cr3t-value\n::add-mask::5432\n");
    }

    [Fact]
    public async Task GitHubEnv_TakesAnotherDelimiter_WhenAValueContainsTheFirst()
    {
        var value = $"before {FirstDelimiter} after";
        SetupResolved(Envelope(new JObject { ["a"] = value }));
        SetupEffective(Envelope(new JObject { ["a"] = "@(a, vault)" }));

        await Execute(Key, "-r", "--github-env", "--allow-unprefixed");

        _fileSystem.File.ReadAllText(GitHubEnv).ShouldBe(Heredocs(SecondDelimiter, ("A", value)));
    }

    [Theory]
    [InlineData("""{ "path": "/tmp", "node_options": "--require x" }""", "--allow-unprefixed", "PATH ($.path), NODE_OPTIONS ($.node_options)")]
    [InlineData("""{ "GITHUB": { "TOKEN": "x" }, "Ld": { "Preload": "x" } }""", "--allow-unprefixed", "GITHUB__TOKEN ($.GITHUB.TOKEN), LD__PRELOAD ($.Ld.Preload)")]
    [InlineData("""{ "db": "x" }""", "--env-prefix=DOTNET_", "DOTNET_DB ($.db)")]
    [InlineData("""{ "Build": { "Props": "x" } }""", "--env-prefix=MSBuild", "MSBUILDBUILD__PROPS ($.Build.Props)")]
    public async Task GitHubEnv_RefusesNamesThatSteerLaterSteps_AndWritesNothing(string document, string option, string names)
    {
        SetupResolved(Envelope(document));

        var exitCode = await Execute(Key, "-r", "--github-env", option);

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        ErrorLines().ShouldBe([$"These names would change how the runner or the tools of later steps run, and are never exported: {names}.", "Nothing was exported."]);
        _out.ToString().ShouldBeEmpty();
        _fileSystem.File.ReadAllText(GitHubEnv).ShouldBeEmpty();
    }

    [Fact]
    public async Task GitHubEnv_WithAPrefix_ExportsAReservedNameUnderThePrefix()
    {
        SetupResolved(Envelope("""{ "path": "/tmp" }"""));
        SetupEffective(Envelope("""{ "path": "/tmp" }"""));

        var exitCode = await Execute(Key, "-r", "--github-env", "--env-prefix", "APP_");

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _fileSystem.File.ReadAllText(GitHubEnv).ShouldBe(Heredocs(FirstDelimiter, ("APP_PATH", "/tmp")));
    }

    [Fact]
    public async Task GitHubEnv_RefusesANul_AndAVariableLongerThanWindowsAllows()
    {
        var document = new JObject
        {
            ["nul"] = "before\0after",
            ["fits"] = new string('x', GitHubEnvironmentFile.MaxVariableLength - "FITS=".Length),
            ["long"] = new string('x', GitHubEnvironmentFile.MaxVariableLength - "LONG=".Length + 1),
        };
        SetupResolved(Envelope(document));

        var exitCode = await Execute(Key, "-r", "--github-env", "--allow-unprefixed");

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        ErrorLines().ShouldBe(
        [
            "These values contain U+0000, which a variable cannot hold: NUL ($.nul).",
            "These variables are longer than 32,767 characters as NAME=value, the limit of Windows: LONG ($.long).",
            "Nothing was exported.",
        ]);
        _out.ToString().ShouldBeEmpty();
        _fileSystem.File.ReadAllText(GitHubEnv).ShouldBeEmpty();
    }

    [Fact]
    public async Task GitHubEnv_RefusesMoreThan512KiB()
    {
        var document = new JObject();

        for (var index = 0; index < 17; index++)
        {
            document[$"v{index}"] = new string('x', 31_000);
        }

        SetupResolved(Envelope(document));

        var exitCode = await Execute(Key, "-r", "--github-env", "--allow-unprefixed");

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        ErrorLines()[0].ShouldBe("The variables take 527,075 bytes as NAME=value lines, more than the 512 KiB sform exports.");
        _fileSystem.File.ReadAllText(GitHubEnv).ShouldBeEmpty();
    }

    [Fact]
    public async Task GitHubEnv_OfADocumentWithoutEnvNames_IsWrongInput_WithTheRuleAndThePath()
    {
        SetupResolved(Envelope("""{ "db": { "pass word": "s3cr3t-value" } }"""));

        var exitCode = await Execute(Key, "-r", "--github-env", "--env-prefix", "APP_");

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        ErrorLines()[1].ShouldBe("Rule: invalid-env-name, at $.db['pass word']");
        _error.ToString().ShouldNotContain("s3cr3t-value");
        _out.ToString().ShouldBeEmpty();
        _fileSystem.File.ReadAllText(GitHubEnv).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(3L, "h4")]
    [InlineData(4L, "h3")]
    public async Task GitHubEnv_WithAnEffectiveDocumentOfAnotherVersion_MasksEveryStringAndNumber(long version, string hash)
    {
        SetupResolved(Envelope(Resolved));
        SetupEffective(Envelope(Effective, version, hash));

        var exitCode = await Execute(Key, "-r", "--github-env", "--env-prefix", "APP_");

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _out.ToString().ShouldBe("::add-mask::db.local\n::add-mask::s3cr3t-value\n::add-mask::5432\n");
        ErrorLines()[0].ShouldBe("Every string and number is masked: the effective and the resolved read returned different versions of the configuration.");
        ErrorLines()[^1].ShouldBe("Not masked, shorter than 4 characters: APP_DB__PIN.");
    }

    [Fact]
    public async Task GitHubEnv_WhenTheEffectiveReadFails_MasksEveryStringAndNumber()
    {
        SetupResolved(Envelope(Resolved));
        _api.Setup(api => api.GetConfigurationConditionalAsync("org1", "proj1", "default", Key, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(AnnotationCommandFixture.Error(500));

        var exitCode = await Execute(Key, "-r", "--github-env", "--env-prefix", "APP_");

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _out.ToString().ShouldBe("::add-mask::db.local\n::add-mask::s3cr3t-value\n::add-mask::5432\n");
        ErrorLines().Take(2).ShouldBe(
        [
            "Unresolved bindings were not checked: the effective read failed with 500.",
            "Every string and number is masked: the effective read failed with 500.",
        ]);
        _fileSystem.File.ReadAllText(GitHubEnv).ShouldNotBeEmpty();
    }

    [Fact]
    public async Task GitHubEnv_WhenTheEffectiveReadTimesOut_MasksEveryStringAndNumber()
    {
        SetupResolved(Envelope(Resolved));
        _api.Setup(api => api.GetConfigurationConditionalAsync("org1", "proj1", "default", Key, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.", new TimeoutException()));

        var exitCode = await Execute(Key, "-r", "--github-env", "--env-prefix", "APP_");

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _out.ToString().ShouldBe("::add-mask::db.local\n::add-mask::s3cr3t-value\n::add-mask::5432\n");
        ErrorLines().Take(2).ShouldBe(
        [
            "Unresolved bindings were not checked: the effective read failed.",
            "Every string and number is masked: the effective read failed.",
        ]);
        _fileSystem.File.ReadAllText(GitHubEnv).ShouldNotBeEmpty();
    }

    [Fact]
    public async Task GitHubEnv_WhenTheEffectiveConfigurationIsGone_IsWrongInput()
    {
        SetupResolved(Envelope(Resolved));
        _api.Setup(api => api.GetConfigurationConditionalAsync("org1", "proj1", "default", Key, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(AnnotationCommandFixture.Error(404));

        var exitCode = await Execute(Key, "-r", "--github-env", "--env-prefix", "APP_");

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _out.ToString().ShouldBeEmpty();
        _fileSystem.File.ReadAllText(GitHubEnv).ShouldBeEmpty();
    }

    [Fact]
    public async Task GitHubEnv_WhenTheEffectiveReadIsRefused_IsLeftToProgram()
    {
        SetupResolved(Envelope(Resolved));
        _api.Setup(api => api.GetConfigurationConditionalAsync("org1", "proj1", "default", Key, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(AnnotationCommandFixture.Error(401));

        await Should.ThrowAsync<ApiException>(() => Execute(Key, "-r", "--github-env", "--env-prefix", "APP_"));
        _out.ToString().ShouldBeEmpty();
        _fileSystem.File.ReadAllText(GitHubEnv).ShouldBeEmpty();
    }

    // A line shorter than four characters is a part of the secret all the same.
    [Theory]
    [InlineData("bound")]
    [InlineData("all")]
    public async Task GitHubEnv_MasksAMultiLineValue_AndEachOfItsLines_WhateverTheirLength(string mask)
    {
        SetupResolved(Envelope("""{ "key": "-----BEGIN KEY-----\nMIIBabc\nend" }"""));
        SetupEffective(Envelope("""{ "key": "@(signing.key, vault)" }"""));

        var exitCode = await Execute(Key, "-r", "--github-env", "--env-prefix", "APP_", "--mask", mask);

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _out.ToString().ShouldBe("::add-mask::-----BEGIN KEY-----%0AMIIBabc%0Aend\n::add-mask::-----BEGIN KEY-----\n::add-mask::MIIBabc\n::add-mask::end\n");
        _fileSystem.File.ReadAllText(GitHubEnv).ShouldBe(Heredocs(FirstDelimiter, ("APP_KEY", "-----BEGIN KEY-----\nMIIBabc\nend")));
        _error.ToString().ShouldNotContain("MIIBabc");
    }

    [Fact]
    public async Task Mask_WithoutGitHubEnv_WritesTheMasksAndTheFile_AndLeavesGitHubEnvAlone()
    {
        SetupResolved(Envelope(Resolved));
        SetupEffective(Envelope(Effective));

        var exitCode = await Execute(Key, "-r", "--mask", "bound", "-e", "app.yaml", "-f", "yaml", "--content-only");

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _out.ToString().ShouldBe("::add-mask::s3cr3t-value\n::add-mask::5432\n");
        _fileSystem.File.ReadAllText(P(@"C:\work\app.yaml")).ShouldBe(Formatted(Resolved, "yaml"));
        _fileSystem.File.ReadAllText(GitHubEnv).ShouldBeEmpty();
        ErrorLines().ShouldBe(
        [
            "Masked 2 value(s) (bound).",
            "Not masked, shorter than 4 characters: $.db.pin.",
            "Wrote the configuration to app.yaml.",
        ]);
        ShouldNotRevealAValue();
        ShouldWriteOnlyThroughTheInnerConsole();
    }

    [Fact]
    public void Options_AreParsed()
    {
        var command = Parse(Key, "-r", "-f", "env", "--content-only", "--env-prefix", "APP_", "--github-env", "--mask", "all", "--allow-unprefixed", "--allow-unresolved", "-e", "app.env");

        command.AnnotationKey.ShouldBe(Key);
        command.IsResolvedRetrievalRequested.ShouldBeTrue();
        command.Format.ShouldBe("env");
        command.IsContentOnly.ShouldBeTrue();
        command.EnvironmentPrefix.ShouldBe("APP_");
        command.IsGitHubEnvRequested.ShouldBeTrue();
        command.Mask.ShouldBe("all");
        command.IsUnprefixedAllowed.ShouldBeTrue();
        command.IsUnresolvedAllowed.ShouldBeTrue();
        command.ExportFilePath.ShouldBe("app.env");
        Parse(Key, "--format", "yaml").Format.ShouldBe("yaml");
        Parse(Key).Format.ShouldBeNull();
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

    private static string Formatted(string json, string format, string? prefix = null)
    {
        ConfigurationFormatNames.TryParse(format, out var parsed).ShouldBeTrue();
        return ConfigurationFormatter.Format(Document(json), parsed, prefix).Text;
    }

    private static string Heredocs(string delimiter, params (string Name, string Value)[] variables)
    {
        return string.Concat(variables.Select(variable => $"{variable.Name}<<{delimiter}\n{variable.Value}\n{delimiter}\n"));
    }

    private IReadOnlyList<string> ErrorLines()
    {
        return _error.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
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

    private void ShouldNotRevealAValue()
    {
        foreach (var value in new[] { "db.local", "s3cr3t-value", "5432", "123" })
        {
            _error.ToString().ShouldNotContain(value);
        }
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
            .AddSingleton(_gitHubActions)
            .BuildServiceProvider();
        var app = new CommandLineApplication<ConfigGetCommand>(NullConsole.Singleton);
        app.Conventions.UseDefaultConventions().UseConstructorInjection(services);

        return app.Parse(args).SelectedCommand.ShouldBeOfType<CommandLineApplication<ConfigGetCommand>>().Model;
    }

    private Task<int> Execute(params string[] args)
    {
        return Parse(args).OnExecuteAsync();
    }

    // Records what the files and the error output held when the command first wrote to the standard output.
    private sealed class FirstWriteProbe : StringWriter
    {
        private readonly Action _onFirstWrite;
        private bool _isWritten;

        public FirstWriteProbe(Action onFirstWrite)
        {
            _onFirstWrite = onFirstWrite;
        }

        public override void Write(char value)
        {
            Probe();
            base.Write(value);
        }

        public override void Write(string? value)
        {
            Probe();
            base.Write(value);
        }

        private void Probe()
        {
            if (!_isWritten)
            {
                _isWritten = true;
                _onFirstWrite();
            }
        }
    }
}

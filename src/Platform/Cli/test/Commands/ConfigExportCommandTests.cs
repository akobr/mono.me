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
using _42.Platform.Cli.Commands.Storyteller;
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

// sform story config export <key>: the resolved configuration into GITHUB_ENV, a file, or both, with masks. The masks
// go to the inner console's Out, every message to its Error, and no value ever reaches the error output.
public class ConfigExportCommandTests
{
    private const string Key = "exe.northwind.invoicing.prod";
    private const string FirstDelimiter = "ghadelimiter_0123456789abcdef0123456789abcdef";
    private const string SecondDelimiter = "ghadelimiter_fedcba9876543210fedcba9876543210";

    // A machine with Secrets.Read over mTLS: the bindings of host and port resolved, the password from the vault.
    private const string Resolved = """{ "db": { "host": "db.local", "password": "s3cr3t-value", "port": 5432, "pin": "123", "tls": true } }""";
    private const string Effective = """{ "db": { "host": "db.local", "password": "@(db.password, vault)", "port": "@config(\"/port\")", "pin": "@(db.pin, vault)", "tls": "@config(\"/tls\")" } }""";

    private static readonly string GitHubEnv = P(@"C:\runner\_temp\set_env");

    private readonly AnnotationCommandFixture _fixture = new();
    private readonly Mock<IConfigurationsApiClient> _api = new(MockBehavior.Strict);
    private readonly MockFileSystem _fileSystem = new(new Dictionary<string, MockFileData>(), P(@"C:\work"));
    private readonly StringWriter _out = new();
    private readonly StringWriter _error = new();
    private readonly Queue<string> _delimiters = new([FirstDelimiter, SecondDelimiter]);
    private GitHubActionsSettings _gitHubActions;

    public ConfigExportCommandTests()
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
        { ["not-a-key", "--mask", "all"], "Please specify a valid annotation key; 'sform assist key' explains them." },
        { [], "Please specify a valid annotation key; 'sform assist key' explains them." },
        { [Key, "-e", "app.env", "-f", "xml"], "Unknown format 'xml'; use env, flat, properties, yaml or json." },
        { [Key, "--mask", "all", "-f", "yaml"], "--format is the format of the --export file: add --export <file>." },
        { [Key, "--github-env", "--prefix", "APP_", "-f", "env"], "--format is the format of the --export file: add --export <file>." },
        { [Key, "--mask", "some"], "Unknown mask 'some'; use bound or all." },
        { [Key], "Nothing to export: add --github-env, --export <file> or both, or --mask alone to print only the masks." },
        { [Key, "--allow-unresolved"], "Nothing to export: add --github-env, --export <file> or both, or --mask alone to print only the masks." },
        { [Key, "--mask", "bound", "--prefix", "APP_"], "--prefix applies to --github-env and to --export in the env format only." },
        { [Key, "-e", "app.yaml", "-f", "yaml", "--prefix", "APP_"], "--prefix applies to --github-env and to --export in the env format only." },
        { [Key, "-e", "app.json", "-f", "json", "--prefix", "APP_"], "--prefix applies to --github-env and to --export in the env format only." },
        { [Key, "--github-env", "--prefix", "1APP"], "--prefix must start with an ASCII letter and hold only ASCII letters, digits and '_', for example APP_." },
        { [Key, "-e", "app.env", "--prefix", "APP-"], "--prefix must start with an ASCII letter and hold only ASCII letters, digits and '_', for example APP_." },
        { [Key, "--github-env"], "--github-env needs --prefix, for example APP_, or --allow-unprefixed: the names come from the configuration and reach every later step of the job." },
        { [Key, "--github-env", "--mask", "all", "-e", "app.env"], "--github-env needs --prefix, for example APP_, or --allow-unprefixed: the names come from the configuration and reach every later step of the job." },
        { [Key, "-e", "app.env", "--allow-unprefixed"], "--allow-unprefixed applies to --github-env only." },
        { [Key, "--mask", "all", "--allow-unprefixed"], "--allow-unprefixed applies to --github-env only." },
    };

    // A name that holds a command of the runner's older syntax, in each message of an export that names a path: an
    // invalid document, a bound value too short to mask, and an unresolved binding as a note.
    public static TheoryData<string, string?, string[]> CommandNames => new()
    {
        { """{ "##[set-output name=target;]prod y": "s3cr3t-value" }""", null, ["--github-env", "--allow-unprefixed"] },
        { """{ "##[set-output name=target;]prod y": "s3cr3t-value" }""", null, ["--mask", "all", "-e", "app.env"] },
        { """{ "##[set-output name=target;]prod": "abc" }""", """{ "##[set-output name=target;]prod": "@(x, vault)" }""", ["--mask", "bound", "-e", "app.yaml", "-f", "yaml"] },
        { """{ "##[error title=Deploy approved]x": "@(x, vault)" }""", """{ "##[error title=Deploy approved]x": "@(x, vault)" }""", ["--mask", "bound", "--allow-unresolved", "-e", "app.yaml", "-f", "yaml"] },
    };

    // The options of a read, which the export implies (--resolved, --content-only) or does not have.
    public static TheoryData<string[]> OptionsOfTheRead => new()
    {
        { ["--mask", "all", "-r"] },
        { ["--mask", "all", "--resolved"] },
        { ["--mask", "all", "--version", "2"] },
        { ["--mask", "all", "-e", "app.env", "--content-only"] },
        { ["-e", "app.env", "--env-prefix", "APP_"] },
    };

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

        var exitCode = await Execute(Key, "--github-env", "--prefix", "APP_");

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _error.ToString().ShouldBe("GITHUB_ENV is not set: --github-env runs in a step of a GitHub Actions job.\n");
        _out.ToString().ShouldBeEmpty();
        _api.VerifyNoOtherCalls();
    }

    // The export always reads the resolved configuration, and the effective one to find what bindings produced.
    [Fact]
    public async Task Export_ReadsTheResolvedAndTheEffectiveConfiguration_AndNoVersion()
    {
        SetupResolved(Envelope(Resolved));
        SetupEffective(Envelope(Effective));

        var exitCode = await Execute(Key, "--mask", "bound");

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _api.Verify(api => api.GetResolvedConfigurationConditionalAsync("org1", "proj1", "default", Key, null, It.IsAny<CancellationToken>()), Times.Once);
        _api.Verify(api => api.GetConfigurationConditionalAsync("org1", "proj1", "default", Key, null, It.IsAny<CancellationToken>()), Times.Once);
        _api.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Mask_Alone_PrintsOnlyTheMasks_AndWritesNoFile()
    {
        SetupResolved(Envelope(Resolved));
        SetupEffective(Envelope(Effective));

        var exitCode = await Execute(Key, "--mask", "bound");

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _out.ToString().ShouldBe("::add-mask::s3cr3t-value\n::add-mask::5432\n");
        _fileSystem.AllFiles.ShouldBe([GitHubEnv]);
        _fileSystem.File.ReadAllText(GitHubEnv).ShouldBeEmpty();
        ErrorLines().ShouldBe(["Masked 2 value(s) (bound).", "Not masked, shorter than 4 characters: $.db.pin."]);
        ShouldNotRevealAValue();
        ShouldWriteOnlyThroughTheInnerConsole();
    }

    // --export alone masks the bound values, and the file is env unless --format says otherwise.
    [Fact]
    public async Task Export_WithoutAFormat_WritesEnv_WithThePrefix_AndMasksTheBoundValues()
    {
        SetupResolved(Envelope(Resolved));
        SetupEffective(Envelope(Effective));
        _fileSystem.AddFile(P(@"C:\work\app.env"), new MockFileData("old"));

        var exitCode = await Execute(Key, "-e", "app.env", "--prefix", "APP_");

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _fileSystem.File.ReadAllBytes(P(@"C:\work\app.env")).ShouldBe(Encoding.UTF8.GetBytes(Formatted(Resolved, "env", "APP_")));
        _out.ToString().ShouldBe("::add-mask::s3cr3t-value\n::add-mask::5432\n");
        _fileSystem.File.ReadAllText(GitHubEnv).ShouldBeEmpty();
        ErrorLines().ShouldBe(
        [
            "Masked 2 value(s) (bound).",
            "Not masked, shorter than 4 characters: $.db.pin.",
            "Wrote the configuration to app.env.",
        ]);
        ShouldNotRevealAValue();
        ShouldWriteOnlyThroughTheInnerConsole();
    }

    [Theory]
    [InlineData("flat")]
    [InlineData("properties")]
    [InlineData("ENV")]
    [InlineData("yaml")]
    public async Task Export_WritesTheTextOfTheFormatter(string format)
    {
        SetupResolved(Envelope(Resolved));
        SetupEffective(Envelope(Effective));

        var exitCode = await Execute(Key, "--mask", "all", "-e", "app.conf", "-f", format);

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _fileSystem.File.ReadAllBytes(P(@"C:\work\app.conf")).ShouldBe(Encoding.UTF8.GetBytes(Formatted(Resolved, format.ToLowerInvariant())));
        ErrorLines()[^1].ShouldBe("Wrote the configuration to app.conf.");
    }

    [Theory]
    [InlineData("json")]
    [InlineData("JSON")]
    public async Task Export_InJson_WritesTheContentAlone(string format)
    {
        SetupResolved(Envelope(Resolved));
        SetupEffective(Envelope(Effective));

        var exitCode = await Execute(Key, "--mask", "all", "-e", "app.json", "-f", format);

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _fileSystem.File.ReadAllText(P(@"C:\work\app.json")).ShouldBe(ConfigurationReads.ToContentJson(Document(Resolved)));
        _out.ToString().ShouldBe("::add-mask::db.local\n::add-mask::s3cr3t-value\n::add-mask::5432\n::add-mask::123\n");
    }

    [Theory]
    [InlineData("--github-env", "--prefix", "APP_")]
    [InlineData("--mask", "all", "-e", "app.env")]
    public async Task WithheldSecrets_StopAnExport_BeforeAnythingIsWritten(params string[] options)
    {
        SetupResolved(Envelope(Resolved), withheld: "mtls-required");

        var exitCode = await Execute([Key, .. options]);

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _error.ToString().ShouldBe("The API withheld the secrets of the configuration (mtls-required): the values of secret bindings are their expressions. Nothing was exported. A machine receives secrets over mTLS with its certificate, or from a project that does not require mTLS for secret delivery.\n");
        _out.ToString().ShouldBeEmpty();
        _fileSystem.File.ReadAllText(GitHubEnv).ShouldBeEmpty();
        _fileSystem.File.Exists(P(@"C:\work\app.env")).ShouldBeFalse();
        _api.Verify(api => api.GetConfigurationConditionalAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("--github-env", "--prefix", "APP_")]
    [InlineData("--mask", "bound")]
    [InlineData("-e", "app.env")]
    public async Task UnresolvedBindings_StopAnExport_NamingThePaths(params string[] options)
    {
        const string unresolved = """{ "db": { "host": "db.local", "password": "@(db.password, vault)" } }""";
        SetupResolved(Envelope(unresolved));
        SetupEffective(Envelope(unresolved));

        var exitCode = await Execute([Key, .. options]);

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _error.ToString().ShouldBe("These values are binding expressions that were not resolved: $.db.password. The machine may lack Secrets.Read. Nothing was exported; --allow-unresolved exports them as they are.\n");
        _out.ToString().ShouldBeEmpty();
        _fileSystem.File.ReadAllText(GitHubEnv).ShouldBeEmpty();
        _fileSystem.File.Exists(P(@"C:\work\app.env")).ShouldBeFalse();
    }

    [Fact]
    public async Task UnresolvedBindings_WithAllowUnresolved_AreExportedAsTheyAre()
    {
        const string unresolved = """{ "db": { "host": "db.local", "password": "@(db.password, vault)" } }""";
        SetupResolved(Envelope(unresolved));
        SetupEffective(Envelope(unresolved));

        var exitCode = await Execute(Key, "--github-env", "--prefix", "APP_", "--allow-unresolved");

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

        var exitCode = await Execute(Key, "--github-env", "--prefix", "APP_");

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
        _fileSystem.AllFiles.ShouldBe([GitHubEnv]);
        ShouldNotRevealAValue();
        ShouldWriteOnlyThroughTheInnerConsole();
    }

    [Fact]
    public async Task GitHubEnv_AppendsToWhatTheJobWroteBefore_WithoutAByteOrderMark()
    {
        _fileSystem.File.WriteAllText(GitHubEnv, "EARLIER=1\n");
        SetupResolved(Envelope("""{ "name": "Žluťoučký kůň" }"""));
        SetupEffective(Envelope("""{ "name": "Žluťoučký kůň" }"""));

        await Execute(Key, "--github-env", "--allow-unprefixed");

        _fileSystem.File.ReadAllBytes(GitHubEnv).ShouldBe(Encoding.UTF8.GetBytes("EARLIER=1\n" + Heredocs(FirstDelimiter, ("NAME", "Žluťoučký kůň"))));
    }

    // The runner would read EARLIER=1APP_NOTE<<… as one variable, and the line of the value as another, unprefixed.
    [Fact]
    public async Task GitHubEnv_StartsOnALineOfItsOwn_WhenTheJobLeftTheLastLineOpen()
    {
        _fileSystem.File.WriteAllText(GitHubEnv, "EARLIER=1");
        SetupResolved(Envelope("""{ "note": "LD_PRELOAD=/tmp/evil.so" }"""));
        SetupEffective(Envelope("""{ "note": "LD_PRELOAD=/tmp/evil.so" }"""));

        var exitCode = await Execute(Key, "--github-env", "--prefix", "APP_");

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

        var exitCode = await Execute(Key, "--github-env", "--allow-unprefixed");

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _fileSystem.File.ReadAllBytes(GitHubEnv).ShouldBe(Encoding.UTF8.GetBytes(Heredocs(FirstDelimiter, ("NAME", "Žluťoučký kůň"))));
    }

    [Fact]
    public async Task GitHubEnv_WithMaskAll_MasksEveryStringAndNumber_ButNoBoolean()
    {
        SetupResolved(Envelope(Resolved));
        SetupEffective(Envelope(Effective));

        var exitCode = await Execute(Key, "--github-env", "--prefix", "APP_", "--mask", "ALL");

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _out.ToString().ShouldBe("::add-mask::db.local\n::add-mask::s3cr3t-value\n::add-mask::5432\n::add-mask::123\n");
        ErrorLines()[1].ShouldBe("Masked 4 value(s) (all).");
    }

    [Theory]
    [InlineData("-e", "app.env", "-f", "env")]
    [InlineData("-e", "app.env")]
    public async Task GitHubEnv_WritesTheMasksBeforeGitHubEnvTheFileAndAnyMessage(params string[] options)
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

        var exitCode = await Execute([Key, "--github-env", "--prefix", "APP_", .. options]);

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

        await Execute(Key, "--github-env", "--prefix", "APP_", "-e", "app.json", "-f", "json");

        _fileSystem.File.ReadAllText(P(@"C:\work\app.json")).ShouldBe(ConfigurationReads.ToContentJson(Document(Resolved)));
        _out.ToString().ShouldBe("::add-mask::s3cr3t-value\n::add-mask::5432\n");
    }

    // With --github-env the prefix also names the variables when the file is in another format.
    [Fact]
    public async Task GitHubEnv_WithAPrefix_AndAYamlFile_PrefixesOnlyTheVariables()
    {
        SetupResolved(Envelope(Resolved));
        SetupEffective(Envelope(Effective));

        var exitCode = await Execute(Key, "--github-env", "--prefix", "APP_", "-e", "app.yaml", "-f", "yaml");

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _fileSystem.File.ReadAllText(P(@"C:\work\app.yaml")).ShouldBe(Formatted(Resolved, "yaml"));
        _fileSystem.File.ReadAllText(GitHubEnv).ShouldStartWith($"APP_DB__HOST<<{FirstDelimiter}\n");
    }

    [Fact]
    public async Task GitHubEnv_TakesAnotherDelimiter_WhenAValueContainsTheFirst()
    {
        var value = $"before {FirstDelimiter} after";
        SetupResolved(Envelope(new JObject { ["a"] = value }));
        SetupEffective(Envelope(new JObject { ["a"] = "@(a, vault)" }));

        await Execute(Key, "--github-env", "--allow-unprefixed");

        _fileSystem.File.ReadAllText(GitHubEnv).ShouldBe(Heredocs(SecondDelimiter, ("A", value)));
    }

    [Theory]
    [InlineData("""{ "path": "/tmp", "node_options": "--require x" }""", "--allow-unprefixed", "PATH ($.path), NODE_OPTIONS ($.node_options)")]
    [InlineData("""{ "GITHUB": { "TOKEN": "x" }, "Ld": { "Preload": "x" } }""", "--allow-unprefixed", "GITHUB__TOKEN ($.GITHUB.TOKEN), LD__PRELOAD ($.Ld.Preload)")]
    [InlineData("""{ "db": "x" }""", "--prefix=DOTNET_", "DOTNET_DB ($.db)")]
    [InlineData("""{ "Build": { "Props": "x" } }""", "--prefix=MSBuild", "MSBUILDBUILD__PROPS ($.Build.Props)")]
    public async Task GitHubEnv_RefusesNamesThatSteerLaterSteps_AndWritesNothing(string document, string option, string names)
    {
        SetupResolved(Envelope(document));

        var exitCode = await Execute(Key, "--github-env", option, "-e", "app.env");

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        ErrorLines().ShouldBe([$"These names would change how the runner or the tools of later steps run, and are never exported: {names}.", "Nothing was exported."]);
        _out.ToString().ShouldBeEmpty();
        _fileSystem.File.ReadAllText(GitHubEnv).ShouldBeEmpty();
        _fileSystem.File.Exists(P(@"C:\work\app.env")).ShouldBeFalse();
    }

    [Fact]
    public async Task GitHubEnv_WithAPrefix_ExportsAReservedNameUnderThePrefix()
    {
        SetupResolved(Envelope("""{ "path": "/tmp" }"""));
        SetupEffective(Envelope("""{ "path": "/tmp" }"""));

        var exitCode = await Execute(Key, "--github-env", "--prefix", "APP_");

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

        var exitCode = await Execute(Key, "--github-env", "--allow-unprefixed");

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

        var exitCode = await Execute(Key, "--github-env", "--allow-unprefixed");

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        ErrorLines()[0].ShouldBe("The variables take 527,075 bytes as NAME=value lines, more than the 512 KiB sform exports.");
        _fileSystem.File.ReadAllText(GitHubEnv).ShouldBeEmpty();
    }

    [Fact]
    public async Task GitHubEnv_OfADocumentWithoutEnvNames_IsWrongInput_WithTheRuleAndThePath()
    {
        SetupResolved(Envelope("""{ "db": { "pass word": "s3cr3t-value" } }"""));

        var exitCode = await Execute(Key, "--github-env", "--prefix", "APP_");

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        ErrorLines()[0].ShouldStartWith("The configuration cannot be written as variables of GITHUB_ENV: ");
        ErrorLines()[1].ShouldBe("Rule: invalid-env-name, at $.db['pass word']");
        _error.ToString().ShouldNotContain("s3cr3t-value");
        _out.ToString().ShouldBeEmpty();
        _fileSystem.File.ReadAllText(GitHubEnv).ShouldBeEmpty();
    }

    // The file is checked before anything is written, as GITHUB_ENV is.
    [Fact]
    public async Task Export_OfADocumentTheFormatCannotWrite_IsWrongInput_AndWritesNothing()
    {
        SetupResolved(Envelope("""{ "db": { "pass word": "s3cr3t-value" } }"""));

        var exitCode = await Execute(Key, "--mask", "all", "-e", "app.env");

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        ErrorLines().ShouldBe(
        [
            "The configuration cannot be written as env: The name of the property at $.db['pass word'] cannot be part of an environment variable name: use ASCII letters, digits, '.' and '-' with single '_' between them, and start the names of the root with a letter.",
            "Rule: invalid-env-name, at $.db['pass word']",
        ]);
        _out.ToString().ShouldBeEmpty();
        _fileSystem.File.Exists(P(@"C:\work\app.env")).ShouldBeFalse();
    }

    [Theory]
    [InlineData(3L, "h4")]
    [InlineData(4L, "h3")]
    public async Task GitHubEnv_WithAnEffectiveDocumentOfAnotherVersion_MasksEveryStringAndNumber(long version, string hash)
    {
        SetupResolved(Envelope(Resolved));
        SetupEffective(Envelope(Effective, version, hash));

        var exitCode = await Execute(Key, "--github-env", "--prefix", "APP_");

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

        var exitCode = await Execute(Key, "--github-env", "--prefix", "APP_");

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

        var exitCode = await Execute(Key, "--github-env", "--prefix", "APP_");

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _out.ToString().ShouldBe("::add-mask::db.local\n::add-mask::s3cr3t-value\n::add-mask::5432\n");
        ErrorLines().Take(2).ShouldBe(
        [
            "Unresolved bindings were not checked: the effective read failed.",
            "Every string and number is masked: the effective read failed.",
        ]);
        _fileSystem.File.ReadAllText(GitHubEnv).ShouldNotBeEmpty();
    }

    // Under all, every string and number is masked anyway: no note says so.
    [Fact]
    public async Task MaskAll_WhenTheEffectiveReadFails_OnlyNotesTheUncheckedBindings()
    {
        SetupResolved(Envelope(Resolved));
        _api.Setup(api => api.GetConfigurationConditionalAsync("org1", "proj1", "default", Key, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(AnnotationCommandFixture.Error(503));

        var exitCode = await Execute(Key, "--mask", "all");

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        ErrorLines().ShouldBe(["Unresolved bindings were not checked: the effective read failed with 503.", "Masked 4 value(s) (all)."]);
    }

    [Fact]
    public async Task GitHubEnv_WhenTheEffectiveConfigurationIsGone_IsWrongInput()
    {
        SetupResolved(Envelope(Resolved));
        _api.Setup(api => api.GetConfigurationConditionalAsync("org1", "proj1", "default", Key, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(AnnotationCommandFixture.Error(404));

        var exitCode = await Execute(Key, "--github-env", "--prefix", "APP_");

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _error.ToString().ShouldBe($"The configuration for '{Key}' has not been found.\n");
        _out.ToString().ShouldBeEmpty();
        _fileSystem.File.ReadAllText(GitHubEnv).ShouldBeEmpty();
    }

    [Fact]
    public async Task GitHubEnv_WhenTheEffectiveReadIsRefused_IsLeftToProgram()
    {
        SetupResolved(Envelope(Resolved));
        _api.Setup(api => api.GetConfigurationConditionalAsync("org1", "proj1", "default", Key, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(AnnotationCommandFixture.Error(401));

        await Should.ThrowAsync<ApiException>(() => Execute(Key, "--github-env", "--prefix", "APP_"));
        _out.ToString().ShouldBeEmpty();
        _fileSystem.File.ReadAllText(GitHubEnv).ShouldBeEmpty();
    }

    [Fact]
    public async Task ResolvedReadNotFound_IsWrongInput_OnTheErrorOutput()
    {
        _api.Setup(api => api.GetResolvedConfigurationConditionalAsync("org1", "proj1", "default", Key, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(AnnotationCommandFixture.Error(404));

        var exitCode = await Execute(Key, "--github-env", "--prefix", "APP_");

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _error.ToString().ShouldBe($"The configuration for '{Key}' has not been found.\n");
        _out.ToString().ShouldBeEmpty();
        _fileSystem.File.ReadAllText(GitHubEnv).ShouldBeEmpty();
        ShouldWriteOnlyThroughTheInnerConsole();
    }

    [Fact]
    public async Task UnprocessableResolvedRead_IsWrongInput_WithTheServersMessageAndCode()
    {
        _api.Setup(api => api.GetResolvedConfigurationConditionalAsync("org1", "proj1", "default", Key, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(AnnotationCommandFixture.Error(422, "binding.evaluation", "The binding at $['##[set-output name=target;]prod'] cannot be evaluated."));

        var exitCode = await Execute(Key, "--mask", "all", "-e", "app.env");

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _error.ToString().ShouldBe("The binding at $['\\u0023\\u0023[set-output name=target;]prod'] cannot be evaluated.\nError code: binding.evaluation\n");
        _out.ToString().ShouldBeEmpty();
        _fileSystem.File.Exists(P(@"C:\work\app.env")).ShouldBeFalse();
        ShouldWriteOnlyThroughTheInnerConsole();
    }

    [Fact]
    public async Task OtherApiError_OfTheResolvedRead_KeepsItsExitCode_OnTheErrorOutput()
    {
        _api.Setup(api => api.GetResolvedConfigurationConditionalAsync("org1", "proj1", "default", Key, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(AnnotationCommandFixture.Error(500, "Unexpected", "Unexpected error occurred on the service.\nRetry later."));

        var exitCode = await Execute(Key, "--github-env", "--prefix", "APP_");

        exitCode.ShouldBe(ExitCodes.ERROR_CRASH);
        _error.ToString().ShouldBe("Unexpected error occurred on the service. Retry later.\nError code: Unexpected\n");
        _out.ToString().ShouldBeEmpty();
        _fileSystem.File.ReadAllText(GitHubEnv).ShouldBeEmpty();
        ShouldWriteOnlyThroughTheInnerConsole();
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public async Task RefusedResolvedRead_IsLeftToProgram(int statusCode)
    {
        _api.Setup(api => api.GetResolvedConfigurationConditionalAsync("org1", "proj1", "default", Key, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(AnnotationCommandFixture.Error(statusCode));

        await Should.ThrowAsync<ApiException>(() => Execute(Key, "--mask", "all"));
        _out.ToString().ShouldBeEmpty();
    }

    // A line shorter than four characters is a part of the secret all the same.
    [Theory]
    [InlineData("bound")]
    [InlineData("all")]
    public async Task GitHubEnv_MasksAMultiLineValue_AndEachOfItsLines_WhateverTheirLength(string mask)
    {
        SetupResolved(Envelope("""{ "key": "-----BEGIN KEY-----\nMIIBabc\nend" }"""));
        SetupEffective(Envelope("""{ "key": "@(signing.key, vault)" }"""));

        var exitCode = await Execute(Key, "--github-env", "--prefix", "APP_", "--mask", mask);

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

        var exitCode = await Execute(Key, "--mask", "bound", "-e", "app.yaml", "-f", "yaml");

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

    // properties writes a non-ASCII character as an escape, which none of the runner's own encodings of a mask produces.
    [Fact]
    public async Task Mask_WithAPropertiesFile_MasksTheValueAsTheFileWritesIt()
    {
        SetupResolved(Envelope("""{ "db": { "password": "Pässwort-2026" } }"""));
        SetupEffective(Envelope("""{ "db": { "password": "@(db.password, vault)" } }"""));

        var exitCode = await Execute(Key, "--mask", "bound", "-f", "properties", "-e", "app.properties");

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _fileSystem.File.ReadAllText(P(@"C:\work\app.properties")).ShouldBe("db.password=P\\u00E4sswort-2026\n");
        _out.ToString().ShouldBe("::add-mask::Pässwort-2026\n::add-mask::P\\u00E4sswort-2026\n");
        ErrorLines().ShouldBe(["Masked 1 value(s) (bound).", "Wrote the configuration to app.properties."]);
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

        await Execute([Key, .. options]);

        _error.ToString().ShouldContain("$['\\u0023\\u0023[");
        _error.ToString().ShouldNotContain("#");
    }

    [Fact]
    public void Options_AreParsed()
    {
        var command = Parse(Key, "--github-env", "--mask", "all", "--prefix", "APP_", "--allow-unprefixed", "--allow-unresolved", "-e", "app.env", "-f", "env");

        command.AnnotationKey.ShouldBe(Key);
        command.IsGitHubEnvRequested.ShouldBeTrue();
        command.Mask.ShouldBe("all");
        command.Prefix.ShouldBe("APP_");
        command.IsUnprefixedAllowed.ShouldBeTrue();
        command.IsUnresolvedAllowed.ShouldBeTrue();
        command.ExportFilePath.ShouldBe("app.env");
        command.Format.ShouldBe("env");
        Parse(Key, "--export", "app.yaml", "--format", "yaml").Format.ShouldBe("yaml");

        // The form the GitHub Action passes every value in.
        var joined = Parse(Key, "--github-env", "--prefix=APP_", "--mask=bound", "--export=app.env", "--format=yaml");
        joined.Prefix.ShouldBe("APP_");
        joined.Mask.ShouldBe("bound");
        joined.ExportFilePath.ShouldBe("app.env");
        joined.Format.ShouldBe("yaml");

        var bare = Parse(Key);
        bare.Mask.ShouldBeNull();
        bare.Format.ShouldBeNull();
        bare.Prefix.ShouldBeNull();
        bare.IsGitHubEnvRequested.ShouldBeFalse();
    }

    [Theory]
    [MemberData(nameof(OptionsOfTheRead))]
    public void OptionsOfTheRead_DoNotParse(string[] options)
    {
        Should.Throw<UnrecognizedCommandParsingException>(() => Parse([Key, .. options]));
        _api.VerifyNoOtherCalls();
    }

    // sform story config export and sform story configuration export, as the host builds the tree; export is matched
    // before the argument of config, so it is never read as a key (a key has at least two segments anyway).
    [Theory]
    [InlineData("config")]
    [InlineData("configuration")]
    public void Export_IsTheSubcommandExport_OfConfig(string config)
    {
        var services = Services()
            .AddSingleton(Mock.Of<IAnnotationsApiClient>())
            .BuildServiceProvider();
        var app = new CommandLineApplication<StorytellerListCommand>(NullConsole.Singleton);
        app.Conventions.UseDefaultConventions().UseConstructorInjection(services);

        var selected = app.Parse(config, "export", Key, "--mask", "all").SelectedCommand;

        selected.Name.ShouldBe("export");
        selected.Parent!.Name.ShouldBe("config");
        selected.ShouldBeOfType<CommandLineApplication<ConfigExportCommand>>().Model.AnnotationKey.ShouldBe(Key);
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

    private IServiceCollection Services()
    {
        return new ServiceCollection()
            .AddSingleton(_fixture.Console.Object)
            .AddSingleton(_fixture.Context.Object)
            .AddSingleton(_api.Object)
            .AddSingleton<IFileSystem>(_fileSystem)
            .AddSingleton(_gitHubActions);
    }

    // The command that config export selects, as the host builds it.
    private ConfigExportCommand Parse(params string[] args)
    {
        var app = new CommandLineApplication<ConfigGetCommand>(NullConsole.Singleton);
        app.Conventions.UseDefaultConventions().UseConstructorInjection(Services().BuildServiceProvider());

        return app.Parse(["export", .. args]).SelectedCommand.ShouldBeOfType<CommandLineApplication<ConfigExportCommand>>().Model;
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

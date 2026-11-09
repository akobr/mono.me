using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Authentication;
using _42.Platform.Cli.Commands;
using _42.Platform.Cli.Commands.AccessPoints;
using _42.Platform.Cli.Commands.Account;
using _42.Platform.Cli.Commands.Assistance;
using _42.Platform.Cli.Commands.Configuration;
using _42.Platform.Cli.Commands.KeyValues;
using _42.Platform.Cli.Commands.MachineAccess;
using _42.Platform.Cli.Commands.Schemas;
using _42.Platform.Cli.Commands.Storyteller;
using _42.Platform.Cli.Commands.Templates;
using _42.Platform.Cli.Configuration;
using _42.Platform.Cli.Output;
using _42.Platform.Cli.Output.Exceptions;
using _42.Platform.Cli.Services;
using _42.Platform.Cli.UnitTests.Authentication;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Options;
using Moq;
using Newtonsoft.Json.Linq;
using Sharprompt;
using Shouldly;
using Xunit;

using static System.IO.Abstractions.TestingHelpers.MockUnixSupport;

namespace _42.Platform.Cli.UnitTests.Commands;

// Spec §1.4: no command waits for an answer when the standard input is redirected. Each prompt fails fast instead,
// naming the option to pass on standard error: a missing value with ERROR_WRONG_INPUT, a confirmation or a command that
// needs a terminal with WARNING_INTERACTION_NEEDED. The API clients are strict mocks, so a command that fails before
// any call proves it.
public class RedirectedInputTests
{
    private static readonly string Home = Path(@"C:\state\ada");
    private static readonly string PatchFile = Path(@"C:\work\patch.json");
    private static readonly string KeyValuesFile = Path(@"C:\work\kv.json");

    private readonly RecordingConsole _console = new RecordingConsole().UseInnerConsole(redirectedInput: true);
    private readonly Mock<ICommandContext> _context = KeyValues.KeyValueTestContext.Create();
    private readonly Mock<IAccessApiClient> _accessApi = new(MockBehavior.Strict);
    private readonly Mock<IConfigurationsApiClient> _configurations = new(MockBehavior.Strict);
    private readonly Mock<ITemplatesApiClient> _templates = new(MockBehavior.Strict);
    private readonly Mock<ISchemasApiClient> _schemas = new(MockBehavior.Strict);
    private readonly Mock<IAnnotationsApiClient> _annotations = new(MockBehavior.Strict);
    private readonly Mock<IKeyValuesApiClient> _keyValues = new(MockBehavior.Strict);
    private readonly Mock<IEditorService> _editor = new(MockBehavior.Strict);
    private readonly MockFileSystem _fileSystem = new();

    [Fact]
    public async Task AccountSet_WithoutAProject_IsAWrongInputNamingTheOption()
    {
        var command = new AccountSetCommand(_console.Object, _accessApi.Object, Options.Create(new AccessDefaultOptions()), Settings());

        await ShouldStop(command.OnExecuteAsync(), ExitCodes.ERROR_WRONG_INPUT);

        _console.ErrorLines.ShouldBe(
        [
            "! sform cannot ask for the default project: the input is redirected.",
            "  Pass it with -p|--projectKey <organizationName>.<projectName>.",
        ]);
    }

    // §1.1 too: the default project goes to sform.settings.json in SFORM_HOME, which is created for it.
    [Fact]
    public async Task AccountSet_WithAProject_WritesTheDefaultIntoTheStateDirectory()
    {
        _accessApi.Setup(api => api.GetAccountAsync()).ReturnsAsync(new _42.Platform.Storyteller.Sdk.Account { AccessMap = new Dictionary<string, AccountRole>() });
        var command = new AccountSetCommand(_console.Object, _accessApi.Object, Options.Create(new AccessDefaultOptions()), Settings(Home))
        {
            ProjectKey = "acme.billing",
            ViewName = "preview",
        };

        var exitCode = await command.OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        var written = JObject.Parse(_fileSystem.File.ReadAllText(Path(@"C:\state\ada\sform.settings.json")));
        written["access"]!["organizationName"]!.Value<string>().ShouldBe("acme");
        written["access"]!["projectName"]!.Value<string>().ShouldBe("billing");
        written["access"]!["viewName"]!.Value<string>().ShouldBe("preview");
        _fileSystem.AllFiles.ShouldNotContain(file => file.StartsWith(Path(@"C:\tools"), StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("acme")]
    [InlineData(".billing")]
    [InlineData("acme.")]
    public async Task AccountSet_WithAMalformedProject_IsAWrongInputBeforeAnyCall(string projectKey)
    {
        var command = new AccountSetCommand(_console.Object, _accessApi.Object, Options.Create(new AccessDefaultOptions()), Settings())
        {
            ProjectKey = projectKey,
        };

        (await command.OnExecuteAsync()).ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _console.Lines.ShouldContain($"! '{projectKey}' is no project key; it takes <organization>.<project>.");
    }

    [Fact]
    public async Task AccountRegister_OfANewUser_NeedsATerminal()
    {
        var authentication = new Mock<IAuthenticationService>(MockBehavior.Strict);
        authentication.Setup(service => service.GetSignedInUserAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new SignedInUser("user_01", "ada@example.com", null));
        _accessApi.Setup(api => api.GetAccountAsync()).ThrowsAsync(NotFound());
        var command = new AccountRegisterCommand(_console.Object, _accessApi.Object, authentication.Object, Settings());

        await ShouldStop(command.OnExecuteAsync(), ExitCodes.WARNING_INTERACTION_NEEDED);

        _console.ErrorLines.ShouldContain("  Run sform account register in a terminal.");
        _accessApi.Verify(api => api.CreateAccountAsync(It.IsAny<AccountCreate>()), Times.Never);
    }

    // SGN-OUT-03: a registered user is told so without a terminal too.
    [Fact]
    public async Task AccountRegister_OfARegisteredUser_SaysSoWithoutATerminal()
    {
        _accessApi.Setup(api => api.GetAccountAsync()).ReturnsAsync(new _42.Platform.Storyteller.Sdk.Account { Id = "7", UserName = "ada@example.com" });
        var command = new AccountRegisterCommand(_console.Object, _accessApi.Object, Mock.Of<IAuthenticationService>(MockBehavior.Strict), Settings());

        (await command.OnExecuteAsync()).ShouldBe(ExitCodes.SUCCESS);
        _console.ErrorText.ShouldBeEmpty();
    }

    [Fact]
    public async Task PointsCreate_NeedsATerminal()
    {
        var command = new AccessPointCreateCommand(_console.Object, _accessApi.Object, Settings());

        await ShouldStop(command.OnExecuteAsync(), ExitCodes.WARNING_INTERACTION_NEEDED);

        _console.ErrorLines.ShouldContain("  Run sform account points create in a terminal.");
    }

    [Fact]
    public async Task PointsGet_WithoutAPoint_IsAWrongInputNamingTheOption()
    {
        var command = new AccessPointGetCommand(_console.Object, _accessApi.Object, Options.Create(new AccessDefaultOptions()));

        await ShouldStop(command.OnExecuteAsync(), ExitCodes.ERROR_WRONG_INPUT);

        _console.ErrorLines.ShouldContain("  Pass it with -p|--pointKey <organizationName>[.<projectName>].");
    }

    [Fact]
    public async Task PointsGrant_WithoutAPoint_IsAWrongInputNamingTheOption()
    {
        var command = new AccessPointGrantCommand(_console.Object, _accessApi.Object, Options.Create(new AccessDefaultOptions()))
        {
            AccountId = "acc-2",
            Role = "Reader",
        };

        await ShouldStop(command.OnExecuteAsync(), ExitCodes.ERROR_WRONG_INPUT);

        _console.ErrorLines.ShouldContain("  Pass it with -p|--point <organizationName>[.<projectName>].");
    }

    [Fact]
    public async Task PointsRevoke_WithoutAPoint_IsAWrongInputNamingTheOption()
    {
        var command = new AccessPointRevokeCommand(_console.Object, _accessApi.Object, Options.Create(new AccessDefaultOptions()))
        {
            AccountId = "acc-2",
        };

        await ShouldStop(command.OnExecuteAsync(), ExitCodes.ERROR_WRONG_INPUT);

        _console.ErrorLines.ShouldContain("  Pass it with -p|--point <organizationName>[.<projectName>].");
    }

    [Fact]
    public async Task Assist_WithoutASubcommand_NamesIt()
    {
        var command = new AssistCommand(_console.Object, new CommandLineApplication());

        await ShouldStop(command.OnExecuteAsync(), ExitCodes.WARNING_INTERACTION_NEEDED);

        _console.ErrorLines.ShouldContain("  Name it: sform assist key.");
        _console.Mock.Verify(console => console.Select(It.IsAny<SelectOptions<string>>()), Times.Never);
    }

    [Fact]
    public async Task AssistKey_NeedsATerminal()
    {
        await ShouldStop(new AssistKeyCommand(_console.Object).OnExecuteAsync(), ExitCodes.WARNING_INTERACTION_NEEDED);

        _console.ErrorLines.ShouldBe(
        [
            "! sform cannot ask for the parts of the annotation key: the input is redirected.",
            "  Run sform assist key in a terminal.",
        ]);
        _console.Lines.ShouldBeEmpty();
    }

    // A patch made in the editor would be lost at the confirmation: that path stops before the editor and any call.
    [Fact]
    public async Task ConfigPatch_InTheEditor_WithoutYes_NamesItBeforeTheEditorAndAnyCall()
    {
        var command = new ConfigPatchCommand(_console.Object, _context.Object, _configurations.Object, _editor.Object, Options.Create(new EditorOptions()), _fileSystem)
        {
            AnnotationKey = "rst.billing",
        };

        (await command.OnExecuteAsync()).ShouldBe(ExitCodes.WARNING_INTERACTION_NEEDED);
        ShouldNameYesForThePatch();
    }

    // A patch file is checked and shown first; only the confirmation stops the run.
    [Fact]
    public async Task ConfigPatch_FromAFile_WithoutYes_StopsAtTheConfirmation()
    {
        _fileSystem.AddFile(PatchFile, new MockFileData("""[ { "op": "replace", "path": "/name", "value": "invoicing" } ]"""));
        StoredConfiguration(new JObject { ["name"] = "billing" });

        (await PatchFromFile().OnExecuteAsync()).ShouldBe(ExitCodes.WARNING_INTERACTION_NEEDED);

        ShouldNameYesForThePatch();
        _console.Lines.ShouldContain("# JSON Patch");
        _console.Mock.Verify(console => console.Confirm(It.IsAny<ConfirmOptions>()), Times.Never);
    }

    // Without a terminal the outcomes before the confirmation keep their codes: nothing to apply, no configuration.
    [Fact]
    public async Task ConfigPatch_FromAnEmptyFile_WithoutYes_IsNoWork()
    {
        _fileSystem.AddFile(PatchFile, new MockFileData("[]"));
        StoredConfiguration(new JObject { ["name"] = "billing" });

        (await PatchFromFile().OnExecuteAsync()).ShouldBe(ExitCodes.WARNING_NO_WORK_NEEDED);

        _console.Lines.ShouldContain("No changes detected.");
        _console.ErrorText.ShouldBeEmpty();
    }

    [Fact]
    public async Task ConfigPatch_FromAFile_OfAMissingConfiguration_WithoutYes_IsAWrongInput()
    {
        _fileSystem.AddFile(PatchFile, new MockFileData("""[ { "op": "replace", "path": "/name", "value": "invoicing" } ]"""));
        _configurations
            .Setup(api => api.GetConfigurationVersionsAsync("org1", "proj1", "default", "rst.billing"))
            .ReturnsAsync(new List<ConfigurationVersion>());

        (await PatchFromFile().OnExecuteAsync()).ShouldBe(ExitCodes.ERROR_WRONG_INPUT);

        _console.ErrorText.ShouldBeEmpty();
    }

    [Fact]
    public async Task ConfigEdit_WithoutYes_NamesItBeforeAnyCall()
    {
        var command = new ConfigEditCommand(_console.Object, _context.Object, _configurations.Object, _editor.Object, Options.Create(new EditorOptions()))
        {
            AnnotationKey = "rst.billing",
        };

        (await command.OnExecuteAsync()).ShouldBe(ExitCodes.WARNING_INTERACTION_NEEDED);
        ShouldNameYesForTheChanges();
    }

    // With -y the editor is opened without the confirmation after it.
    [Fact]
    public async Task SchemaTypeEdit_WithYes_EditsWithoutAConfirmation()
    {
        _schemas
            .Setup(api => api.GetConfigurationSchemaAsync("org1", "proj1", "default", "unt"))
            .ReturnsAsync(new ConfigurationSchema { Version = 1, Content = new JObject { ["type"] = "object" } });
        _editor
            .Setup(editor => editor.EditJsonAsync(It.IsAny<IExtendedConsole>(), It.IsAny<EditorOptions>(), It.IsAny<JObject>(), It.IsAny<string>(), false, false))
            .ReturnsAsync(new JsonEditResult(null, ExitCodes.WARNING_NO_WORK_NEEDED));
        var command = new SchemaTypeEditCommand(_console.Object, _context.Object, _schemas.Object, _editor.Object, Options.Create(new EditorOptions()))
        {
            AnnotationType = "unt",
            Yes = true,
        };

        (await command.OnExecuteAsync()).ShouldBe(ExitCodes.WARNING_NO_WORK_NEEDED);
        _editor.Verify(editor => editor.EditJsonAsync(It.IsAny<IExtendedConsole>(), It.IsAny<EditorOptions>(), It.IsAny<JObject>(), It.IsAny<string>(), false, false), Times.Once);
        _console.ErrorText.ShouldBeEmpty();
    }

    [Fact]
    public async Task TemplateEdit_WithoutYes_NamesItBeforeAnyCall()
    {
        var command = new TemplateEditCommand(_console.Object, _context.Object, _templates.Object, _editor.Object, Options.Create(new EditorOptions()))
        {
            AnnotationType = "unt",
        };

        (await command.OnExecuteAsync()).ShouldBe(ExitCodes.WARNING_INTERACTION_NEEDED);
        ShouldNameYesForTheChanges();
    }

    [Fact]
    public async Task SchemaTypeEdit_WithoutYes_NamesItBeforeAnyCall()
    {
        var command = new SchemaTypeEditCommand(_console.Object, _context.Object, _schemas.Object, _editor.Object, Options.Create(new EditorOptions()))
        {
            AnnotationType = "unt",
        };

        (await command.OnExecuteAsync()).ShouldBe(ExitCodes.WARNING_INTERACTION_NEEDED);
        ShouldNameYesForTheChanges();
    }

    [Fact]
    public async Task SchemaAnnotationEdit_WithoutYes_NamesItBeforeAnyCall()
    {
        var command = new SchemaAnnotationEditCommand(_console.Object, _context.Object, _schemas.Object, _editor.Object, Options.Create(new EditorOptions()))
        {
            AnnotationKey = "rst.billing",
        };

        (await command.OnExecuteAsync()).ShouldBe(ExitCodes.WARNING_INTERACTION_NEEDED);
        ShouldNameYesForTheChanges();
    }

    [Fact]
    public async Task SchemaDescendantEdit_WithoutYes_NamesItBeforeAnyCall()
    {
        var command = new SchemaDescendantEditCommand(_console.Object, _context.Object, _schemas.Object, _editor.Object, Options.Create(new EditorOptions()))
        {
            AnnotationKey = "rst.billing",
            AnnotationType = "unt",
        };

        (await command.OnExecuteAsync()).ShouldBe(ExitCodes.WARNING_INTERACTION_NEEDED);
        ShouldNameYesForTheChanges();
    }

    // The key is looked up first; the confirmation stops the run before the descendants are read or anything is purged.
    [Fact]
    public async Task StoryDelete_Permanent_WithoutYes_StopsAtTheConfirmation()
    {
        _annotations
            .Setup(api => api.GetAnnotationAsync("org1", "proj1", "default", "rst.billing", true, null))
            .ReturnsAsync(new Annotation { AnnotationKey = "rst.billing" });

        (await PermanentDelete().OnExecuteAsync()).ShouldBe(ExitCodes.WARNING_INTERACTION_NEEDED);

        _console.ErrorLines.ShouldBe(
        [
            "! sform cannot ask for the confirmation of the permanent delete: the input is redirected.",
            "  Pass -y|--yes to delete without asking.",
        ]);
        _console.Mock.Verify(console => console.Confirm(It.IsAny<ConfirmOptions>()), Times.Never);
    }

    [Fact]
    public async Task StoryDelete_Permanent_OfAnUnknownKey_WithoutYes_IsNotFound()
    {
        _annotations
            .Setup(api => api.GetAnnotationAsync("org1", "proj1", "default", "rst.billing", true, null))
            .ThrowsAsync(NotFound());
        _annotations
            .Setup(api => api.GetDeletedAnnotationAsync("org1", "proj1", "default", "rst.billing"))
            .ThrowsAsync(NotFound());

        (await PermanentDelete().OnExecuteAsync()).ShouldBe(ExitCodes.ERROR_WRONG_INPUT);

        _console.Lines.ShouldContain("The annotation 'rst.billing' has not been found.");
        _console.ErrorText.ShouldBeEmpty();
    }

    [Fact]
    public async Task KvDelete_Permanent_WithoutYes_NamesIt()
    {
        var command = new KeyValueDeleteCommand(_console.Object, _context.Object, _keyValues.Object)
        {
            Key = "db/password",
            Permanent = true,
        };

        (await command.OnExecuteAsync()).ShouldBe(ExitCodes.WARNING_INTERACTION_NEEDED);
        _console.ErrorLines.ShouldContain("  Pass -y|--yes to remove the key without asking.");
        _console.Mock.Verify(console => console.Input(It.IsAny<InputOptions<string>>()), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task KvPurge_WithoutYes_NamesIt(bool byPrefix)
    {
        var command = new KeyValuePurgeCommand(_console.Object, _context.Object, _keyValues.Object)
        {
            Keys = byPrefix ? null : ["tmp/a"],
            Prefix = byPrefix ? "tmp/" : null,
        };

        (await command.OnExecuteAsync()).ShouldBe(ExitCodes.WARNING_INTERACTION_NEEDED);
        _console.ErrorLines.ShouldContain("  Pass -y|--yes to purge without asking.");
    }

    // The plan (a dry run of the API) is made and shown; the confirmation stops the run before anything is written.
    [Fact]
    public async Task KvImport_WithoutYes_StopsAtTheConfirmationAfterThePlan()
    {
        _fileSystem.AddFile(KeyValuesFile, new MockFileData("""{ "Format": "2s.key-values.v1", "Items": [ { "Key": "db.a", "Protection": "Public", "Value": "1" } ] }"""));
        PlannedImport(KeyValueImportItemResultAction.Create);

        (await Import().OnExecuteAsync()).ShouldBe(ExitCodes.WARNING_INTERACTION_NEEDED);

        _console.ErrorLines.ShouldBe(
        [
            "! sform cannot ask for the confirmation of the import: the input is redirected.",
            "  Pass -y|--yes to import without asking, or --dry-run to see the plan.",
        ]);
        _keyValues.Verify(api => api.ImportKeyValuesAsync("org1", "proj1", "default", It.IsAny<KeyValueDocument>(), "Merge", true, null), Times.Once);
    }

    // An import with nothing to change, the usual idempotent CI step, ends with 42 without a terminal too.
    [Fact]
    public async Task KvImport_WithNothingToChange_WithoutYes_IsNoWork()
    {
        _fileSystem.AddFile(KeyValuesFile, new MockFileData("""{ "Format": "2s.key-values.v1", "Items": [ { "Key": "db.a", "Protection": "Public", "Value": "1" } ] }"""));
        PlannedImport(KeyValueImportItemResultAction.Unchanged);

        (await Import().OnExecuteAsync()).ShouldBe(ExitCodes.WARNING_NO_WORK_NEEDED);

        _console.ErrorText.ShouldBeEmpty();
    }

    [Fact]
    public async Task KvImport_OfAMissingFile_WithoutYes_IsAWrongInput()
    {
        (await Import().OnExecuteAsync()).ShouldBe(ExitCodes.ERROR_WRONG_INPUT);

        _console.ErrorText.ShouldBeEmpty();
        _console.Lines.ShouldContain($"! The file '{KeyValuesFile}' does not exist.");
    }

    // A dry run asks nothing, so it gets past the check (and stops at the missing file here).
    [Fact]
    public async Task KvImport_DryRun_IsNotAskedFor()
    {
        var command = new KeyValueImportCommand(_console.Object, _context.Object, _keyValues.Object, _fileSystem)
        {
            File = "kv.json",
            DryRun = true,
        };

        (await command.OnExecuteAsync()).ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _console.ErrorText.ShouldBeEmpty();
        _console.Lines.ShouldContain("! The file 'kv.json' does not exist.");
    }

    [Fact]
    public async Task KvRecipientsRemove_WithoutYes_NamesIt()
    {
        var command = new KeyValueRecipientRemoveCommand(_console.Object, _context.Object, _keyValues.Object)
        {
            Id = "laptop",
        };

        (await command.OnExecuteAsync()).ShouldBe(ExitCodes.WARNING_INTERACTION_NEEDED);
        _console.ErrorLines.ShouldContain("  Pass -y|--yes to remove the recipient without asking.");
    }

    // KV-SEC-04: a secret without a value source.
    [Fact]
    public async Task KvSet_SecretWithoutAValue_IsAWrongInputNamingStdin()
    {
        var command = new KeyValueSetCommand(_console.Object, _context.Object, _keyValues.Object, _fileSystem)
        {
            Key = "db/password",
            Protection = "standard",
        };

        (await command.OnExecuteAsync()).ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _console.ErrorLines.ShouldBe(
        [
            "! sform cannot ask for the value of 'db/password': the input is redirected.",
            "  Pass it with --stdin, with -i|--import <file>, or as an argument.",
        ]);
        _console.Mock.Verify(console => console.Password(It.IsAny<PasswordOptions>()), Times.Never);
    }

    // machine renew --password-stdin: the password is the first line of the standard input, and nothing after it is read.
    [Fact]
    public async Task MachineRenew_WithPasswordStdin_ReadsTheFirstLineOfTheInput()
    {
        var certificatePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sform-renew-" + Guid.NewGuid().ToString("N") + ".pfx");
        await File.WriteAllBytesAsync(certificatePath, TestCertificates.Pkcs12(TestCertificates.MachineUri("org1", "proj1", "m1")), TestContext.Current.CancellationToken);
        var console = new RecordingConsole().UseInnerConsole(redirectedInput: true, input: TestCertificates.Password + "\nnext-machine\n");
        _accessApi
            .Setup(api => api.RenewMachineCertificateAsync("org1", "proj1", "m1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CertificateRenewalResult { Message = "Renewed." });
        var configuration = new SdkConfiguration();

        try
        {
            var command = new MachineRenewCommand(console.Object, _context.Object, AccessServices(), configuration, _fileSystem)
            {
                MachineId = "m1",
                CertificatePath = certificatePath,
                PasswordFromStdin = true,
            };

            (await command.OnExecuteAsync()).ShouldBe(ExitCodes.SUCCESS);
            configuration.ClientCertificate.ShouldNotBeNull();
            console.Mock.Verify(c => c.Password(It.IsAny<PasswordOptions>()), Times.Never);
            console.Object.Console.In.ReadLine().ShouldBe("next-machine");
        }
        finally
        {
            File.Delete(certificatePath);
        }
    }

    // Without --password-stdin a redirected input is never read: the run stops before the certificate and any call
    // (a renew in a `while read` loop does not eat the next line).
    [Fact]
    public async Task MachineRenew_WithoutPasswordStdin_IsAWrongInputNamingIt()
    {
        var console = new RecordingConsole().UseInnerConsole(redirectedInput: true, input: "next-machine\n");
        var command = new MachineRenewCommand(console.Object, _context.Object, AccessServices(), new SdkConfiguration(), _fileSystem)
        {
            MachineId = "m1",
            CertificatePath = "m1.pfx",
        };

        (await command.OnExecuteAsync()).ShouldBe(ExitCodes.ERROR_WRONG_INPUT);

        console.ErrorLines.ShouldBe(
        [
            "! sform cannot ask for the password of the certificate: the input is redirected.",
            "  Pass it as the first line of the standard input with --password-stdin (an empty line for none).",
        ]);
        console.Object.Console.In.ReadLine().ShouldBe("next-machine");
    }

    [Fact]
    public async Task MachineRenew_PasswordStdinWithoutACertificate_IsAWrongInput()
    {
        var command = new MachineRenewCommand(_console.Object, _context.Object, AccessServices(), new SdkConfiguration(), _fileSystem)
        {
            MachineId = "m1",
            PasswordFromStdin = true,
        };

        (await command.OnExecuteAsync()).ShouldBe(ExitCodes.ERROR_WRONG_INPUT);

        _console.Lines.ShouldContain("! --password-stdin reads the password of the certificate given with -c|--certificate.");
    }

    [Fact]
    public void EditorSetup_NamesTheWaysToSetTheEditor()
    {
        var service = new EditorService(_fileSystem, Settings(Home), EnvironmentEditor.None);

        var exception = Should.Throw<InteractionNeededException>(() => service.SetupEditorPreference(_console.Object));

        exception.ExitCode.ShouldBe(ExitCodes.WARNING_INTERACTION_NEEDED);
        _console.ErrorLines.ShouldBe(
        [
            "! sform cannot ask for the editor to use: the input is redirected.",
            "  Choose one with sform account settings editor in a terminal, set it with sform account settings set editor.type, or set $VISUAL or $EDITOR.",
        ]);
    }

    [Fact]
    public void ExportToAnExistingFile_IsRewrittenWithoutAsking()
    {
        _fileSystem.AddFile(Path(@"C:\out\a.json"), new MockFileData("old"));

        _console.Object.WriteJsonToFile(new { value = 1 }, Path(@"C:\out\a.json"), _fileSystem);

        _fileSystem.File.ReadAllText(Path(@"C:\out\a.json")).ShouldContain("\"value\"");
        _console.Mock.Verify(console => console.Confirm(It.IsAny<ConfirmOptions>()), Times.Never);
    }

    // The same commands in a terminal still ask.
    [Fact]
    public async Task InATerminal_TheConfirmationIsAsked()
    {
        var console = new RecordingConsole().UseInnerConsole(redirectedInput: false);
        console.Mock.Setup(c => c.Confirm(It.IsAny<ConfirmOptions>())).Returns(false);
        var command = new KeyValueRecipientRemoveCommand(console.Object, _context.Object, _keyValues.Object)
        {
            Id = "laptop",
        };

        (await command.OnExecuteAsync()).ShouldBe(ExitCodes.WARNING_ABORTED);
        console.ErrorText.ShouldBeEmpty();
    }

    private ConfigPatchCommand PatchFromFile()
    {
        return new ConfigPatchCommand(_console.Object, _context.Object, _configurations.Object, _editor.Object, Options.Create(new EditorOptions()), _fileSystem)
        {
            AnnotationKey = "rst.billing",
            ImportFilePath = PatchFile,
        };
    }

    private void StoredConfiguration(JObject content)
    {
        _configurations
            .Setup(api => api.GetConfigurationVersionsAsync("org1", "proj1", "default", "rst.billing"))
            .ReturnsAsync(new List<ConfigurationVersion> { new() { Version = 3, ExpirationTime = DateTimeOffset.MaxValue } });
        _configurations
            .Setup(api => api.GetConfigurationVersionAsync("org1", "proj1", "default", "rst.billing", 3))
            .ReturnsAsync(new _42.Platform.Storyteller.Sdk.Configuration { Content = content });
    }

    private StorytellerDeleteCommand PermanentDelete()
    {
        return new StorytellerDeleteCommand(_console.Object, _context.Object, _annotations.Object)
        {
            AnnotationKey = "rst.billing",
            Permanent = true,
        };
    }

    private KeyValueImportCommand Import()
    {
        return new KeyValueImportCommand(_console.Object, _context.Object, _keyValues.Object, _fileSystem)
        {
            File = KeyValuesFile,
        };
    }

    private void PlannedImport(KeyValueImportItemResultAction action)
    {
        _keyValues
            .Setup(api => api.ImportKeyValuesAsync("org1", "proj1", "default", It.IsAny<KeyValueDocument>(), "Merge", true, null))
            .ReturnsAsync(KeyValues.KeyValueTestContext.Import(true, true, ("db.a", action)));
    }

    private IServiceProvider AccessServices()
    {
        var services = new Mock<IServiceProvider>();
        services.Setup(provider => provider.GetService(typeof(IAccessApiClient))).Returns(_accessApi.Object);
        return services.Object;
    }

    private void ShouldNameYesForThePatch()
    {
        _console.ErrorLines.ShouldBe(
        [
            "! sform cannot ask for the confirmation of the patch: the input is redirected.",
            "  Pass -y|--yes to apply it without asking.",
        ]);
    }

    private UserSettingsStore Settings(string? home = null)
    {
        return new UserSettingsStore(_fileSystem, Paths(home));
    }

    private CliPaths Paths(string? home = null)
    {
        return CliPaths.Create(_fileSystem, home, Path(@"C:\tools\sform"), () => Path(@"C:\Users\ada"));
    }

    private static ApiException NotFound()
    {
        return new ApiException("Not found", (int)HttpStatusCode.NotFound, string.Empty, new Dictionary<string, IEnumerable<string>>(), null);
    }

    // A command outside BaseContextCommand: the exception reaches Program, which ends with its exit code.
    private static async Task ShouldStop(Task<int> execution, int exitCode)
    {
        var exception = await Should.ThrowAsync<InteractionNeededException>(execution);
        exception.ExitCode.ShouldBe(exitCode);
    }

    private void ShouldNameYesForTheChanges()
    {
        _console.ErrorLines.ShouldBe(
        [
            "! sform cannot ask for the confirmation of the changes: the input is redirected.",
            "  Pass -y|--yes to save them without asking.",
        ]);
    }
}

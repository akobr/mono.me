using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using _42.Platform.Cli.AutomationTests.Access;
using _42.Platform.Cli.AutomationTests.Data;
using _42.Platform.Cli.AutomationTests.Editing;
using _42.Platform.Cli.AutomationTests.Running;
using _42.Platform.Cli.AutomationTests.Running.Terminal;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;

namespace _42.Platform.Cli.AutomationTests.Tests.Account;

// Settings: sform account settings and its editor, set and unset, which keep the user's preferences in sform.settings.json
// of SFORM_HOME (the editor and the default project; UserSettingDefinitions of the CLI): the list with the value each
// command uses and where it comes from (the settings, a legacy file, a variable, a machine sign-in, the default), and the
// effect of the values on the context commands and the edit commands. No account settings command needs a sign-in or
// calls the API; their errors go to the standard output ("! …"), with 2 for wrong input.
[Area(Area.Account)]
public sealed partial class AccountSettingsTests : CliTest
{
    private const string FromSettings = "settings";
    private const string Default = "default";
    private const string EditorType = "editor.type";
    private const string EditorCommand = "editor.command";
    private const string AccessProject = "access.project";
    private const string AccessView = "access.view";
    private const string KnownSettings = "the settings are editor.type, editor.command, access.project, access.view.";
    private const string DamagedNote = "sform uses no editor and no default project from it until it is fixed (sform account settings --path).";

    // A custom editor of the cases that never start it.
    private const string CodeWait = "code --wait";

    private static readonly TimeSpan PromptTimeout = TimeSpan.FromSeconds(30);

    // ACC-STG-01: account settings lists every setting with its default and source in an empty home without a sign-in;
    // --path prints the file and --json the values the commands use.
    // In an empty home: sform account settings prints the header "Settings", "File <SFORM_HOME>/sform.settings.json" and a
    // row per setting, editor.type "(ask on first use)", editor.command "(not set)", access.project house.main and
    // access.view default, each from "default", then "Change them with sform account settings editor, set and unset.";
    // sform account settings --path prints only the path of sform.settings.json in SFORM_HOME; sform account settings
    // --json prints { "editor.type": null, "editor.command": null, "access.project": "house.main", "access.view":
    // "default" }. Each ends with 0 without a sign-in, and no sform.settings.json is written.
    [Test]
    [TestCase(474)]
    [UserRole(Role.Any)]
    [FreshSignIn]
    [Covers("account settings", "--path", "--json")]
    public async Task Settings_EmptyHome_ListsDefaults()
    {
        var list = (await Sform("account", "settings"))
            .ShouldSucceed()
            .ShouldHaveLine("# Settings")
            .ShouldHaveLine($"File {Home.SettingsFile}")
            .ShouldHaveLine("Change them with sform account settings editor, set and unset.");
        await AssertRowAsync(list, EditorType, "(ask on first use)", Default);
        await AssertRowAsync(list, EditorCommand, "(not set)", Default);
        await AssertRowAsync(list, AccessProject, "house.main", Default);
        await AssertRowAsync(list, AccessView, "default", Default);

        var path = (await Sform("account", "settings", "--path")).ShouldSucceed();
        await Assert.That(path.PlainStdOut.Trim()).IsEqualTo(Home.SettingsFile);

        var json = (await Sform("account", "settings", "--json")).ShouldSucceed().JsonNode();
        await AssertJsonAsync(json, editorType: null, editorCommand: null, project: "house.main", view: "default");

        await Assert.That(FileSystem.File.Exists(Home.SettingsFile)).IsFalse();
        await Assert.That(Home.HasSession).IsFalse();
    }

    // ACC-STG-02: account settings set stores each setting in sform.settings.json, and the list shows it with the source
    // settings.
    // In an empty home: account settings set access.project <org>.<generated> ("access.project = <org>.<generated>"),
    // access.view <view>, editor.command "code --wait" ("editor.command = code --wait" and the hint that sform uses it when
    // editor.type is custom) and editor.type custom ("editor.type = custom", no hint: the command is there) each end with 0
    // and no call; sform.settings.json holds the access section (organizationName, projectName, viewName) and the editor
    // section (editorType Custom, customCommand); the list shows the four values from "settings" and --json the same. The
    // same access.view again ends with 42 ("access.view is already <view>."). After editor.type vim the list shows
    // editor.command from "settings (used by a custom editor only)", and --json has editor.type vim and editor.command null.
    [Test]
    [TestCase(475)]
    [UserRole(Role.Any)]
    [FreshSignIn]
    [Covers("account settings set", "arg0", "arg1", "arg0=editor.type", "arg0=editor.command", "arg0=access.project", "arg0=access.view")]
    public async Task SettingsSet_EachKey_Stored()
    {
        var generated = CliData.ProjectName();
        var project = $"{Target.Organization}.{generated}";
        var view = CliData.ViewName();

        (await Sform("account", "settings", "set", AccessProject, project)).ShouldSucceed().ShouldHaveLine($"{AccessProject} = {project}");
        (await Sform("account", "settings", "set", AccessView, view)).ShouldSucceed().ShouldHaveLine($"{AccessView} = {view}");
        (await Sform("account", "settings", "set", EditorCommand, CodeWait))
            .ShouldSucceed()
            .ShouldHaveLine($"{EditorCommand} = {CodeWait}")
            .ShouldContain("sform uses it when editor.type is custom: sform account settings set editor.type custom.");
        (await Sform("account", "settings", "set", EditorType, "custom"))
            .ShouldSucceed()
            .ShouldHaveLine($"{EditorType} = custom")
            .ShouldNotContain("A custom editor needs its command too");

        var access = SectionOf(Home, "access");
        await Assert.That(access?["organizationName"]?.GetValue<string>()).IsEqualTo(Target.Organization);
        await Assert.That(access?["projectName"]?.GetValue<string>()).IsEqualTo(generated);
        await Assert.That(access?["viewName"]?.GetValue<string>()).IsEqualTo(view);
        var editor = SectionOf(Home, "editor");
        await Assert.That(editor?["editorType"]?.GetValue<string>()).IsEqualTo("Custom");
        await Assert.That(editor?["customCommand"]?.GetValue<string>()).IsEqualTo(CodeWait);

        var list = (await Sform("account", "settings")).ShouldSucceed();
        await AssertRowAsync(list, EditorType, "custom", FromSettings);
        await AssertRowAsync(list, EditorCommand, CodeWait, FromSettings);
        await AssertRowAsync(list, AccessProject, project, FromSettings);
        await AssertRowAsync(list, AccessView, view, FromSettings);
        await AssertJsonAsync((await Sform("account", "settings", "--json")).ShouldSucceed().JsonNode(), "custom", CodeWait, project, view);

        (await Sform("account", "settings", "set", AccessView, view))
            .ShouldExitWith(CliExit.NoWorkNeeded)
            .ShouldContain($"{AccessView} is already {view}.");

        (await Sform("account", "settings", "set", EditorType, "vim")).ShouldSucceed().ShouldHaveLine($"{EditorType} = vim");
        var vim = (await Sform("account", "settings")).ShouldSucceed();
        await AssertRowAsync(vim, EditorType, "vim", FromSettings);
        await AssertRowAsync(vim, EditorCommand, CodeWait, "settings (used by a custom editor only)");
        await AssertJsonAsync((await Sform("account", "settings", "--json")).ShouldSucceed().JsonNode(), "vim", null, project, view);
    }

    // ACC-STG-03: account settings set refuses an unknown setting, a missing key or value and an invalid value with exit 2;
    // a damaged settings file fails the list and the change.
    // In an empty home each of account settings set nope.key x, set (no key), set access.view (no value), set
    // access.project only-org, set editor.type nano and set editor.command '""' ends with 2 and names what is wrong and what
    // it takes; no sform.settings.json is written. With "{ "access": " in sform.settings.json, account settings set
    // access.view <view> ends with 2 ("access.view could not be saved: The settings file <path> is damaged: …") and leaves
    // the file as it was, account settings ends with 2 ("The settings file <path> is damaged: …"), both say on stderr that
    // sform uses nothing from it until it is fixed, and account settings --path still prints the path with 0.
    [Test]
    [TestCase(476)]
    [UserRole(Role.Any)]
    [FreshSignIn]
    [Covers("account settings", "--path")]
    [Covers("account settings set", "arg0", "arg1")]
    public async Task SettingsSet_InvalidInputOrDamagedFile_Refused()
    {
        (string[] Arguments, string Message)[] refused =
        [
            (["nope.key", "x"], $"'nope.key' is no setting sform knows; {KnownSettings}"),
            ([], $"Name the setting; {KnownSettings}"),
            ([AccessView], "Give the value of access.view; it takes a view name."),
            ([AccessProject, "only-org"], "'only-org' is no value of access.project; it takes <organization>.<project>."),
            ([EditorType, "nano"], "'nano' is no value of editor.type; the values are vscode, neovim, vim, custom."),
            ([EditorCommand, "\"\""], "'\"\"' is no value of editor.command; it takes a command line, e.g. \"code --wait\"."),
        ];

        foreach (var (arguments, message) in refused)
        {
            (await Sform(["account", "settings", "set", .. arguments]))
                .ShouldExitWith(CliExit.WrongInput)
                .ShouldContain(message);
        }

        await Assert.That(FileSystem.File.Exists(Home.SettingsFile)).IsFalse();

        const string damaged = """{ "access": """;
        Home.Ensure();
        await FileSystem.File.WriteAllTextAsync(Home.SettingsFile, damaged);
        var damagedFile = $"The settings file {Home.SettingsFile} is damaged: ";

        (await Sform("account", "settings", "set", AccessView, CliData.ViewName()))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain($"{AccessView} could not be saved: {damagedFile}")
            .ShouldContain(DamagedNote, SformStream.StdErr);
        await Assert.That(Home.ReadText(SformHome.SettingsFileName)).IsEqualTo(damaged);

        (await Sform("account", "settings"))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain(damagedFile)
            .ShouldContain(DamagedNote, SformStream.StdErr);

        var path = (await Sform("account", "settings", "--path")).ShouldSucceed();
        await Assert.That(path.PlainStdOut.Trim()).IsEqualTo(Home.SettingsFile);
        await Assert.That(Home.ReadText(SformHome.SettingsFileName)).IsEqualTo(damaged);
    }

    // ACC-STG-04: account settings unset removes each setting and names what sform uses then; a setting that is not set
    // ends with 42, an unknown one with 2.
    // In an empty home with every setting set (access.project <org>.<generated>, access.view <view>, editor.type custom,
    // editor.command "code --wait"): account settings unset access.view says "access.view is unset; sform uses default."
    // (0), and again "access.view is not set." (42); unset access.project "access.project is unset; sform uses
    // house.main."; unset editor.command "editor.command is unset."; unset editor.type "editor.type is unset; the next edit
    // uses $VISUAL or $EDITOR, or asks."; unset nope and unset without a key end with 2. The list then shows every setting
    // from "default", and sform.settings.json holds no section.
    [Test]
    [TestCase(477)]
    [UserRole(Role.Any)]
    [FreshSignIn]
    [Covers("account settings unset", "arg0", "arg0=editor.type", "arg0=editor.command", "arg0=access.project", "arg0=access.view")]
    public async Task SettingsUnset_EachKey_Removed()
    {
        (await Sform("account", "settings", "set", AccessProject, $"{Target.Organization}.{CliData.ProjectName()}")).ShouldSucceed();
        (await Sform("account", "settings", "set", AccessView, CliData.ViewName())).ShouldSucceed();
        (await Sform("account", "settings", "set", EditorType, "custom")).ShouldSucceed();
        (await Sform("account", "settings", "set", EditorCommand, CodeWait)).ShouldSucceed();

        (await Sform("account", "settings", "unset", AccessView)).ShouldSucceed().ShouldHaveLine("access.view is unset; sform uses default.");
        (await Sform("account", "settings", "unset", AccessView)).ShouldExitWith(CliExit.NoWorkNeeded).ShouldHaveLine("access.view is not set.");
        (await Sform("account", "settings", "unset", AccessProject)).ShouldSucceed().ShouldHaveLine("access.project is unset; sform uses house.main.");
        (await Sform("account", "settings", "unset", EditorCommand)).ShouldSucceed().ShouldHaveLine("editor.command is unset.");
        (await Sform("account", "settings", "unset", EditorType)).ShouldSucceed().ShouldHaveLine("editor.type is unset; the next edit uses $VISUAL or $EDITOR, or asks.");

        (await Sform("account", "settings", "unset", "nope"))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain($"'nope' is no setting sform knows; {KnownSettings}");
        (await Sform("account", "settings", "unset"))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain($"Name the setting; {KnownSettings}");

        var list = (await Sform("account", "settings")).ShouldSucceed();
        await AssertRowAsync(list, EditorType, "(ask on first use)", Default);
        await AssertRowAsync(list, EditorCommand, "(not set)", Default);
        await AssertRowAsync(list, AccessProject, "house.main", Default);
        await AssertRowAsync(list, AccessView, "default", Default);
        await Assert.That(Home.ReadSettings()?.Count ?? 0).IsEqualTo(0);
    }

    // ACC-STG-05: account settings editor stores each editor type, and a custom one with -c or --command; the same editor
    // again ends with 42.
    // In an empty home, with <cmd> the fake editor's apphost (a program that exists): account settings editor vscode,
    // neovim and vim say "Editor set to vscode." (neovim, vim) with 0 and store editorType VsCode (Neovim, Vim) without a
    // command (a program that is not on the PATH adds "<type> was not found on this machine; the choice is saved anyway.");
    // vim again ends with 42 and "The editor is already vim."; editor custom -c <cmd> says "Editor set to custom (<cmd>)."
    // and stores editorType Custom with the command; editor --command "<cmd> --wait" alone is a custom editor ("Editor set
    // to custom (<cmd> --wait)."), and the same with custom ends with 42 ("The editor is already custom (<cmd> --wait).");
    // the list shows editor.type custom and editor.command <cmd> --wait from "settings".
    [Test]
    [TestCase(478)]
    [UserRole(Role.Any)]
    [FreshSignIn]
    [Covers("account settings editor", "arg0", "arg0=vscode", "arg0=neovim", "arg0=vim", "arg0=custom", "-c", "--command")]
    public async Task SettingsEditor_EachType_Stored()
    {
        foreach (var (name, stored) in new[] { ("vscode", "VsCode"), ("neovim", "Neovim"), ("vim", "Vim") })
        {
            var set = (await Sform("account", "settings", "editor", name))
                .ShouldSucceed()
                .ShouldHaveLine($"Editor set to {name}.");
            if (set.PlainStdOut.Contains("was not found", StringComparison.Ordinal))
            {
                set.ShouldContain($"{name} was not found on this machine; the choice is saved anyway.");
            }

            var section = SectionOf(Home, "editor");
            await Assert.That(section?["editorType"]?.GetValue<string>()).IsEqualTo(stored);
            await Assert.That(section?["customCommand"]).IsNull();
        }

        (await Sform("account", "settings", "editor", "vim"))
            .ShouldExitWith(CliExit.NoWorkNeeded)
            .ShouldContain("The editor is already vim.");

        var command = FakeEditor.ResolveCommand(FileSystem);
        (await Sform("account", "settings", "editor", "custom", "-c", command))
            .ShouldSucceed()
            .ShouldContain($"Editor set to custom ({command}).")
            .ShouldNotContain("was not found");
        var custom = SectionOf(Home, "editor");
        await Assert.That(custom?["editorType"]?.GetValue<string>()).IsEqualTo("Custom");
        await Assert.That(custom?["customCommand"]?.GetValue<string>()).IsEqualTo(command);

        var withArguments = $"{command} --wait";
        (await Sform("account", "settings", "editor", "--command", withArguments))
            .ShouldSucceed()
            .ShouldContain($"Editor set to custom ({withArguments}).");
        (await Sform("account", "settings", "editor", "custom", "--command", withArguments))
            .ShouldExitWith(CliExit.NoWorkNeeded)
            .ShouldContain($"The editor is already custom ({withArguments}).");

        var list = (await Sform("account", "settings")).ShouldSucceed();
        await AssertRowAsync(list, EditorType, "custom", FromSettings);
        await AssertRowAsync(list, EditorCommand, withArguments, FromSettings);
    }

    // ACC-STG-06: account settings editor refuses an unknown type, a custom one without a command and --command with another
    // type, and without a type on redirected input names the form; nothing is saved.
    // In an empty home each of account settings editor nano ("'nano' is no editor sform knows; use vscode, neovim, vim,
    // custom."), editor custom ("A custom editor needs its command line: sform account settings editor custom --command
    // "code --wait"."), editor vim -c "code --wait" and editor neovim --command "code --wait" ("--command is for a custom
    // editor only; vim needs none.", neovim) and editor without a type on redirected input ("Name the editor: sform account
    // settings editor <vscode|neovim|vim|custom> [--command "<command>"].") ends with 2, and no sform.settings.json is
    // written.
    [Test]
    [TestCase(479)]
    [UserRole(Role.Any)]
    [FreshSignIn]
    [Covers("account settings editor", "arg0", "-c", "--command")]
    public async Task SettingsEditor_InvalidInput_Refused()
    {
        (string[] Arguments, string Message)[] refused =
        [
            (["nano"], "'nano' is no editor sform knows; use vscode, neovim, vim, custom."),
            (["custom"], "A custom editor needs its command line: sform account settings editor custom --command \"code --wait\"."),
            (["vim", "-c", CodeWait], "--command is for a custom editor only; vim needs none."),
            (["neovim", "--command", CodeWait], "--command is for a custom editor only; neovim needs none."),
            ([], "Name the editor: sform account settings editor <vscode|neovim|vim|custom> [--command \"<command>\"]."),
        ];

        foreach (var (arguments, message) in refused)
        {
            (await Sform(new SformRunOptions { Timeout = PromptTimeout }, ["account", "settings", "editor", .. arguments]))
                .ShouldExitWith(CliExit.WrongInput)
                .ShouldContain(message)
                .ShouldNotContain("Editor set to");
        }

        await Assert.That(FileSystem.File.Exists(Home.SettingsFile)).IsFalse();
    }

    // ACC-STG-07: account settings editor without a type asks for the editor in a terminal and stores a custom command line;
    // an empty command saves nothing.
    // In an empty home, with <cmd> the fake editor's apphost: sform account settings editor in a terminal asks "Select your
    // preferred editor for JSON configuration editing" (the editors found on the PATH, and "Custom command", picked by typing
    // "Custom" into the list) and "Enter the command to launch your editor" (<cmd>): "Editor set to custom (<cmd>)." with 0
    // and the editor section { "editorType": "Custom", "customCommand": "<cmd>" }. The same with an empty command says "No
    // editor command was entered, nothing was saved." with 40, and the section is as before.
    [Test]
    [TestCase(480)]
    [UserRole(Role.Any)]
    [FreshSignIn]
    [Terminal]
    [Covers("account settings editor")]
    public async Task SettingsEditor_Interactive_AsksForEditor()
    {
        var command = FakeEditor.ResolveCommand(FileSystem);

        var saved = await PickCustomEditorAsync(command);
        saved.ShouldSucceed().ShouldContain($"Editor set to custom ({command}).");
        var section = SectionOf(Home, "editor");
        await Assert.That(section?["editorType"]?.GetValue<string>()).IsEqualTo("Custom");
        await Assert.That(section?["customCommand"]?.GetValue<string>()).IsEqualTo(command);
        var before = Home.ReadText(SformHome.SettingsFileName);

        var empty = await PickCustomEditorAsync(string.Empty);
        empty.ShouldExitWith(CliExit.Aborted).ShouldContain("No editor command was entered, nothing was saved.");
        await Assert.That(Home.ReadText(SformHome.SettingsFileName)).IsEqualTo(before);
    }

    // ACC-STG-08: The legacy access.default.json and editor.config.json show as the source until the first change moves them
    // into sform.settings.json, whose sections win.
    // In an empty home with the files of an earlier sform (access.default.json { "access": { "OrganizationName",
    // "ProjectName", "ViewName" } } and editor.config.json { "editor": { "EditorType": "Vim" } }): account settings shows
    // editor.type vim from "legacy editor.config.json", access.project and access.view from "legacy access.default.json",
    // and --json those values. account settings set access.view <view2> moves both into sform.settings.json (access with
    // viewName <view2>, editor with editorType Vim) and deletes them; the list shows the three from "settings". An
    // access.default.json written again with another project changes nothing: the settings file has its access section.
    [Test]
    [TestCase(481)]
    [UserRole(Role.Any)]
    [FreshSignIn]
    [Covers("account settings", "--json")]
    [Covers("account settings set")]
    public async Task Settings_LegacyFiles_ReadThenMoved()
    {
        var generated = CliData.ProjectName();
        var project = $"{Target.Organization}.{generated}";
        var view = CliData.ViewName();
        var view2 = CliData.ViewName();
        await WriteLegacyAccessAsync(Target.Organization, generated, view);
        await FileSystem.File.WriteAllTextAsync(Home.EditorConfigFile, """{ "editor": { "EditorType": "Vim" } }""");

        var legacy = (await Sform("account", "settings")).ShouldSucceed();
        await AssertRowAsync(legacy, EditorType, "vim", "legacy editor.config.json");
        await AssertRowAsync(legacy, AccessProject, project, "legacy access.default.json");
        await AssertRowAsync(legacy, AccessView, view, "legacy access.default.json");
        await AssertJsonAsync((await Sform("account", "settings", "--json")).ShouldSucceed().JsonNode(), "vim", null, project, view);

        (await Sform("account", "settings", "set", AccessView, view2)).ShouldSucceed();
        await Assert.That(FileSystem.File.Exists(Home.AccessDefaultFile)).IsFalse();
        await Assert.That(FileSystem.File.Exists(Home.EditorConfigFile)).IsFalse();
        await Assert.That(SectionOf(Home, "access")?["projectName"]?.GetValue<string>()).IsEqualTo(generated);
        await Assert.That(SectionOf(Home, "access")?["viewName"]?.GetValue<string>()).IsEqualTo(view2);
        await Assert.That(SectionOf(Home, "editor")?["editorType"]?.GetValue<string>()).IsEqualTo("Vim");

        var moved = (await Sform("account", "settings")).ShouldSucceed();
        await AssertRowAsync(moved, EditorType, "vim", FromSettings);
        await AssertRowAsync(moved, AccessProject, project, FromSettings);
        await AssertRowAsync(moved, AccessView, view2, FromSettings);

        await WriteLegacyAccessAsync(Target.Organization, CliData.ProjectName(), CliData.ViewName());
        var settingsWin = (await Sform("account", "settings")).ShouldSucceed();
        await AssertRowAsync(settingsWin, AccessProject, project, FromSettings);
        await AssertRowAsync(settingsWin, AccessView, view2, FromSettings);
    }

    // ACC-STG-09: The source column names $VISUAL, $EDITOR, STORYTELLER_PROJECT, STORYTELLER_VIEW and a machine sign-in, as
    // the commands resolve them.
    // In an empty home: with EDITOR=<cmd> account settings shows editor.type custom and editor.command <cmd> from "$EDITOR";
    // with VISUAL=<cmd2> too, <cmd2> from "$VISUAL"; after account settings set editor.type vim, editor.type vim from
    // "settings" despite $VISUAL. With STORYTELLER_PROJECT and STORYTELLER_VIEW, access.project and access.view from them;
    // after account settings set access.view <view2>, STORYTELLER_PROJECT alone leaves the view default ("default
    // (STORYTELLER_PROJECT is set)": the view of the settings belongs to their project). STORYTELLER_PROJECT=only-org shows
    // "STORYTELLER_PROJECT (invalid, not <organization>.<project>)" and --json null for access.project. With
    // STORYTELLER_CLIENT_ID and STORYTELLER_CLIENT_SECRET (a machine sign-in) access.project and access.view are "(of the
    // machine sign-in)" from "machine sign-in (STORYTELLER_CLIENT_ID, STORYTELLER_CLIENT_SECRET)", and --json null. Every
    // run ends with 0.
    [Test]
    [TestCase(482)]
    [UserRole(Role.Any)]
    [FreshSignIn]
    [Covers("account settings", "--json")]
    public async Task Settings_Environment_ShowsSource()
    {
        var editorCommand = $"{CliData.Slug("ed")} --wait";
        var visualCommand = CliData.Slug("vis");
        var project = $"{Target.Organization}.{CliData.ProjectName()}";
        var view = CliData.ViewName();

        var editor = (await Sform(With(("EDITOR", editorCommand)), "account", "settings")).ShouldSucceed();
        await AssertRowAsync(editor, EditorType, "custom", "$EDITOR");
        await AssertRowAsync(editor, EditorCommand, editorCommand, "$EDITOR");
        await AssertJsonAsync((await Sform(With(("EDITOR", editorCommand)), "account", "settings", "--json")).ShouldSucceed().JsonNode(), "custom", editorCommand, "house.main", "default");

        var visual = (await Sform(With(("VISUAL", visualCommand), ("EDITOR", editorCommand)), "account", "settings")).ShouldSucceed();
        await AssertRowAsync(visual, EditorType, "custom", "$VISUAL");
        await AssertRowAsync(visual, EditorCommand, visualCommand, "$VISUAL");

        (await Sform("account", "settings", "set", EditorType, "vim")).ShouldSucceed();
        var configured = (await Sform(With(("VISUAL", visualCommand)), "account", "settings")).ShouldSucceed();
        await AssertRowAsync(configured, EditorType, "vim", FromSettings);

        var target = (await Sform(With(("STORYTELLER_PROJECT", project), ("STORYTELLER_VIEW", view)), "account", "settings")).ShouldSucceed();
        await AssertRowAsync(target, AccessProject, project, "STORYTELLER_PROJECT");
        await AssertRowAsync(target, AccessView, view, "STORYTELLER_VIEW");

        (await Sform("account", "settings", "set", AccessView, CliData.ViewName())).ShouldSucceed();
        var projectOnly = (await Sform(With(("STORYTELLER_PROJECT", project)), "account", "settings")).ShouldSucceed();
        await AssertRowAsync(projectOnly, AccessProject, project, "STORYTELLER_PROJECT");
        await AssertRowAsync(projectOnly, AccessView, "default", "default (STORYTELLER_PROJECT is set)");

        var invalid = (await Sform(With(("STORYTELLER_PROJECT", "only-org")), "account", "settings")).ShouldSucceed();
        await AssertRowAsync(invalid, AccessProject, "only-org", "STORYTELLER_PROJECT (invalid, not <organization>.<project>)");
        var invalidJson = (await Sform(With(("STORYTELLER_PROJECT", "only-org")), "account", "settings", "--json")).ShouldSucceed().JsonNode();
        await Assert.That(invalidJson[AccessProject]).IsNull();

        var secret = CliData.SecretValue();
        AddSecret(secret);
        var machine = With(("STORYTELLER_CLIENT_ID", CliData.Slug("client")), ("STORYTELLER_CLIENT_SECRET", secret));
        var signedIn = (await Sform(machine, "account", "settings")).ShouldSucceed();
        const string machineSource = "machine sign-in (STORYTELLER_CLIENT_ID, STORYTELLER_CLIENT_SECRET)";
        await AssertRowAsync(signedIn, AccessProject, "(of the machine sign-in)", machineSource);
        await AssertRowAsync(signedIn, AccessView, "(of the machine sign-in)", machineSource);
        var machineJson = (await Sform(machine, "account", "settings", "--json")).ShouldSucceed().JsonNode();
        await Assert.That(machineJson[AccessProject]).IsNull();
        await Assert.That(machineJson[AccessView]).IsNull();
    }

    // ACC-STG-10: access.project and access.view are the target of a command without -p and -v, and
    // STORYTELLER_PROJECT/STORYTELLER_VIEW win over them.
    // An Editor put <rst> into a generated view <view> of the target project. In the leased user's home, account settings
    // set access.project <org>.<project> and set access.view <view>: sform story without -p and -v lists <rst> with 0, and
    // sform account prints "Default project set to <org>.<project> with <view> view."; after account settings unset
    // access.view, story reads the view default (0, without <rst>); after account settings set access.project
    // <org>.<generated>, story ends with 44 and "No Reader access to '<org>.<generated>'."; with STORYTELLER_PROJECT and
    // STORYTELLER_VIEW of the target project and <view>, story lists <rst> again with 0.
    [Test]
    [TestCase(483)]
    [UserRole(Role.Any)]
    [Covers("account settings set", "arg0", "arg1", "arg0=access.project", "arg0=access.view")]
    [Covers("account settings unset", "arg0", "arg0=access.view")]
    public async Task SettingsAccess_TargetsCommandsWithoutContext()
    {
        var view = CliData.ViewName();
        var key = CliData.ResponsibilityKey();
        var editor = await ArrangeAsEditorAsync();
        (await editor.Sform("story", "set", key, "-p", ProjectKey, "-v", view)).ShouldSucceed();

        (await Sform("account", "settings", "set", AccessProject, ProjectKey)).ShouldSucceed();
        (await Sform("account", "settings", "set", AccessView, view)).ShouldSucceed();
        (await Sform("story")).ShouldSucceed().ShouldHaveLine($"- {key}");
        (await Sform("account")).ShouldSucceed().ShouldContain($"Default project set to {ProjectKey} with {view} view.");

        (await Sform("account", "settings", "unset", AccessView)).ShouldSucceed();
        (await Sform("story")).ShouldSucceed().ShouldNotContain(key);

        var generated = $"{Organization}.{CliData.ProjectName()}";
        (await Sform("account", "settings", "set", AccessProject, generated)).ShouldSucceed();
        (await Sform("story"))
            .ShouldExitWith(CliExit.Unauthorized)
            .ShouldContain($"No Reader access to '{generated}'.");

        (await Sform(With(("STORYTELLER_PROJECT", ProjectKey), ("STORYTELLER_VIEW", view)), "story"))
            .ShouldSucceed()
            .ShouldHaveLine($"- {key}");
    }

    // ACC-STG-11: An edit opens the editor of account settings editor, $EDITOR when none is set, and the configured editor
    // before $VISUAL.
    // As an Editor, with <rst> and its configuration (a=1) and no editor in the home: account settings editor custom
    // --command <the fake editor>, then config edit <rst> -y (the editor merges a=2) opens it once with a=1 and saves,
    // without "Using $"; after account settings unset editor.type, config edit <rst> -y with EDITOR=<the fake editor> (a=3)
    // says "Using $EDITOR (<the fake editor>); change it with sform account settings editor." and opens it with a=2; after
    // account settings set editor.type custom (the command stayed in the settings), config edit <rst> -y with VISUAL=<a
    // program that does not exist> (a=4) opens the fake editor with a=3, not $VISUAL; story config <rst> --content-only
    // has a=4.
    [Test]
    [TestCase(484)]
    [UserRole(Role.Editor)]
    [Covers("account settings editor", "arg0", "arg0=custom", "--command")]
    [Covers("account settings set", "arg0", "arg1", "arg0=editor.type")]
    [Covers("account settings unset", "arg0", "arg0=editor.type")]
    public async Task SettingsEditor_OpenedByEdit()
    {
        var key = CliData.ResponsibilityKey();
        (await Sform(InProject("story", "set", key))).ShouldSucceed();
        (await Sform(InProject("story", "config", "set", key, "-x", "a=1"))).ShouldSucceed();
        var editor = new FakeEditor(WorkingDirectory, FileSystem);

        (await Sform("account", "settings", "editor", "custom", "--command", editor.Command))
            .ShouldSucceed()
            .ShouldContain($"Editor set to custom ({editor.Command}).");
        var configured = editor.With(EditorChange.Merge("""{ "a": 2 }"""));
        (await Sform(configured, InProject("story", "config", "edit", key, "-y")))
            .ShouldSucceed()
            .ShouldContain($"Configuration for '{key}' has been saved (version")
            .ShouldNotContain("Using $");
        await Assert.That(editor.ReadLog(configured).Single().OriginalJson()!["a"]!.GetValue<int>()).IsEqualTo(1);

        (await Sform("account", "settings", "unset", EditorType)).ShouldSucceed();
        var fromVariable = editor.With(EditorChange.Merge("""{ "a": 3 }"""), With(("EDITOR", editor.Command)));
        (await Sform(fromVariable, InProject("story", "config", "edit", key, "-y")))
            .ShouldSucceed()
            .ShouldContain($"Using $EDITOR ({editor.Command}); change it with sform account settings editor.")
            .ShouldContain($"Configuration for '{key}' has been saved (version");
        await Assert.That(editor.ReadLog(fromVariable).Single().OriginalJson()!["a"]!.GetValue<int>()).IsEqualTo(2);

        (await Sform("account", "settings", "set", EditorType, "custom")).ShouldSucceed();
        var overVisual = editor.With(EditorChange.Merge("""{ "a": 4 }"""), With(("VISUAL", CliData.Slug("no-such-editor"))));
        (await Sform(overVisual, InProject("story", "config", "edit", key, "-y")))
            .ShouldSucceed()
            .ShouldNotContain("Using $VISUAL")
            .ShouldContain($"Configuration for '{key}' has been saved (version");
        await Assert.That(editor.ReadLog(overVisual).Single().OriginalJson()!["a"]!.GetValue<int>()).IsEqualTo(3);

        var content = (await Sform(InProject("story", "config", key, "--content-only"))).ShouldSucceed().JsonNode();
        await Assert.That(content["a"]?.GetValue<int>()).IsEqualTo(4);
    }

    // The options of a run with these variables.
    private static SformRunOptions With(params (string Name, string Value)[] variables)
    {
        var options = new SformRunOptions();
        foreach (var (name, value) in variables)
        {
            options.Environment[name] = value;
        }

        return options;
    }

    // A section of the home's sform.settings.json (the names as sform writes them, camelCase), or null.
    private static JsonObject? SectionOf(SformHome home, string section)
    {
        return home.ReadSettings()?[section] as JsonObject;
    }

    // A row of the list of sform account settings, "  <key>  <value>  <source>": the table pads its columns with two spaces
    // or more, and a value or a source has single spaces only ("(ask on first use)", "code --wait").
    private static async Task AssertRowAsync(SformResult result, string key, string value, string source)
    {
        var row = result.Lines
            .Select(line => Columns().Split(line.Trim()))
            .FirstOrDefault(cells => cells.Length == 3 && cells[0] == key)
            ?? throw result.Fail($"listed no setting {key} with a value and a source");
        await Assert.That(row[1]).IsEqualTo(value).Because($"the value of {key}");
        await Assert.That(row[2]).IsEqualTo(source).Because($"the source of {key}");
    }

    // The JSON of account settings --json: the value each command uses, null for none.
    private static async Task AssertJsonAsync(JsonNode json, string? editorType, string? editorCommand, string? project, string? view)
    {
        await Assert.That(json[EditorType]?.GetValue<string>()).IsEqualTo(editorType);
        await Assert.That(json[EditorCommand]?.GetValue<string>()).IsEqualTo(editorCommand);
        await Assert.That(json[AccessProject]?.GetValue<string>()).IsEqualTo(project);
        await Assert.That(json[AccessView]?.GetValue<string>()).IsEqualTo(view);
    }

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex Columns();

    // access.default.json of an earlier sform in the home: { "access": { "OrganizationName", "ProjectName", "ViewName" } }.
    private async Task WriteLegacyAccessAsync(string organization, string project, string view)
    {
        Home.Ensure();
        var json = new JsonObject
        {
            ["access"] = new JsonObject { ["OrganizationName"] = organization, ["ProjectName"] = project, ["ViewName"] = view },
        };
        await FileSystem.File.WriteAllTextAsync(Home.AccessDefaultFile, json.ToJsonString());
    }

    // ACC-STG-07: account settings editor in a terminal, "Custom command" picked by typing into the list, and the command
    // entered (an empty one is Enter alone).
    private async Task<SformResult> PickCustomEditorAsync(string command)
    {
        await using var terminal = await StartSformInTerminal("account", "settings", "editor");
        await terminal.ExpectAsync("Select your preferred editor for JSON configuration editing", PromptTimeout);
        await terminal.SendAsync("Custom");
        await terminal.ExpectAsync("Custom command", PromptTimeout);
        await terminal.SendKeyAsync(TerminalKey.Enter);
        await terminal.ExpectAsync("Enter the command to launch your editor", PromptTimeout);
        if (command.Length == 0)
        {
            await terminal.SendKeyAsync(TerminalKey.Enter);
        }
        else
        {
            await terminal.SendLineAsync(command);
        }

        return await terminal.WaitForExitAsync();
    }
}

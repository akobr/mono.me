using System.Reflection;
using System.Text;
using _42.Platform.Cli.AutomationTests.Coverage;

namespace _42.Platform.Cli.AutomationTests.SelfTests;

// The rules of the command coverage and of the catalogue check on a small command tree built by hand (no sform, no
// catalogue): what a [Covers] path covers (aliases in the middle or at the end, help-only groups, the implicit -p of a
// context command), values in any form and case, the problems of a [Covers], the checks of coverage-exceptions.json and
// coverage-values.json, the strict loading of both files, and CatalogueCovers on rows like the catalogue's (a context case
// that lists a token once for many commands, a token listed twice for two commands).
[Category(CliCategories.SelfTest)]
public sealed class CoverageRulesSelfTests
{
    private const string Level = "Selftest.Level";
    private const string Mode = "Selftest.Mode";

    private static readonly MethodInfo AnyTest = typeof(CoverageRulesSelfTests).GetMethod(nameof(Resolve_AliasInThePath_CoversTheAliasAndTheLeaf))!;

    [Test]
    public async Task Resolve_AliasInThePath_CoversTheAliasAndTheLeaf()
    {
        var resolved = Model().Resolve(Usage("story kv set", "arg0"));

        await Assert.That(resolved.IsValid).IsTrue();
        await Assert.That(Keys(resolved)).IsEquivalentTo(new[]
        {
            "storyteller|Alias|story",
            "storyteller kv set|Command|set",
            "storyteller kv set|Option|-p",
            "storyteller kv set|Argument|arg0",
        });
        await Assert.That(resolved.CommandTokens).IsEquivalentTo(new[] { "story", "set" });
    }

    // A path that ends with an alias covers the alias, not the command: "story kv create" is no case of set.
    [Test]
    public async Task Resolve_AliasAtTheEnd_CoversOnlyTheAlias()
    {
        var resolved = Model().Resolve(Usage("story kv create"));

        await Assert.That(Keys(resolved)).Contains("storyteller kv set|Alias|create");
        await Assert.That(Keys(resolved)).DoesNotContain("storyteller kv set|Command|set");
        await Assert.That(Keys(resolved)).DoesNotContain("storyteller kv|Command|kv");
        await Assert.That(resolved.CommandTokens).IsEquivalentTo(new[] { "story", "create" });
        await Assert.That(resolved.LeafWord).IsEqualTo("create");
    }

    [Test]
    public async Task Resolve_HelpOnlyGroups_AreCoveredByAPathThroughThem()
    {
        var model = Model();

        var typeSet = Keys(model.Resolve(Usage("storyteller schema type set")));
        await Assert.That(typeSet).Contains("storyteller schema|Command|schema");
        await Assert.That(typeSet).Contains("storyteller schema type|Command|type");
        await Assert.That(typeSet).Contains("storyteller schema type set|Command|set");
        await Assert.That(typeSet).DoesNotContain("storyteller|Command|storyteller");

        await Assert.That(Keys(model.Resolve(Usage("sform")))).Contains("sform|Command|sform");
        await Assert.That(Keys(model.Resolve(Usage("sform", "--version")))).Contains("sform|Option|--version");
    }

    // -p of BaseContextCommand counts with any [Covers] of a context command; account set's own -p does not.
    [Test]
    public async Task Resolve_ImplicitProjectOption_OnlyOnContextCommands()
    {
        var model = Model();

        await Assert.That(Keys(model.Resolve(Usage("story kv get")))).Contains("storyteller kv get|Option|-p");
        await Assert.That(Keys(model.Resolve(Usage("story kv get")))).DoesNotContain("storyteller kv get|Option|--projectKey");
        await Assert.That(Keys(model.Resolve(Usage("account set")))).DoesNotContain("account set|Option|-p");
        await Assert.That(Keys(model.Resolve(Usage("account set", "-p")))).Contains("account set|Option|-p");
    }

    [Test]
    public async Task Resolve_Values_AnyFormAnyCase_AndProblems()
    {
        var model = Model();

        var value = model.Resolve(Usage("story kv set", "-l=PUBLIC"));
        await Assert.That(value.IsValid).IsTrue();
        await Assert.That(Keys(value)).Contains("storyteller kv set|Value|--protection=public");

        await Assert.That(model.Resolve(Usage("story kv set", "--protection=hyok")).Problems.Single()).Contains("'hyok' is not a value of --protection");
        await Assert.That(model.Resolve(Usage("story kv get", "--view=x")).Problems.Single()).Contains("has no fixed set of values");
        await Assert.That(model.Resolve(Usage("story kv set", "arg2")).Problems.Single()).Contains("takes 2 argument(s)");
        await Assert.That(model.Resolve(Usage("story kv set", "--nope")).Problems.Single()).Contains("has no option --nope");
        await Assert.That(model.Resolve(Usage("story kv nope")).Problems.Single()).Contains("'nope' is no subcommand of 'storyteller kv'");
        await Assert.That(model.Resolve(Usage("story kv set", "create")).Problems).IsEmpty();
    }

    // coverage-exceptions.json against the tests: gated-only items must be listed, and an entry is stale when it names
    // nothing, has no reason, names an unknown feature, is listed twice, is covered by a test that runs everywhere, or names
    // another feature than its tests; an entry whose item no test covers yet is fine.
    [Test]
    public async Task Evaluate_Exceptions_ListedUnlistedAndStale()
    {
        var model = Model();
        var usages = new[]
        {
            Usage("story kv set"),
            Usage("story kv protect"),
            Usage("story kv set", ["--protection=byok"], features: [CliFeatures.Byok]),
            Usage("story kv protect", ["--protection=hsm"], features: [CliFeatures.Hsm]),
            Usage("story kv get", ["arg0"]),
            Usage("story kv set", ["--protection=standard"], features: [CliFeatures.Byok]),
            Usage("story kv set", ["--secret"], skipped: true),
        };
        var exceptions = new CoverageExceptions
        {
            Exceptions =
            [
                Exception("storyteller kv set", "--protection=byok", CliFeatures.Byok),
                Exception("story kv set", "--protection=byok", CliFeatures.Byok),
                Exception("storyteller kv get", "arg0", null),
                Exception("storyteller kv set", "--protection=standard", CliFeatures.Hsm),
                Exception("storyteller kv protect", "--protection=byok", CliFeatures.Byok),
                Exception("storyteller kv protect", "arg0", CliFeatures.Byok) with { Reason = null },
                Exception("storyteller kv", "--prefix", "Bring"),
                Exception("storyteller kv nope", null, null),
            ],
        };

        var report = model.Evaluate(usages, exceptions);

        await Assert.That(report.InvalidCovers).IsEmpty();
        await Assert.That(report.UnlistedExceptions.Select(item => item.Key)).IsEquivalentTo(new[]
        {
            "storyteller kv set|Option|--secret",
            "storyteller kv protect|Value|--protection=hsm",
        });
        var stale = string.Join('\n', report.StaleExceptions);
        await Assert.That(stale).Contains("story kv set --protection=byok: listed twice.");
        await Assert.That(stale).Contains("storyteller kv get arg0: covered by");
        await Assert.That(stale).Contains("storyteller kv set --protection=standard: listed for the feature Hsm");
        await Assert.That(stale).Contains("storyteller kv protect arg0: no reason.");
        await Assert.That(stale).Contains("the feature 'Bring' is none of CliFeatures");
        await Assert.That(stale).Contains("sform has no command 'storyteller kv nope'");
        await Assert.That(stale).DoesNotContain("storyteller kv protect --protection=byok:");
        await Assert.That(report.StaleExceptions.Count).IsEqualTo(6);
        await Assert.That(report.Uncovered.Select(item => item.Key)).Contains("storyteller kv protect|Value|--protection=byok");
    }

    // coverage-values.json against the CLI: an alias path (which For would never find), drift from the enum, unknown or
    // doubled "except" and "refused", "except" without an enum, and an enum-typed option without an entry.
    [Test]
    public async Task CheckValues_ReportsAliasPathsAndDrift()
    {
        var values = new CoverageValues
        {
            Options =
            [
                Values("story kv set", "--protection", ["public", "standard", "hsm", "byok"], refused: ["Hyok"]),
                Values("storyteller kv protect", "--protection", ["standard", "byok"], except: ["Public", "Hyok"]),
                Values("storyteller kv", "--protection", ["public", "standard", "hsm", "byok"], refused: ["Secret"]),
                Values("storyteller config", "--format", ["json"], except: ["Hyok"], enumType: null),
            ],
        };

        var problems = string.Join('\n', new CoverageModel(Tree(), values).CheckValues());

        await Assert.That(problems).Contains("story kv set --protection: write the command's canonical path, \"storyteller kv set\"");
        await Assert.That(problems).Contains("storyteller kv protect --protection: the values differ from the members of Selftest.Level except Public, Hyok: missing Hsm.");
        await Assert.That(problems).Contains("storyteller kv --protection: \"refused\" names Secret");
        await Assert.That(problems).Contains("storyteller kv --protection: the values differ");
        await Assert.That(problems).Contains("storyteller config --format: \"except\", \"refused\" and \"extra\" are members of an enum");
        await Assert.That(problems).Contains("storyteller kv set --mode: its type is the enum Selftest.Mode");

        var both = new CoverageValues { Options = [Values("storyteller kv set", "--protection", ["standard", "hsm", "byok"], except: ["Public"], refused: ["Public", "Hyok"])] };
        await Assert.That(string.Join('\n', new CoverageModel(Tree(), both).CheckValues())).Contains("Public in both \"except\"");

        await Assert.That(Model().CheckValues()).IsEmpty();
    }

    // A positional argument with a fixed set ("argument": "arg0"): each value is an item "arg0=value", in any case, listed in
    // the catalogue as written there; an entry names exactly one option or argument the command has, once, and an enum it
    // names is checked like an option's.
    [Test]
    public async Task ArgumentValues_ResolveCheckAndCompare()
    {
        var model = new CoverageModel(Tree(), new CoverageValues
        {
            Options =
            [
                ArgumentValues("storyteller kv get", "arg0", ["public", "standard"], enumType: null),
                ArgumentValues("storyteller kv protect", "arg0", ["public", "standard", "hsm", "byok"], refused: ["Hyok"]),
                Values("storyteller kv set", "--mode", ["merge", "replace"], enumType: null),
            ],
        });

        await Assert.That(model.CheckValues()).IsEmpty();
        await Assert.That(model.Items.Select(item => item.Key)).Contains("storyteller kv get|Value|arg0=public");
        await Assert.That(model.Items.Select(item => item.Key)).Contains("storyteller kv protect|Value|arg0=byok");

        var value = model.Resolve(Usage("story kv get", "arg0", "arg0=STANDARD"));
        await Assert.That(value.IsValid).IsTrue();
        await Assert.That(Keys(value)).Contains("storyteller kv get|Value|arg0=standard");
        await Assert.That(model.Resolve(Usage("story kv get", "arg0=hsm")).Problems.Single()).Contains("'hsm' is not a value of arg0 of 'storyteller kv get'");
        await Assert.That(model.Resolve(Usage("story kv set", "arg0=x")).Problems.Single()).Contains("arg0 of 'storyteller kv set' has no fixed set of values");
        await Assert.That(model.Resolve(Usage("story kv get", "arg1=x")).Problems.Single()).Contains("takes 1 argument(s)");

        await Assert.That(CatalogueCovers.IsItem("arg0=public")).IsTrue();
        await Assert.That(CatalogueCovers.Compare(Row("get", "arg0", "arg0=standard"), Resolve(model, Usage("story kv get", "arg0", "arg0=Standard")))).IsEmpty();
        var missing = CatalogueCovers.Compare(Row("get", "arg0", "arg0=public"), Resolve(model, Usage("story kv get", "arg0")));
        await Assert.That(missing.Single()).Contains("the row lists `arg0=public`, no [Covers] does.");

        var wrong = new CoverageValues
        {
            Options =
            [
                ArgumentValues("storyteller kv get", "arg1", ["a"], enumType: null),
                ArgumentValues("storyteller kv protect", "arg0", ["public", "standard"]),
                ArgumentValues("storyteller kv set", "arg0", ["a"], enumType: null) with { Option = "--secret" },
                ArgumentValues("storyteller kv set", "arg1", ["a"], enumType: null),
                ArgumentValues("storyteller kv set", "arg1", ["b"], enumType: null),
            ],
        };
        var problems = string.Join('\n', new CoverageModel(Tree(), wrong).CheckValues());
        await Assert.That(problems).Contains("storyteller kv get arg1: 'storyteller kv get' has no argument arg1 (its arguments: arg0 (Key)).");
        await Assert.That(problems).Contains("storyteller kv protect arg0: the values differ from the members of Selftest.Level: missing Hsm, Byok, Hyok.");
        await Assert.That(problems).Contains("name either an \"option\" or an \"argument\"");
        await Assert.That(problems).Contains("storyteller kv set arg1: the argument is listed twice.");
    }

    // A misspelled field fails the load with the file's name and the field, "$comment" is welcome at both levels.
    [Test]
    public async Task Load_UnknownField_Fails_CommentsAllowed()
    {
        using var sandbox = new SelfTestSandbox();
        var fileSystem = sandbox.FileSystem;
        var good = fileSystem.Path.Combine(sandbox.WorkingDirectory, "good.json");
        fileSystem.File.WriteAllText(good, """{ "$comment": "x", "exceptions": [ { "$comment": "y", "command": "storyteller kv set", "item": "--protection=byok", "feature": "Byok", "reason": "z" } ] }""", new UTF8Encoding(false));
        var typo = fileSystem.Path.Combine(sandbox.WorkingDirectory, "typo.json");
        fileSystem.File.WriteAllText(typo, """{ "exceptions": [ { "command": "storyteller kv set", "item": "--protection=byok", "feture": "Byok", "reason": "z" } ] }""", new UTF8Encoding(false));
        var values = fileSystem.Path.Combine(sandbox.WorkingDirectory, "values.json");
        fileSystem.File.WriteAllText(values, """{ "options": [ { "command": "storyteller kv set", "option": "--protection", "values": [ "public" ], "source": "s", "excpet": [ "Hyok" ] } ] }""", new UTF8Encoding(false));

        await Assert.That(CoverageExceptions.Load(good, fileSystem).Exceptions.Single().Feature).IsEqualTo("Byok");
        var exception = await Assert.That(() => CoverageExceptions.Load(typo, fileSystem)).Throws<InvalidOperationException>();
        await Assert.That(exception!.Message).Contains("feture");
        await Assert.That(exception.Message).Contains(CoverageExceptions.FileName);
        var valuesException = await Assert.That(() => CoverageValues.Load(values, fileSystem)).Throws<InvalidOperationException>();
        await Assert.That(valuesException!.Message).Contains("excpet");
    }

    // A context case lists --projectKey, -v and --view once and covers them on every command it runs.
    [Test]
    public async Task CatalogueCovers_ContextRow_OneTokenOnManyCommands()
    {
        var model = Model();
        var row = Row("--projectKey", "-v", "--view");

        var differences = CatalogueCovers.Compare(row, Resolve(
            model,
            Usage("story kv set", "--projectKey", "-v", "--view"),
            Usage("story kv get", "--projectKey", "-v", "--view"),
            Usage("story kv protect", "--projectKey", "--view"),
            Usage("story config", "--projectKey", "-v")));

        await Assert.That(differences).IsEmpty();
    }

    // KV-POL-07: --protection and --protection=byok once in the row, on kv set and kv protect in the steps.
    [Test]
    public async Task CatalogueCovers_ValueListedOnce_CoveredOnSeveralCommands()
    {
        var differences = CatalogueCovers.Compare(
            Row("--protection", "--protection=byok"),
            Resolve(Model(), Usage("story kv set", "--protection", "--protection=byok"), Usage("story kv protect", "--protection", "--protection=byok")));

        await Assert.That(differences).IsEmpty();
    }

    // A token the row lists twice (arg0 of set and of get) needs two commands; two paths to one command are one.
    [Test]
    public async Task CatalogueCovers_TokenListedTwice_NeedsTwoCommands()
    {
        var model = Model();
        var row = Row("set", "arg0", "get", "arg0");

        await Assert.That(CatalogueCovers.Compare(row, Resolve(model, Usage("story kv set", "arg0"), Usage("story kv get", "arg0")))).IsEmpty();

        var short1 = CatalogueCovers.Compare(row, Resolve(model, Usage("story kv set", "arg0"), Usage("story kv get")));
        await Assert.That(short1.Single()).Contains("the row lists `arg0` 2 times (once per command that uses it), the [Covers] of 1 command(s) list it.");

        var oneCommand = CatalogueCovers.Compare(
            Row("config", "configuration", "arg0", "arg0"),
            Resolve(model, Usage("story config", "arg0"), Usage("story configuration", "arg0")));
        await Assert.That(oneCommand.Single()).Contains("the row lists `arg0` 2 times");
        await Assert.That(CatalogueCovers.Compare(Row("config", "configuration", "arg0"), Resolve(model, Usage("story config", "arg0"), Usage("story configuration", "arg0")))).IsEmpty();
    }

    [Test]
    public async Task CatalogueCovers_Mismatches()
    {
        var model = Model();

        var extra = CatalogueCovers.Compare(Row("set", "arg0"), Resolve(model, Usage("story kv set", "arg0", "--secret")));
        await Assert.That(extra.Single()).Contains("[Covers] list `--secret`, the row does not.");

        var missing = CatalogueCovers.Compare(Row("set", "arg0", "-s"), Resolve(model, Usage("story kv set", "arg0")));
        await Assert.That(missing.Single()).Contains("the row lists `-s`, no [Covers] does.");

        var claim = CatalogueCovers.Compare(Row("set"), Resolve(model, Usage("story kv set"), Usage("story kv get")));
        await Assert.That(claim.Single()).Contains("covers `get`, which the row does not list.");

        var command = CatalogueCovers.Compare(Row("create", "arg0"), Resolve(model, Usage("story kv set", "arg0")));
        await Assert.That(command.Single()).Contains("the row lists `create`, which no [Covers] covers");

        var helpOnly = CatalogueCovers.Compare(Row("schema", "type", "set", "arg0"), Resolve(model, Usage("story schema type set", "arg0")));
        await Assert.That(helpOnly).IsEmpty();

        // A value compares as the catalogue writes it (long form, the value of coverage-values.json); a form does not.
        var value = CatalogueCovers.Compare(Row("--protection", "--protection=public"), Resolve(model, Usage("story kv set", "--protection", "-l=PUBLIC")));
        await Assert.That(value).IsEmpty();
        var form = CatalogueCovers.Compare(Row("--protection"), Resolve(model, Usage("story kv set", "-l")));
        await Assert.That(form.Count).IsEqualTo(2);

        var invalid = CatalogueCovers.Compare(Row("set"), Resolve(model, Usage("story kv set", "--nope")));
        await Assert.That(string.Join('\n', invalid)).Contains("has no option --nope");

        await Assert.That(CatalogueCovers.Compare(Row("set"), []).Single()).Contains("[Covers] is missing");
    }

    // sform
    //   storyteller (story)             context, arg0
    //     config (configuration)        context, -f|--format, arg0
    //     kv (key-values)               context, --protection, --prefix
    //       set (create)                context, -l|--protection, -s|--secret, --mode (an enum), arg0, arg1
    //       get                         context, arg0
    //       protect                     context, --protection, arg0
    //     schema                        help only, context
    //       type                        help only, context
    //         set                       context, arg0
    //   account (access, login)         -o|--organization
    //     set                           its own -p|--project
    private static CommandTree Tree()
    {
        var root = new CliCommand(CliCommand.RootName, [], "Selftest.PlatformCommand", null) { IsHelpOnly = true, Options = [Option("Selftest.PlatformCommand", "--version")] };
        var story = new CliCommand("storyteller", ["story"], "Selftest.StorytellerCommand", root) { Options = Context(), Arguments = [Argument(0, "Key")] };
        _ = new CliCommand("config", ["configuration"], "Selftest.ConfigCommand", story) { Options = [.. Context(), Option("Selftest.ConfigCommand", "-f", "--format")], Arguments = [Argument(0, "Key")] };
        var kv = new CliCommand("kv", ["key-values"], "Selftest.KvCommand", story) { Options = [.. Context(), Option("Selftest.KvCommand", "--protection"), Option("Selftest.KvCommand", "--prefix")] };
        _ = new CliCommand("set", ["create"], "Selftest.KvSetCommand", kv)
        {
            Options = [.. Context(), Option("Selftest.KvSetCommand", "-l", "--protection"), Option("Selftest.KvSetCommand", "-s", "--secret"), new CliOption(["--mode"], "Selftest.KvSetCommand", "Mode", Mode, Mode, ["Merge", "Replace"])],
            Arguments = [Argument(0, "Key"), Argument(1, "Value")],
        };
        _ = new CliCommand("get", [], "Selftest.KvGetCommand", kv) { Options = Context(), Arguments = [Argument(0, "Key")] };
        _ = new CliCommand("protect", [], "Selftest.KvProtectCommand", kv) { Options = [.. Context(), Option("Selftest.KvProtectCommand", "--protection")], Arguments = [Argument(0, "Key")] };
        var schema = new CliCommand("schema", [], "Selftest.SchemaCommand", story) { IsHelpOnly = true, Options = Context() };
        var type = new CliCommand("type", [], "Selftest.SchemaTypeCommand", schema) { IsHelpOnly = true, Options = Context() };
        _ = new CliCommand("set", [], "Selftest.SchemaTypeSetCommand", type) { Options = Context(), Arguments = [Argument(0, "Key")] };
        var account = new CliCommand("account", ["access", "login"], "Selftest.AccountCommand", root) { Options = [Option("Selftest.AccountCommand", "-o", "--organization")] };
        _ = new CliCommand("set", [], "Selftest.AccountSetCommand", account) { Options = [Option("Selftest.AccountSetCommand", "-p", "--project")] };

        return new CommandTree(root, "selftest.dll", new Dictionary<string, IReadOnlyList<string>?>(StringComparer.Ordinal)
        {
            [Level] = ["Public", "Standard", "Hsm", "Byok", "Hyok"],
        });
    }

    private static CoverageModel Model()
    {
        return new CoverageModel(Tree(), new CoverageValues
        {
            Options =
            [
                Values("storyteller kv set", "--protection", ["public", "standard", "hsm", "byok"], refused: ["Hyok"]),
                Values("storyteller kv protect", "--protection", ["standard", "hsm", "byok"], except: ["Public", "Hyok"]),
                Values("storyteller kv set", "--mode", ["merge", "replace"], enumType: null),
            ],
        });
    }

    private static IReadOnlyList<CliOption> Context()
    {
        return [Option(CommandTree.ContextCommandType, "-p", "--projectKey"), Option(CommandTree.ContextCommandType, "-v", "--view")];
    }

    private static CliOption Option(string declaringType, params string[] forms)
    {
        return new CliOption(forms, declaringType, "Member", "string?", null, null);
    }

    private static CliArgument Argument(int index, string name)
    {
        return new CliArgument(index, name, "Selftest.Command", name);
    }

    private static CoverageValues.Entry Values(string command, string option, string[] values, string[]? except = null, string[]? refused = null, string? enumType = Level)
    {
        return new CoverageValues.Entry { Command = command, Option = option, Values = values, Enum = enumType, Except = except ?? [], Refused = refused ?? [], Source = "selftest" };
    }

    private static CoverageValues.Entry ArgumentValues(string command, string argument, string[] values, string[]? refused = null, string? enumType = Level)
    {
        return new CoverageValues.Entry { Command = command, Argument = argument, Values = values, Enum = enumType, Refused = refused ?? [], Source = "selftest" };
    }

    private static CoverageExceptions.Entry Exception(string command, string? item, string? feature)
    {
        return new CoverageExceptions.Entry { Command = command, Item = item, Feature = feature, Reason = "selftest" };
    }

    private static CoversUsage Usage(string command, params string[] items)
    {
        return Usage(command, items, features: [], skipped: false);
    }

    private static CoversUsage Usage(string command, string[] items, string[]? features = null, bool skipped = false)
    {
        return new CoversUsage(typeof(CoverageRulesSelfTests), AnyTest, new CoversAttribute(command, items), IsTest: true, features ?? [], skipped);
    }

    private static List<ResolvedCovers> Resolve(CoverageModel model, params CoversUsage[] usages)
    {
        return [.. usages.Select(model.Resolve)];
    }

    private static string[] Keys(ResolvedCovers resolved)
    {
        return [.. resolved.Items.Select(item => item.Key)];
    }

    private static CatalogueCase Row(params string[] covers)
    {
        return new CatalogueCase
        {
            Reference = "SLF-TST-01",
            Testiny = "1",
            Title = "A self test row",
            Priority = "Medium",
            Role = "Any",
            Modes = [],
            Covers = covers,
            Method = "Selftest",
            Section = new CatalogueSection("Self", "Tests/Self", 1),
            SubFolder = "Self",
            ClassName = "SelfTests",
            Line = 1,
        };
    }
}

using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using _42.Platform.Cli.AutomationTests.Data;
using _42.Platform.Cli.AutomationTests.Running;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;

namespace _42.Platform.Cli.AutomationTests.Tests.Annotations;

// Delete, Restore and History: sform story delete (remove), restore, deleted (trash) and history.
// A case that checks what story or story deleted lists works in a view of its own (a view exists by its data): the
// default view of Target:Project holds more than a page of every run's annotations, and its trash those of every case
// that deletes.
[Area(Area.Annotations)]
public sealed partial class AnnotationDeleteTests : CliTest
{
    // More than a page of 1000 (the page of story deleted) below one responsibility, deleted with it.
    private const int BulkUnitCount = 1001;

    private static readonly TimeSpan BulkTimeout = TimeSpan.FromMinutes(25);

    // ANN-DEL-01: story delete soft-deletes an annotation with its descendants.
    // As an Editor, in a view of its own: story delete <rst> of <rst> with a unit says "Deleted '<rst>' and 1
    // descendant." and how to restore it; story no longer lists either; story deleted lists <rst> with 1 descendant and
    // not the unit; story deleted -a lists the unit too.
    [Test]
    [TestCase(309)]
    [UserRole(Role.Editor)]
    [Covers("story delete", "arg0")]
    public async Task Story_Delete_SoftDeletesTree()
    {
        UseProject(ProjectKey, CliData.ViewName());
        var key = CliData.ResponsibilityKey();
        var unit = CliData.UnitKeyOf(key);
        await Sform(InProject("story", "set", key)).ShouldSucceed();
        await Sform(InProject("story", "set", unit)).ShouldSucceed();

        (await Sform(InProject("story", "delete", key)))
            .ShouldSucceed()
            .ShouldContain($"Deleted '{key}' and 1 descendant.")
            .ShouldContain($"sform story restore {key}");

        var list = (await Sform(InProject("story"))).ShouldSucceed();
        await Assert.That(AnnotationOutput.Keys(list)).IsEmpty();

        var trash = (await Sform(InProject("story", "deleted"))).ShouldSucceed();
        await Assert.That(AnnotationOutput.TableKeys(trash)).IsEquivalentTo(new[] { key });
        await Assert.That(DescendantsColumn().IsMatch($" {AnnotationOutput.RowOf(trash, key)} ")).IsTrue();

        var all = (await Sform(InProject("story", "deleted", "-a"))).ShouldSucceed();
        await Assert.That(AnnotationOutput.TableKeys(all)).IsEquivalentTo(new[] { key, unit });
    }

    // ANN-DEL-02: story remove is an alias of delete.
    // As an Editor, in a view of its own: story remove <rst2> says "Deleted '<rst2>'."; story no longer lists it, and story
    // deleted does.
    [Test]
    [TestCase(310)]
    [UserRole(Role.Editor)]
    [Covers("story remove")]
    public async Task Story_RemoveAlias_Deletes()
    {
        UseProject(ProjectKey, CliData.ViewName());
        var key = CliData.ResponsibilityKey();
        await Sform(InProject("story", "set", key)).ShouldSucceed();

        (await Sform(InProject("story", "remove", key))).ShouldSucceed().ShouldContain($"Deleted '{key}'.");

        await Assert.That(AnnotationOutput.Keys((await Sform(InProject("story"))).ShouldSucceed())).IsEmpty();
        await Assert.That(AnnotationOutput.TableKeys((await Sform(InProject("story", "deleted"))).ShouldSucceed())).IsEquivalentTo(new[] { key });
    }

    // ANN-DEL-03: --permanent -y deletes for good as an Administrator.
    // As an Administrator, in a view of its own: story delete <rst> --permanent -y and story delete <rst2> --permanent
    // --yes say "Deleted '<key>' permanently."; story deleted lists neither ("No annotation is deleted."), and story
    // restore of either ends with 2.
    [Test]
    [TestCase(311)]
    [UserRole(Role.Admin)]
    [Covers("story delete", "--permanent", "-y", "--yes")]
    public async Task Story_PermanentDelete_Admin()
    {
        UseProject(ProjectKey, CliData.ViewName());
        var keys = new[] { CliData.ResponsibilityKey(), CliData.ResponsibilityKey() };
        var confirmations = new[] { "-y", "--yes" };
        for (var index = 0; index < keys.Length; index++)
        {
            await Sform(InProject("story", "set", keys[index])).ShouldSucceed();
            (await Sform(InProject("story", "delete", keys[index], "--permanent", confirmations[index])))
                .ShouldSucceed()
                .ShouldContain($"Deleted '{keys[index]}' permanently.");
        }

        var trash = (await Sform(InProject("story", "deleted"))).ShouldSucceed().ShouldContain("No annotation is deleted.");
        await Assert.That(AnnotationOutput.TableKeys(trash)).IsEmpty();

        foreach (var key in keys)
        {
            (await Sform(InProject("story", "restore", key))).ShouldExitWith(CliExit.WrongInput);
            (await Sform(InProject("story", "get", key)))
                .ShouldExitWith(CliExit.WrongInput)
                .ShouldContain($"The annotation '{key}' has not been found.");
        }
    }

    // ANN-DEL-04: --permanent asks for a confirmation in a terminal; No aborts with exit 40.
    // As an Administrator, story delete <rst> --permanent of <rst> with a unit, in a terminal, asks "Delete '<rst>' and its
    // 1 descendant permanently? It can't be restored"; No prints "Nothing was deleted." and ends with 40, and story get
    // <rst> still reads it. Again with Yes: "Deleted '<rst>' and 1 descendant permanently.", and <rst> is gone.
    [Test]
    [TestCase(313)]
    [UserRole(Role.Admin)]
    [Terminal]
    [Covers("story delete", "--permanent")]
    public async Task Story_PermanentDelete_ConfirmOrAbort()
    {
        var key = CliData.ResponsibilityKey();
        await Sform(InProject("story", "set", key)).ShouldSucceed();
        await Sform(InProject("story", "set", CliData.UnitKeyOf(key))).ShouldSucceed();

        await using (var declined = await StartSformInTerminal(InProject("story", "delete", key, "--permanent")))
        {
            await declined.ExpectAsync("and its 1 descendant permanently? It can't be restored");
            await declined.SendLineAsync("n");
            await declined.ExpectAsync("Nothing was deleted.");
            (await declined.WaitForExitAsync()).ShouldExitWith(CliExit.Aborted);
        }

        (await Sform(InProject("story", "get", key))).ShouldSucceed();

        await using (var confirmed = await StartSformInTerminal(InProject("story", "delete", key, "--permanent")))
        {
            await confirmed.ExpectAsync("and its 1 descendant permanently? It can't be restored");
            await confirmed.SendLineAsync("y");
            await confirmed.ExpectAsync($"Deleted '{key}' and 1 descendant permanently.");
            (await confirmed.WaitForExitAsync()).ShouldSucceed();
        }

        (await Sform(InProject("story", "get", key)))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain($"The annotation '{key}' has not been found.");
    }

    // ANN-DEL-05: --permanent without -y on redirected input ends with exit 43, and a Contributor's permanent delete fails
    // with exit 44.
    // As a Contributor (of Target:RoleProjects:Contributor or Target:Project), story delete <rst> --permanent of an
    // existing <rst> on the redirected input ends with 43 at once, and its standard error has "! sform cannot ask for the
    // confirmation of the permanent delete: the input is redirected." and "Pass -y|--yes to delete without asking.";
    // <rst> stays. story delete <rst> --permanent -y ends with 44: the server's "No Administrator access to '<project>'.",
    // "Error code: AccessDenied" and "Only an Administrator or Owner can delete permanently."; <rst> still stays.
    [Test]
    [TestCase(315)]
    [UserRole(Role.Contributor)]
    [Covers("story delete", "--permanent")]
    public async Task Story_PermanentDelete_RedirectedOrContributor()
    {
        var key = CliData.ResponsibilityKey();
        await Sform(InProject("story", "set", key)).ShouldSucceed();

        (await Sform(new SformRunOptions { Timeout = TimeSpan.FromSeconds(60) }, InProject("story", "delete", key, "--permanent")))
            .ShouldExitWith(CliExit.InteractionNeeded)
            .ShouldContain("! sform cannot ask for the confirmation of the permanent delete: the input is redirected.", SformStream.StdErr)
            .ShouldContain("Pass -y|--yes to delete without asking.", SformStream.StdErr);
        (await Sform(InProject("story", "get", key))).ShouldSucceed();

        (await Sform(InProject("story", "delete", key, "--permanent", "-y")))
            .ShouldExitWith(CliExit.Unauthorized)
            .ShouldContain($"No Administrator access to '{ProjectKey}'.")
            .ShouldContain("Error code: AccessDenied")
            .ShouldContain("Only an Administrator or Owner can delete permanently.");
        (await Sform(InProject("story", "get", key))).ShouldSucceed();
    }

    // ANN-DEL-06: story restore restores several keys with their descendants.
    // As an Editor, <rst1> and <rst2>, each with a unit, are deleted; story restore <rst1> <rst2> lists them under
    // "# Restored", and both responsibilities and both units read again (story get).
    [Test]
    [TestCase(316)]
    [UserRole(Role.Editor)]
    [Covers("story restore", "arg0")]
    public async Task Story_Restore_WithDescendants()
    {
        var first = CliData.ResponsibilityKey();
        var second = CliData.ResponsibilityKey();
        var units = new[] { CliData.UnitKeyOf(first), CliData.UnitKeyOf(second) };
        foreach (var key in new[] { first, units[0], second, units[1] })
        {
            await Sform(InProject("story", "set", key)).ShouldSucceed();
        }

        await Sform(InProject("story", "delete", first)).ShouldSucceed();
        await Sform(InProject("story", "delete", second)).ShouldSucceed();

        var restored = (await Sform(InProject("story", "restore", first, second))).ShouldSucceed();
        var listed = AnnotationOutput.Section(restored, "Restored");
        await Assert.That(listed).Contains(first);
        await Assert.That(listed).Contains(second);

        foreach (var key in new[] { first, second, units[0], units[1] })
        {
            var annotation = (await Sform(InProject("story", "get", key))).ShouldSucceed().JsonNode();
            await Assert.That(annotation["AnnotationKey"]!.GetValue<string>()).IsEqualTo(key);
        }
    }

    // ANN-DEL-07: restore --without-descendants keeps the descendants deleted.
    // As an Editor, <rst> with a unit is deleted; story restore <rst> --without-descendants brings <rst> back (story get
    // reads it), while story get of the unit still says it is deleted (exit 2).
    [Test]
    [TestCase(318)]
    [UserRole(Role.Editor)]
    [Covers("story restore", "--without-descendants")]
    public async Task Story_RestoreWithoutDescendants()
    {
        var key = CliData.ResponsibilityKey();
        var unit = CliData.UnitKeyOf(key);
        await Sform(InProject("story", "set", key)).ShouldSucceed();
        await Sform(InProject("story", "set", unit)).ShouldSucceed();
        await Sform(InProject("story", "delete", key)).ShouldSucceed();

        var restored = (await Sform(InProject("story", "restore", key, "--without-descendants"))).ShouldSucceed();
        await Assert.That(AnnotationOutput.Section(restored, "Restored")).IsEquivalentTo(new[] { key });

        (await Sform(InProject("story", "get", key))).ShouldSucceed();
        (await Sform(InProject("story", "get", unit)))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain($"The annotation '{unit}' is deleted (");
    }

    // ANN-DEL-08: story deleted (and trash) lists the deleted keys; -a lists every deleted annotation, -k pages.
    // In a disposable project the Owner imports <rst> with 1001 units and <rst2> (story import of a folder of empty
    // annotation files), deletes <rst2>, then <rst> with its units. story deleted and story trash list the two requested
    // keys, <rst> (the newest deletion, with 1001 descendants) before <rst2>, without a token. story deleted -a and
    // --all list the units too, a page of them with a token; story deleted --all -k <token> and --all
    // --continuation-token <token> print the same next page (the token belongs to the --all query). The pages repeat no
    // key, and with the pages that may follow they hold all 1003 deleted keys.
    [Test]
    [TestCase(320)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("story deleted", "-a", "--all", "-k", "--continuation-token")]
    [Covers("story trash")]
    public async Task Story_Deleted_ListsTrash()
    {
        await CreateDisposableProjectAsync();
        var parent = CliData.ResponsibilityKey();
        var single = CliData.ResponsibilityKey();
        var stem = CliData.AnnotationPart();
        var units = Enumerable.Range(1, BulkUnitCount)
            .Select(index => CliData.AnnotationKey(CliData.Unit, parent["rst.".Length..], $"{stem}-{index:D4}"))
            .ToList();
        foreach (var key in units.Prepend(single).Prepend(parent))
        {
            await WriteFileAsync($"bulk/{key}.json", "{}");
        }

        (await Sform(
            new SformRunOptions { Timeout = BulkTimeout, Label = $"import <rst> with {BulkUnitCount} units" },
            InProject("story", "import", "bulk", "--timeout", "20")))
            .ShouldSucceed();
        await Sform(InProject("story", "delete", single)).ShouldSucceed();
        (await Sform(new SformRunOptions { Timeout = TimeSpan.FromMinutes(10), Label = $"delete <rst> with {BulkUnitCount} units" }, InProject("story", "delete", parent)))
            .ShouldSucceed()
            .ShouldContain($"Deleted '{parent}' and {BulkUnitCount} descendants.");

        foreach (var command in new[] { "deleted", "trash" })
        {
            var roots = (await Sform(InProject("story", command))).ShouldSucceed().ShouldHaveLine("# Deleted annotations");
            await Assert.That(string.Join(' ', AnnotationOutput.TableKeys(roots))).IsEqualTo($"{parent} {single}");
            await Assert.That(AnnotationOutput.RowOf(roots, parent)!).Contains($" {BulkUnitCount} ");
            await Assert.That(AnnotationOutput.ContinuationToken(roots)).IsNull();
        }

        var first = (await Sform(InProject("story", "deleted", "-a"))).ShouldSucceed();
        var token = AnnotationOutput.ContinuationToken(first) ?? throw first.Fail($"printed no continuation token for {BulkUnitCount + 2} deleted annotations");
        var firstPage = AnnotationOutput.TableKeys(first);
        var again = (await Sform(InProject("story", "deleted", "--all"))).ShouldSucceed();
        await Assert.That(AnnotationOutput.TableKeys(again)).IsEquivalentTo(firstPage);
        await Assert.That(AnnotationOutput.ContinuationToken(again)).IsNotNull();

        var second = (await Sform(InProject("story", "deleted", "--all", "-k", token))).ShouldSucceed();
        var secondPage = AnnotationOutput.TableKeys(second);
        var secondAgain = (await Sform(InProject("story", "deleted", "--all", "--continuation-token", token))).ShouldSucceed();
        await Assert.That(AnnotationOutput.TableKeys(secondAgain)).IsEquivalentTo(secondPage);

        var listed = firstPage.Concat(secondPage).ToList();
        var next = AnnotationOutput.ContinuationToken(second);
        for (var page = 0; next is not null && page < 20; page++)
        {
            var more = (await Sform(InProject("story", "deleted", "--all", "-k", next))).ShouldSucceed();
            listed.AddRange(AnnotationOutput.TableKeys(more));
            next = AnnotationOutput.ContinuationToken(more);
        }

        await Assert.That(listed.Count).IsEqualTo(listed.Distinct(StringComparer.Ordinal).Count());
        await Assert.That(listed).IsEquivalentTo(units.Prepend(single).Prepend(parent));
    }

    // ANN-DEL-09: story history shows who changed what; -n limits it and --json writes JSON.
    // An Editor creates <rst> and changes it twice (a label, then a custom property): three entries. As a Reader, story
    // history <rst> prints them under "# History of <rst>", each with its time and who, the newest (Updated) first and
    // Created last; -n 1 shows one entry and "2 older entries are not shown.", --top 2 two entries and "1 older entries
    // are not shown."; --json writes a JSON document of the same three entries (Created, Updated, Updated), each with its
    // time and its actor.
    [Test]
    [TestCase(322)]
    [UserRole(Role.Reader)]
    [Covers("story history", "arg0", "-n", "--top", "--json")]
    public async Task Story_History_ShowsChanges()
    {
        var editor = await ArrangeAsEditorAsync();
        var key = CliData.ResponsibilityKey();
        await editor.Sform(InProject("story", "set", key)).ShouldSucceed();
        await editor.Sform(InProject("story", "set", key, "-l", CliData.LabelName())).ShouldSucceed();
        await editor.Sform(InProject("story", "set", key, "-c", $"probe={CliData.Value()}")).ShouldSucceed();

        var history = (await Sform(InProject("story", "history", key))).ShouldSucceed().ShouldHaveLine($"# History of {key}");
        var rows = AnnotationOutput.HistoryRows(history);
        await Assert.That(rows.Count).IsEqualTo(3);
        await Assert.That(rows[0]).Contains("Updated");
        await Assert.That(rows[^1]).Contains("Created");
        await Assert.That(rows.Select(row => row[..16]).SequenceEqual(rows.Select(row => row[..16]).OrderDescending(StringComparer.Ordinal))).IsTrue();

        var newest = (await Sform(InProject("story", "history", key, "-n", "1"))).ShouldSucceed().ShouldContain("2 older entries are not shown.");
        await Assert.That(AnnotationOutput.HistoryRows(newest).Count).IsEqualTo(1);
        var two = (await Sform(InProject("story", "history", key, "--top", "2"))).ShouldSucceed().ShouldContain("1 older entries are not shown.");
        await Assert.That(AnnotationOutput.HistoryRows(two).Count).IsEqualTo(2);

        var json = (await Sform(InProject("story", "history", key, "--json"))).ShouldSucceed().JsonNode();
        await Assert.That(json["AnnotationKey"]?.GetValue<string>()).IsEqualTo(key);
        var entries = (json["Entries"] as JsonArray)!.Select(entry => entry!).ToList();
        await Assert.That(entries.Select(entry => entry["Action"]!.GetValue<string>())).IsEquivalentTo(new[] { "Created", "Updated", "Updated" });
        foreach (var entry in entries)
        {
            await Assert.That(entry["At"]).IsNotNull();
            await Assert.That(entry["Actor"]?["Kind"]?.GetValue<string>()).IsEqualTo("Account");
        }
    }

    // The Descendants column of a story deleted row holding 1: the number alone between spaces.
    [GeneratedRegex(@"\s1\s", RegexOptions.CultureInvariant)]
    private static partial Regex DescendantsColumn();
}

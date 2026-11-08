using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using _42.Platform.Cli.AutomationTests.Access;
using _42.Platform.Cli.AutomationTests.Data;
using _42.Platform.Cli.AutomationTests.Running;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;

namespace _42.Platform.Cli.AutomationTests.Tests.KeyValues;

// List, Delete and Bulk: sform story kv (the listing and its filters), kv delete, restore, purge, protect, versions and
// diff. A key is segments of letters, digits and '_' joined by '.' (a '/' is refused), so a case puts its keys under a
// generated key <p> (<p>.app.a, the catalogue's notation) and a prefix is "<p>.app.". The listings of a Reader's case
// read a view of their own (a view exists by its data), so they hold exactly the keys the case set.
[Area(Area.KeyValues)]
public sealed class KeyValueBulkTests : CliTest
{
    // The most pairs the API returns on one page (CosmosConstants.MaxItemCountPerPage), and the most items of one import.
    private const int PageSize = 1000;

    // KV-BLK-01: kv (and key-values) lists the keys of the view; --prefix filters.
    // An Editor sets <p>.app.a, <p>.app.b and <p>.other.c in a new view; as a Reader, story kv lists all three,
    // and story key-values --prefix <p>.app. the two app keys.
    [Test]
    [TestCase(386)]
    [UserRole(Role.Reader)]
    [Covers("story kv")]
    [Covers("story key-values", "--prefix")]
    public async Task Kv_List_Prefix()
    {
        var editor = await ArrangeAsEditorAsync();
        var view = CliData.ViewName();
        var root = CliData.KeyValueKey();
        string[] app = [$"{root}.app.a", $"{root}.app.b"];
        string[] keys = [.. app, $"{root}.other.c"];
        foreach (var key in keys)
        {
            (await editor.Sform(InView(view, "story", "kv", "set", key, CliData.Value(), "--protection", "public"))).ShouldSucceed();
        }

        var all = (await Sform(InView(view, "story", "kv"))).ShouldSucceed();
        await Assert.That(Sorted(KeyValuesSupportA.ListedKeys(all, root))).IsEquivalentTo(Sorted(keys));

        var prefixed = (await Sform(InView(view, "story", "key-values", "--prefix", $"{root}.app."))).ShouldSucceed();
        await Assert.That(Sorted(KeyValuesSupportA.ListedKeys(prefixed, root))).IsEquivalentTo(Sorted(app));
    }

    // KV-BLK-02: --protection filters by levels, and --values shows public values with secrets masked.
    // A ContributorWithSecrets sets a public pair and a Standard secret in a new view; as a Reader, story kv --protection
    // public lists only the public key, --protection public --protection standard both, --protection hsm --protection byok
    // --protection hyok neither ("No keys found."), and story kv --values shows the public value and not the secret's.
    [Test]
    [TestCase(387)]
    [UserRole(Role.Reader)]
    [Covers("story kv", "--protection", "--values", "--protection=public", "--protection=standard", "--protection=hsm", "--protection=byok", "--protection=hyok")]
    public async Task Kv_List_LevelsAndValues()
    {
        var holder = await ArrangeAsAsync(SecretsUser);
        var view = CliData.ViewName();
        var root = CliData.KeyValueKey();
        var plainKey = $"{root}.plain";
        var secretKey = $"{root}.hidden";
        var value = CliData.Value();
        var secret = CliData.SecretValue();
        AddSecret(secret);
        (await holder.Sform(InView(view, "story", "kv", "set", plainKey, value, "--protection", "public"))).ShouldSucceed();
        (await holder.Sform(new SformRunOptions { Secrets = [secret] }, InView(view, "story", "kv", "set", secretKey, secret, "--protection", "standard"))).ShouldSucceed();

        var publicOnly = (await Sform(InView(view, "story", "kv", "--protection", "public"))).ShouldSucceed();
        await Assert.That(KeyValuesSupportA.ListedKeys(publicOnly, root).ToArray()).IsEquivalentTo(new[] { plainKey });

        var both = (await Sform(InView(view, "story", "kv", "--protection", "public", "--protection", "standard"))).ShouldSucceed();
        await Assert.That(Sorted(KeyValuesSupportA.ListedKeys(both, root))).IsEquivalentTo(Sorted([plainKey, secretKey]));
        await Assert.That(KeyValuesSupportA.RowOf(both, secretKey)).Contains("Standard");

        var none = (await Sform(InView(view, "story", "kv", "--protection", "hsm", "--protection", "byok", "--protection", "hyok")))
            .ShouldSucceed()
            .ShouldContain("No keys found.");
        await Assert.That(KeyValuesSupportA.ListedKeys(none, root)).IsEmpty();

        var values = (await Sform(InView(view, "story", "kv", "--values"))).ShouldSucceed().ShouldNotContain(secret);
        await Assert.That(KeyValuesSupportA.RowOf(values, plainKey)).Contains(value);
        await Assert.That(KeyValuesSupportA.RowOf(values, secretKey)).IsNotNull();
    }

    // KV-BLK-03: --deleted lists only soft-deleted keys, --all both, -k pages.
    // In a disposable project the Owner imports 1000 public pairs (<p>.page.k0000…k0999), sets <p>.act, and sets
    // and deletes <p>.del.a and <p>.del.b, so the active keys are more than a page. story kv --deleted lists only the
    // two deleted keys with their expiry; story kv --all lists the active and the deleted keys. story kv prints a page
    // and "More keys follow; next page with -k <token>"; story kv -k <token> and --continuation-token <token> print the
    // next page, the same for both, without a key of the first page, and the two pages hold every active key.
    [Test]
    [TestCase(388)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("story kv", "--deleted", "--all", "-k", "--continuation-token")]
    public async Task Kv_List_DeletedAllPages()
    {
        await CreateDisposableProjectAsync();
        var root = CliData.KeyValueKey();
        var active = $"{root}.act";
        string[] deleted = [$"{root}.del.a", $"{root}.del.b"];
        var paged = Enumerable.Range(0, PageSize).Select(index => $"{root}.page.k{index:0000}").ToArray();

        var document = new JsonObject
        {
            ["Format"] = "2s.key-values.v1",
            ["Items"] = new JsonArray(paged.Select(key => (JsonNode)new JsonObject { ["Key"] = key, ["Protection"] = "Public", ["Value"] = "p" }).ToArray()),
        };
        await WriteFileAsync("page.json", document.ToJsonString());
        (await Sform(InProject("story", "kv", "import", "page.json", "-y"))).ShouldSucceed();
        (await Sform(InProject("story", "kv", "set", active, CliData.Value(), "--protection", "public"))).ShouldSucceed();
        foreach (var key in deleted)
        {
            (await Sform(InProject("story", "kv", "set", key, CliData.Value(), "--protection", "public"))).ShouldSucceed();
            (await Sform(InProject("story", "kv", "delete", key))).ShouldSucceed();
        }

        var onlyDeleted = (await Sform(InProject("story", "kv", "--deleted"))).ShouldSucceed();
        await Assert.That(Sorted(KeyValuesSupportA.ListedKeys(onlyDeleted, root))).IsEquivalentTo(Sorted(deleted));
        foreach (var key in deleted)
        {
            await Assert.That(KeyValuesSupportA.RowOf(onlyDeleted, key)).Matches(new Regex(@"expires (in \d+ days|today)", RegexOptions.CultureInvariant));
        }

        // ordered by key: <p>.act and <p>.del.* come before the page keys, so the first page of --all has them
        var all = (await Sform(InProject("story", "kv", "--all"))).ShouldSucceed();
        foreach (var key in deleted.Append(active))
        {
            await Assert.That(KeyValuesSupportA.Lists(all, key)).IsTrue();
        }

        var first = (await Sform(InProject("story", "kv"))).ShouldSucceed();
        var token = KeyValuesSupportA.ContinuationToken(first) ?? throw first.Fail($"printed no continuation token for {PageSize + 1} active keys");
        var firstKeys = KeyValuesSupportA.ListedKeys(first, root);

        var second = (await Sform(InProject("story", "kv", "-k", token))).ShouldSucceed();
        var secondKeys = KeyValuesSupportA.ListedKeys(second, root);
        var again = (await Sform(InProject("story", "kv", "--continuation-token", token))).ShouldSucceed();

        await Assert.That(secondKeys).IsNotEmpty();
        await Assert.That(firstKeys.Intersect(secondKeys, StringComparer.OrdinalIgnoreCase).ToArray()).IsEmpty();
        await Assert.That(Sorted(KeyValuesSupportA.ListedKeys(again, root))).IsEquivalentTo(Sorted(secondKeys));
        await Assert.That(Sorted(firstKeys.Concat(secondKeys))).IsEquivalentTo(Sorted(paged.Append(active)));
    }

    // KV-BLK-04: kv delete (and remove) soft-deletes, with --expected-version.
    // As an Editor, story kv delete <k1> says "'<k1>' was deleted." and the key is soft-deleted (found only with kv get
    // --deleted); story kv remove <k2> --expected-version 1 with <k2> at version 2 fails with exit 2 and leaves it, and
    // story kv remove <k2> --expected-version 2 deletes it.
    [Test]
    [TestCase(389)]
    [UserRole(Role.Editor)]
    [Covers("story kv delete", "arg0")]
    [Covers("story kv remove", "arg0", "--expected-version")]
    public async Task Kv_Delete_SoftDeletes()
    {
        var first = CliData.KeyValueKey();
        var second = CliData.KeyValueKey();
        (await Sform(InProject("story", "kv", "set", first, CliData.Value(), "--protection", "public"))).ShouldSucceed();
        (await Sform(InProject("story", "kv", "set", second, CliData.Value(), "--protection", "public"))).ShouldSucceed();
        (await Sform(InProject("story", "kv", "set", second, CliData.Value()))).ShouldSucceed().ShouldContain($"'{second}' is at version 2 (Public).");

        (await Sform(InProject("story", "kv", "delete", first))).ShouldSucceed().ShouldContain($"'{first}' was deleted.");
        (await Sform(InProject("story", "kv", "get", first))).ShouldExitWith(CliExit.WrongInput);
        await Assert.That(await IsDeletedAsync(first)).IsTrue();

        (await Sform(InProject("story", "kv", "remove", second, "--expected-version", "1"))).ShouldExitWith(CliExit.WrongInput);
        var kept = (await Sform(InProject("story", "kv", "get", second))).ShouldSucceed().JsonNode();
        await Assert.That(kept["Version"]?.GetValue<int>()).IsEqualTo(2);

        (await Sform(InProject("story", "kv", "remove", second, "--expected-version", "2"))).ShouldSucceed().ShouldContain($"'{second}' was deleted.");
        await Assert.That(await IsDeletedAsync(second)).IsTrue();
    }

    // KV-BLK-05: delete --permanent -y removes every version as an Administrator; a terminal asks for the key first.
    // As an Administrator, story kv delete <k> --permanent -y says "'<k>' and every version of it were removed."; story
    // kv delete <k2> --permanent in a pseudo terminal asks to type the key: a wrong text prints "Aborted, nothing was
    // removed." with exit 40 and <k2> stays, typing <k2> removes it; story kv delete <k3> --permanent --yes removes it.
    // The keys are gone from kv --deleted and from kv get --deleted (exit 2).
    [Test]
    [TestCase(390)]
    [UserRole(Role.Admin)]
    [Terminal]
    [Covers("story kv delete", "--permanent", "-y", "--yes")]
    public async Task Kv_PermanentDelete()
    {
        var root = CliData.KeyValueKey();
        string[] keys = [$"{root}.one", $"{root}.two", $"{root}.three"];
        foreach (var key in keys)
        {
            (await Sform(InProject("story", "kv", "set", key, CliData.Value(), "--protection", "public"))).ShouldSucceed();
        }

        (await Sform(InProject("story", "kv", "delete", keys[0], "--permanent", "-y")))
            .ShouldSucceed()
            .ShouldContain($"'{keys[0]}' and every version of it were removed.");

        await using (var wrong = await StartSformInTerminal(InProject("story", "kv", "delete", keys[1], "--permanent")))
        {
            await wrong.ExpectAsync("Type the key to remove");
            await wrong.SendLineAsync("not-the-key");
            (await wrong.WaitForExitAsync()).ShouldExitWith(CliExit.Aborted).ShouldContain("Aborted, nothing was removed.");
        }

        (await Sform(InProject("story", "kv", "get", keys[1]))).ShouldSucceed();

        await using (var typed = await StartSformInTerminal(InProject("story", "kv", "delete", keys[1], "--permanent")))
        {
            await typed.ExpectAsync("Type the key to remove");
            await typed.SendLineAsync(keys[1]);
            (await typed.WaitForExitAsync()).ShouldSucceed().ShouldContain($"'{keys[1]}' and every version of it were removed.");
        }

        (await Sform(InProject("story", "kv", "delete", keys[2], "--permanent", "--yes")))
            .ShouldSucceed()
            .ShouldContain($"'{keys[2]}' and every version of it were removed.");

        var trash = (await Sform(InProject("story", "kv", "--deleted", "--prefix", root))).ShouldSucceed();
        await Assert.That(KeyValuesSupportA.ListedKeys(trash, root)).IsEmpty();
        foreach (var key in keys)
        {
            (await Sform(InProject("story", "kv", "get", key, "--deleted"))).ShouldExitWith(CliExit.WrongInput);
        }
    }

    // KV-BLK-06: kv restore restores keys by list or --prefix.
    // As an Editor, <p>.app.a (two versions), <p>.app.b and <p>.x are deleted; story kv restore <p>.x says
    // "1 of 1 keys restored.", story kv restore --prefix <p>.app. "2 of 2 keys restored.", and all three are active at
    // their latest versions.
    [Test]
    [TestCase(409)]
    [UserRole(Role.Editor)]
    [Covers("story kv restore", "arg0", "--prefix")]
    public async Task Kv_Restore_KeysAndPrefix()
    {
        var root = CliData.KeyValueKey();
        var appA = $"{root}.app.a";
        var appB = $"{root}.app.b";
        var single = $"{root}.x";
        var latest = CliData.Value();
        (await Sform(InProject("story", "kv", "set", appA, CliData.Value(), "--protection", "public"))).ShouldSucceed();
        (await Sform(InProject("story", "kv", "set", appA, latest))).ShouldSucceed();
        (await Sform(InProject("story", "kv", "set", appB, CliData.Value(), "--protection", "public"))).ShouldSucceed();
        (await Sform(InProject("story", "kv", "set", single, CliData.Value(), "--protection", "public"))).ShouldSucceed();
        foreach (var key in new[] { appA, appB, single })
        {
            (await Sform(InProject("story", "kv", "delete", key))).ShouldSucceed();
        }

        (await Sform(InProject("story", "kv", "restore", single))).ShouldSucceed().ShouldContain("1 of 1 keys restored.");
        (await Sform(InProject("story", "kv", "restore", "--prefix", $"{root}.app."))).ShouldSucceed().ShouldContain("2 of 2 keys restored.");

        foreach (var (key, version) in new[] { (appA, 2), (appB, 1), (single, 1) })
        {
            var pair = (await Sform(InProject("story", "kv", "get", key))).ShouldSucceed().JsonNode();
            await Assert.That(pair["Version"]?.GetValue<int>()).IsEqualTo(version);
            await Assert.That(pair["IsDeleted"]?.GetValue<bool>() ?? false).IsFalse();
        }

        var restored = (await Sform(InProject("story", "kv", "get", appA))).ShouldSucceed().JsonNode();
        await Assert.That(restored["Value"]?.GetValue<string>()).IsEqualTo(latest);
    }

    // KV-BLK-07: kv purge -y removes soft-deleted keys for good (Administrator), and Contributors are refused.
    // As an Administrator, <p>.one, <p>.tmp.a and <p>.tmp.b are deleted; story kv purge <p>.one -y says "1 of
    // 1 keys purged.", story kv purge --prefix <p>.tmp. --yes "2 of 2 keys purged.", and kv get --deleted no longer
    // finds them (exit 2). A Contributor's story kv purge <k2> -y in <contributor project> ends with exit 44.
    [Test]
    [TestCase(410)]
    [UserRole(Role.Admin)]
    [Covers("story kv purge", "arg0", "--prefix", "-y", "--yes")]
    public async Task Kv_Purge_Admin()
    {
        var root = CliData.KeyValueKey();
        var single = $"{root}.one";
        string[] temporary = [$"{root}.tmp.a", $"{root}.tmp.b"];
        foreach (var key in temporary.Prepend(single))
        {
            (await Sform(InProject("story", "kv", "set", key, CliData.Value(), "--protection", "public"))).ShouldSucceed();
            (await Sform(InProject("story", "kv", "delete", key))).ShouldSucceed();
        }

        (await Sform(InProject("story", "kv", "purge", single, "-y"))).ShouldSucceed().ShouldContain("1 of 1 keys purged.");
        (await Sform(InProject("story", "kv", "purge", "--prefix", $"{root}.tmp.", "--yes"))).ShouldSucceed().ShouldContain("2 of 2 keys purged.");
        foreach (var key in temporary.Prepend(single))
        {
            (await Sform(InProject("story", "kv", "get", key, "--deleted"))).ShouldExitWith(CliExit.WrongInput);
        }

        var (contributor, projectKey, view) = await AsRoleUserAsync(Role.Contributor);
        (await contributor.Sform("story", "kv", "purge", CliData.KeyValueKey(), "-y", "-p", projectKey, "-v", view))
            .ShouldExitWith(CliExit.Unauthorized);
    }

    // KV-BLK-08: kv protect --protection keeps a key already at the level unchanged, and refuses public.
    // As a ContributorWithSecrets, <secret> and two secrets under <p>.sec. are set at standard. story kv protect
    // <secret> --protection standard and story kv protect --prefix <p>.sec. --protection standard report each key
    // Unchanged, "N of N keys protected." and "Older versions keep their previous level until they expire.", exit 0, and
    // the keys stay at version 1. story kv protect <secret> --protection public ends with exit 2 and "Only standard, hsm
    // and byok can be reached on the server; public and hyok need a new value with 'set'."
    [Test]
    [TestCase(411)]
    [UserRole(Role.Secrets)]
    [Covers("story kv protect", "arg0", "--prefix", "--protection", "--protection=standard")]
    public async Task Kv_Protect_SameLevelUnchanged()
    {
        var root = CliData.KeyValueKey();
        var single = $"{root}.secret";
        string[] underPrefix = [$"{root}.sec.a", $"{root}.sec.b"];
        foreach (var key in underPrefix.Prepend(single))
        {
            var secret = CliData.SecretValue();
            AddSecret(secret);
            (await Sform(new SformRunOptions { Secrets = [secret] }, InProject("story", "kv", "set", key, secret, "--protection", "standard"))).ShouldSucceed();
        }

        var one = (await Sform(InProject("story", "kv", "protect", single, "--protection", "standard")))
            .ShouldSucceed()
            .ShouldContain("1 of 1 keys protected.")
            .ShouldContain("Older versions keep their previous level until they expire.");
        await Assert.That(KeyValuesSupportA.RowOf(one, single)).Contains("Unchanged");

        var prefixed = (await Sform(InProject("story", "kv", "protect", "--prefix", $"{root}.sec.", "--protection", "standard")))
            .ShouldSucceed()
            .ShouldContain("2 of 2 keys protected.")
            .ShouldContain("Older versions keep their previous level until they expire.");
        foreach (var key in underPrefix)
        {
            await Assert.That(KeyValuesSupportA.RowOf(prefixed, key)).Contains("Unchanged");
        }

        foreach (var key in underPrefix.Prepend(single))
        {
            var pair = (await Sform(InProject("story", "kv", "get", key))).ShouldSucceed().JsonNode();
            await Assert.That(pair["Version"]?.GetValue<int>()).IsEqualTo(1);
            await Assert.That(pair["Protection"]?.GetValue<string>()).IsEqualTo("Standard");
        }

        (await Sform(InProject("story", "kv", "protect", single, "--protection", "public")))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain("Only standard, hsm and byok can be reached on the server; public and hyok need a new value with 'set'.");
    }

    // KV-BLK-09: kv versions and kv diff show the history of a key.
    // An Editor sets a public pair three times; as a Reader, story kv versions <k> lists versions 3 (current), 2 and 1;
    // story kv diff <k> shows the change from version 2 to 3, story kv diff <k> 3 1 the change from 1 to 3, and story kv
    // diff <k> 1 0 the first value against the empty text.
    [Test]
    [TestCase(412)]
    [UserRole(Role.Reader)]
    [Covers("story kv versions", "arg0")]
    [Covers("story kv diff", "arg0", "arg1", "arg2")]
    public async Task Kv_VersionsAndDiff()
    {
        var editor = await ArrangeAsEditorAsync();
        var key = CliData.KeyValueKey();
        string[] values = [CliData.Value(), CliData.Value(), CliData.Value()];
        (await editor.Sform(InProject("story", "kv", "set", key, values[0], "--protection", "public"))).ShouldSucceed();
        (await editor.Sform(InProject("story", "kv", "set", key, values[1]))).ShouldSucceed();
        (await editor.Sform(InProject("story", "kv", "set", key, values[2]))).ShouldSucceed();

        (await Sform(InProject("story", "kv", "versions", key)))
            .ShouldSucceed()
            .ShouldContain($"Versions of '{key}'")
            .ShouldMatch(new Regex(@"(?m)^\s*3 \(current\)\s+Public\b", RegexOptions.CultureInvariant))
            .ShouldMatch(new Regex(@"(?m)^\s*2\s+Public\b", RegexOptions.CultureInvariant))
            .ShouldMatch(new Regex(@"(?m)^\s*1\s+Public\b", RegexOptions.CultureInvariant));

        (await Sform(InProject("story", "kv", "diff", key)))
            .ShouldSucceed()
            .ShouldContain($"- {values[1]}")
            .ShouldContain($"+ {values[2]}");

        (await Sform(InProject("story", "kv", "diff", key, "3", "1")))
            .ShouldSucceed()
            .ShouldContain($"- {values[0]}")
            .ShouldContain($"+ {values[2]}");

        (await Sform(InProject("story", "kv", "diff", key, "1", "0")))
            .ShouldSucceed()
            .ShouldContain($"+ {values[0]}")
            .ShouldNotContain($"- {values[0]}");
    }

    private static string[] Sorted(IEnumerable<string> keys)
    {
        return keys.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    // The arguments with the acting project and another view of it: -p <project> -v <view>.
    private string[] InView(string view, params string[] arguments)
    {
        return [.. arguments, "-p", ProjectKey, "-v", view];
    }

    // Whether kv get --deleted finds the key soft-deleted.
    private async Task<bool> IsDeletedAsync(string key)
    {
        var pair = (await Sform(InProject("story", "kv", "get", key, "--deleted"))).ShouldSucceed().JsonNode();
        return pair["IsDeleted"]?.GetValue<bool>() ?? false;
    }
}

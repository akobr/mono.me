using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using _42.Platform.Cli.AutomationTests.Data;
using _42.Platform.Cli.AutomationTests.Running;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;

namespace _42.Platform.Cli.AutomationTests.Tests.KeyValues;

// Audit and Usage: sform story kv audit, the audit trail of a project's secrets newest first (Administrator), and
// kv usage, the totals of the key/value meters over a range of days. kv audit filters by view only with -v: the steps
// that read every view's events (and the project's own: the policy, the keys, the recipients, which have no view) give
// the project with -p alone.
[Area(Area.KeyValues)]
public sealed partial class KeyValueAuditTests : CliTest
{
    // How long a binding read may take to reach the audit: the API counts them in memory and writes them every flush
    // interval (KeyValueUsageFlushService, 60 s by default).
    private static readonly TimeSpan BindingReadWait = TimeSpan.FromMinutes(3);

    // KV-AUD-01: kv audit lists the secrets' audit trail newest first, and --key, --operation, --from, --to filter it.
    // As an Administrator, a secret created, updated and revealed in this run: kv audit lists its events with time,
    // actor and channel, newest first; --key <secret> lists only events of that key (its Create, Update and Reveal);
    // --operation reveal only Reveal events, its reveal among them; --from <run start> --to <now> only events of that
    // range, the secret's among them.
    [Test]
    [TestCase(456)]
    [UserRole(Role.Admin)]
    [Covers("story kv audit", "--key", "--operation", "--from", "--to")]
    public async Task KvAudit_Filters()
    {
        var start = DateTimeOffset.UtcNow.AddMinutes(-2);
        var key = CliData.KeyValueKey();
        var first = CliData.SecretValue();
        var second = CliData.SecretValue();
        AddSecret(first);
        AddSecret(second);
        string[] secrets = [first, second];
        (await Sform(new SformRunOptions { Secrets = secrets }, InProject("story", "kv", "set", key, first, "--secret"))).ShouldSucceed();
        (await Sform(new SformRunOptions { Secrets = secrets }, InProject("story", "kv", "set", key, second))).ShouldSucceed();
        (await Sform(new SformRunOptions { Secrets = secrets }, InProject("story", "kv", "get", key, "--reveal"))).ShouldSucceed();

        var all = (await Sform(KeyValuesSupportB.WithProject(ProjectKey, "story", "kv", "audit")))
            .ShouldSucceed()
            .ShouldMatch(HeaderPattern());
        var allRows = KeyValuesSupportB.AuditRows(all);
        await Assert.That(allRows.Count).IsGreaterThanOrEqualTo(3);
        await Assert.That(allRows.Zip(allRows.Skip(1)).All(pair => pair.First.Time >= pair.Second.Time)).IsTrue();
        var keyRows = allRows.Where(row => row.Line.Contains(key, StringComparison.Ordinal)).ToList();
        await Assert.That(keyRows.Select(row => row.Operation)).Contains("Reveal");

        var byKey = KeyValuesSupportB.AuditRows((await Sform(KeyValuesSupportB.WithProject(ProjectKey, "story", "kv", "audit", "--key", key))).ShouldSucceed());
        await Assert.That(byKey.All(row => row.Line.Contains(key, StringComparison.Ordinal))).IsTrue();
        await Assert.That(string.Join(", ", byKey.Select(row => row.Operation))).IsEqualTo("Reveal, Update, Create");

        var reveals = KeyValuesSupportB.AuditRows((await Sform(KeyValuesSupportB.WithProject(ProjectKey, "story", "kv", "audit", "--operation", "reveal"))).ShouldSucceed());
        await Assert.That(reveals.Select(row => row.Operation).Distinct()).IsEquivalentTo(new[] { "Reveal" });
        await Assert.That(reveals.Any(row => row.Line.Contains(key, StringComparison.Ordinal))).IsTrue();

        var end = DateTimeOffset.UtcNow.AddMinutes(1);
        var ranged = KeyValuesSupportB.AuditRows(
            (await Sform(KeyValuesSupportB.WithProject(ProjectKey, "story", "kv", "audit", "--from", start.ToString("o", CultureInfo.InvariantCulture), "--to", end.ToString("o", CultureInfo.InvariantCulture)))).ShouldSucceed());
        await Assert.That(ranged.Any(row => row.Line.Contains(key, StringComparison.Ordinal))).IsTrue();
        var earliest = Minute(start.LocalDateTime);
        var latest = Minute(end.LocalDateTime);
        await Assert.That(ranged.All(row => row.Time >= earliest && row.Time <= latest)).IsTrue();
    }

    // KV-AUD-02: kv audit -v filters by view, and -k pages.
    // In a disposable project, more than a page (100) of events in a view <B> (an import of 105 secrets, a replace that
    // soft-deletes 104 of them, and kv restore --prefix, one Restore event each) and a secret created in the view default
    // afterwards: kv audit -v <B> lists events of <B> only and the token of the next page; -k <token> continues with the
    // next page (and so on until no token follows), every event of <B> once and none of another view; --continuation-token
    // <token> lists the same second page; kv audit without -v lists every view's events, the default view's secret first.
    [Test]
    [TestCase(457)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("story kv audit", "-v", "-k", "--continuation-token")]
    public async Task KvAudit_ViewAndPages()
    {
        await CreateDisposableProjectAsync();
        var view = CliData.ViewName();
        var group = CliData.KeyValueKey();
        var items = Enumerable.Range(0, 105).Select(index => ($"{group}.key{index:D3}", "Standard", CliData.Value())).ToArray();
        await WriteFileAsync("bulk.json", KeyValuesSupportB.Document(items));
        await WriteFileAsync("keep.json", KeyValuesSupportB.Document(items[0]));
        (await Sform("story", "kv", "import", "bulk.json", "-y", "-p", ProjectKey, "-v", view)).ShouldSucceed().ShouldContain("Imported: 105 create.");
        (await Sform("story", "kv", "import", "keep.json", "--mode", "replace", "--prefix", $"{group}.", "-y", "-p", ProjectKey, "-v", view))
            .ShouldSucceed()
            .ShouldContain("Imported: 1 unchanged, 104 delete.");
        (await Sform("story", "kv", "restore", "--prefix", $"{group}.", "-p", ProjectKey, "-v", view)).ShouldSucceed().ShouldContain("104 of 104 keys restored.");

        var other = CliData.KeyValueKey();
        var secret = CliData.SecretValue();
        AddSecret(secret);
        (await Sform(new SformRunOptions { Secrets = [secret] }, "story", "kv", "set", other, secret, "--secret", "-p", ProjectKey, "-v", "default")).ShouldSucceed();

        // A page has at most 100 events (the API's page size; Cosmos DB may return fewer), so <B>'s 106 events (104
        // restores and the 2 imports) take two pages or more: the pages continue until no token follows.
        var firstPage = (await Sform("story", "kv", "audit", "-p", ProjectKey, "-v", view)).ShouldSucceed();
        var firstRows = KeyValuesSupportB.AuditRows(firstPage);
        await Assert.That(firstRows.Count).IsGreaterThanOrEqualTo(1);
        await Assert.That(firstRows.Count).IsLessThanOrEqualTo(100);
        await AssertOfViewAsync(firstRows, view);
        var token = KeyValuesSupportB.NextPageToken(firstPage) ?? throw firstPage.Fail("printed no \"next page with -k <token>\" before all of the view's 106 events");

        var pages = new List<IReadOnlyList<KeyValuesSupportB.AuditRow>> { firstRows };
        IReadOnlyList<KeyValuesSupportB.AuditRow>? secondRows = null;
        for (var next = token; next is not null;)
        {
            var page = (await Sform("story", "kv", "audit", "-p", ProjectKey, "-v", view, "-k", next)).ShouldSucceed();
            var rows = KeyValuesSupportB.AuditRows(page);
            await Assert.That(rows.Count).IsGreaterThanOrEqualTo(1);
            await AssertOfViewAsync(rows, view);
            pages.Add(rows);
            secondRows ??= rows;
            next = KeyValuesSupportB.NextPageToken(page);
            if (next is not null && pages.Count >= 20)
            {
                throw page.Fail("still printed a next page after 20 pages of the view's 106 events");
            }
        }

        // 104 Restore events and the 2 imports (an import writes its keys in batches; one that falls back to single
        // writes records their Create events too)
        await Assert.That(pages.Sum(page => page.Count)).IsGreaterThanOrEqualTo(106);
        await Assert.That(pages.SelectMany(page => page).Count(row => row.Operation == "Import")).IsEqualTo(2);
        var restored = pages.Select(page => RestoredKeys(page, group)).ToList();
        await Assert.That(restored.Sum(keys => keys.Count)).IsEqualTo(104);
        await Assert.That(restored.SelectMany(keys => keys).Distinct(StringComparer.Ordinal).Count()).IsEqualTo(104);

        var sameSecond = KeyValuesSupportB.AuditRows((await Sform("story", "kv", "audit", "-p", ProjectKey, "-v", view, "--continuation-token", token)).ShouldSucceed());
        await Assert.That(sameSecond.Select(row => row.Line).ToArray()).IsEquivalentTo(secondRows!.Select(row => row.Line).ToArray());

        var everyView = KeyValuesSupportB.AuditRows((await Sform(KeyValuesSupportB.WithProject(ProjectKey, "story", "kv", "audit"))).ShouldSucceed());
        await Assert.That(everyView.Count).IsGreaterThanOrEqualTo(2);
        await Assert.That(everyView[0].Line).Contains($"default/{other}");
        await Assert.That(everyView.Any(row => row.Line.Contains($"{view}/{group}.", StringComparison.Ordinal))).IsTrue();
    }

    // KV-AUD-03: kv usage shows the totals of the last 30 days, and --from --to those of a range.
    // As an Administrator, kv usage prints "Key/value usage <from> to <to> (N days with data)" for the 30 days up to today
    // (UTC) and the totals per meter (or "No usage recorded."); kv usage --from <today-7> --to <today> the same for that
    // range.
    [Test]
    [TestCase(458)]
    [UserRole(Role.Admin)]
    [Covers("story kv usage", "--from", "--to")]
    public async Task KvUsage_Range()
    {
        var usage = (await Sform(InProject("story", "kv", "usage"))).ShouldSucceed();
        var range = UsageHeaderPattern().Match(usage.PlainStdOut);
        if (!range.Success)
        {
            throw usage.Fail("printed no \"Key/value usage <from> to <to> (N days with data)\"");
        }

        var from = DateOnly.ParseExact(range.Groups["from"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var to = DateOnly.ParseExact(range.Groups["to"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        await Assert.That(to.DayNumber - from.DayNumber).IsEqualTo(30);
        await Assert.That(Math.Abs(to.DayNumber - DateOnly.FromDateTime(DateTime.UtcNow).DayNumber)).IsLessThanOrEqualTo(1);
        await AssertTotalsAsync(usage);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var weekAgo = today.AddDays(-7);
        var week = (await Sform(InProject("story", "kv", "usage", "--from", Day(weekAgo), "--to", Day(today)))).ShouldSucceed();
        week.ShouldMatch(new Regex($@"Key/value usage {Day(weekAgo)} to {Day(today)} \(\d+ days with data\)"));
        await AssertTotalsAsync(week);
    }

    // KV-AUD-04: A Contributor's kv audit and kv usage are refused.
    // As a Contributor, kv audit and kv usage end with 44.
    [Test]
    [TestCase(459)]
    [UserRole(Role.Contributor)]
    [Covers("story kv audit")]
    [Covers("story kv usage")]
    public async Task KvAudit_Contributor_Refused()
    {
        (await Sform(InProject("story", "kv", "audit"))).ShouldExitWith(CliExit.Unauthorized);
        (await Sform(InProject("story", "kv", "usage"))).ShouldExitWith(CliExit.Unauthorized);
    }

    // KV-AUD-05: kv audit --operation filters each of the nineteen operations, and an unknown one fails with exit 2.
    // In a disposable project: <rst> with a configuration bound to the secret <s>, read resolved (a binding read); <s>
    // created, updated, revealed, exported with the secrets, deleted and restored; an import of a secret; <s2> deleted and
    // purged, <s3> removed for good; mTLS turned off (a policy change); the Standard key rotated; a recipient added,
    // disabled and removed. kv audit --operation <op> lists only events of <op> for each of the nineteen, the one its step
    // caused among them: the binding read once the API wrote it (within a few minutes), the re-wrap once it ran (the
    // policy's Rewrap is complete); reprotect, byokverify and byokunavailable print "No audit events." (a project without
    // HSM and BYOK causes none). kv audit --operation nope ends with 2 and "Unknown operation 'nope'.".
    [Test]
    [TestCase(460)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("story kv audit", "--operation", "--operation=Reveal", "--operation=Export", "--operation=BindingRead", "--operation=Create", "--operation=Update", "--operation=Delete", "--operation=Restore", "--operation=PermanentDelete", "--operation=Purge", "--operation=Reprotect", "--operation=Import", "--operation=PolicyChange", "--operation=Rotate", "--operation=Rewrap", "--operation=ByokVerify", "--operation=ByokUnavailable", "--operation=RecipientAdded", "--operation=RecipientChanged", "--operation=RecipientRemoved")]
    public async Task KvAudit_EachOperation_Filters()
    {
        await CreateDisposableProjectAsync();
        var group = CliData.KeyValueKey();
        var s = $"{group}.s";
        var s2 = $"{group}.s2";
        var s3 = $"{group}.s3";
        var imported = $"{group}.s_token";
        var values = new[] { CliData.SecretValue(), CliData.SecretValue(), CliData.SecretValue(), CliData.SecretValue(), CliData.SecretValue() };
        foreach (var value in values)
        {
            AddSecret(value);
        }

        var hidden = new SformRunOptions { Secrets = values };
        var rst = CliData.ResponsibilityKey();

        // create, and the binding read first, so the API has written it by the time its filter runs
        (await Sform(hidden, InProject("story", "kv", "set", s, values[0], "--secret"))).ShouldSucceed();
        (await Sform(InProject("story", "set", rst))).ShouldSucceed();
        (await Sform(InProject("story", "config", "set", rst, "-x", $"secret=@{s}"))).ShouldSucceed();
        (await Sform(hidden, InProject("story", "config", rst, "-r"))).ShouldSucceed();

        (await Sform(hidden, InProject("story", "kv", "set", s, values[1]))).ShouldSucceed().ShouldContain($"'{s}' is at version 2 (");
        (await Sform(hidden, InProject("story", "kv", "get", s, "--reveal"))).ShouldSucceed();
        (await Sform(hidden, InProject("story", "kv", "export", "--include-secrets"))).ShouldSucceed();
        await WriteFileAsync("s.env", $"{KeyValuesSupportB.EnvName(imported)}={values[2]}\n");
        (await Sform(hidden, InProject("story", "kv", "import", "s.env", "--format", "env", "--secret", $"{group}.S_*", "-y"))).ShouldSucceed();
        (await Sform(InProject("story", "kv", "delete", s))).ShouldSucceed();
        (await Sform(InProject("story", "kv", "restore", s))).ShouldSucceed();
        (await Sform(hidden, InProject("story", "kv", "set", s2, values[3], "--secret"))).ShouldSucceed();
        (await Sform(InProject("story", "kv", "delete", s2))).ShouldSucceed();
        (await Sform(InProject("story", "kv", "purge", s2, "-y"))).ShouldSucceed();
        (await Sform(hidden, InProject("story", "kv", "set", s3, values[4], "--secret"))).ShouldSucceed();
        (await Sform(InProject("story", "kv", "delete", s3, "--permanent", "-y"))).ShouldSucceed();
        (await Sform(InProject("story", "kv", "policy", "set", "--require-mtls", "false"))).ShouldSucceed();
        (await Sform(InProject("story", "kv", "policy", "rotate", "--protection", "standard"))).ShouldSucceed();
        var recipientKey = KeyValuesSupportB.NewRecipientKey();
        await WriteFileAsync("r.pem", recipientKey.PublicPem);
        var recipient = KeyValuesSupportB.RecipientIdOf(
            (await Sform(InProject("story", "kv", "recipients", "add", "--name", CliData.Slug("rcp"), "--public-key", "r.pem"))).ShouldSucceed());
        (await Sform(InProject("story", "kv", "recipients", "disable", recipient))).ShouldSucceed();
        (await Sform(InProject("story", "kv", "recipients", "remove", recipient, "-y"))).ShouldSucceed();

        // the key the step's event names, or null for the events of the project or the view (export, import, the policy,
        // the keys, the recipients)
        var caused = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["reveal"] = s,
            ["export"] = null,
            ["create"] = s,
            ["update"] = s,
            ["delete"] = s,
            ["restore"] = s,
            ["permanentdelete"] = s3,
            ["purge"] = s2,
            ["import"] = null,
            ["policychange"] = null,
            ["rotate"] = null,
            ["recipientadded"] = null,
            ["recipientchanged"] = null,
            ["recipientremoved"] = null,
        };
        foreach (var (operation, key) in caused)
        {
            var rows = await AuditOfAsync(operation);
            await Assert.That(rows.Count).IsGreaterThanOrEqualTo(1);
            if (key is not null)
            {
                await Assert.That(rows.Any(row => row.Line.Contains($"/{key}@", StringComparison.Ordinal))).IsTrue();
            }
        }

        // the re-wrap of the rotation: a timer of the API runs it, so it is listed once the policy says it completed
        var rewraps = await AuditOfAsync("rewrap");
        if ((await Sform(InProject("story", "kv", "policy"))).ShouldSucceed().JsonNode()["Rewrap"]?["CompletedAt"] is JsonValue)
        {
            await Assert.That(rewraps.Count).IsGreaterThanOrEqualTo(1);
        }

        foreach (var operation in new[] { "reprotect", "byokverify", "byokunavailable" })
        {
            (await Sform(KeyValuesSupportB.WithProject(ProjectKey, "story", "kv", "audit", "--operation", operation)))
                .ShouldSucceed()
                .ShouldContain("No audit events.");
        }

        // the binding read: written by the API's flush, so it is waited for
        var bindingReads = await EventuallyAsync(
            () => AuditOfAsync("bindingread"),
            rows => rows.Any(row => row.Line.Contains($"/{s}", StringComparison.Ordinal)),
            BindingReadWait,
            TimeSpan.FromSeconds(15));

        await Assert.That(bindingReads.Any(row => row.Line.Contains($"/{s}", StringComparison.Ordinal))).IsTrue();

        (await Sform(KeyValuesSupportB.WithProject(ProjectKey, "story", "kv", "audit", "--operation", "nope")))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain("Unknown operation 'nope'.");
    }

    private static DateTime Minute(DateTime time)
    {
        return new DateTime(time.Year, time.Month, time.Day, time.Hour, time.Minute, 0, time.Kind);
    }

    private static string Day(DateOnly day)
    {
        return day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    // The keys of the Restore rows of the group ("<view>/<key>@<version>").
    private static HashSet<string> RestoredKeys(IEnumerable<KeyValuesSupportB.AuditRow> rows, string group)
    {
        var pattern = new Regex($@"/(?<key>{Regex.Escape(group)}\.key\d{{3}})@", RegexOptions.CultureInvariant);
        return rows.Where(row => row.Operation == "Restore")
            .Select(row => pattern.Match(row.Line))
            .Where(match => match.Success)
            .Select(match => match.Groups["key"].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static async Task AssertOfViewAsync(IReadOnlyList<KeyValuesSupportB.AuditRow> rows, string view)
    {
        await Assert.That(rows.All(row => row.Line.Contains($" {view}", StringComparison.Ordinal))).IsTrue();
        await Assert.That(rows.Any(row => row.Line.Contains("default/", StringComparison.Ordinal))).IsFalse();
    }

    // The table of meters (Meter, Total), or "No usage recorded." for a project without usage in the range.
    private static async Task AssertTotalsAsync(SformResult usage)
    {
        var text = usage.PlainStdOut;
        await Assert.That(text.Contains("No usage recorded.", StringComparison.Ordinal) || (text.Contains("Meter", StringComparison.Ordinal) && text.Contains("Total (keys as key-months)", StringComparison.Ordinal))).IsTrue();
    }

    [GeneratedRegex(@"Time\s+Operation\s+Key\s+Level\s+Actor\s+Channel\s+Detail", RegexOptions.CultureInvariant)]
    private static partial Regex HeaderPattern();

    [GeneratedRegex(@"Key/value usage (?<from>\d{4}-\d{2}-\d{2}) to (?<to>\d{4}-\d{2}-\d{2}) \(\d+ days with data\)", RegexOptions.CultureInvariant)]
    private static partial Regex UsageHeaderPattern();

    // kv audit --operation <operation> of the project (every view): its rows, each of that operation.
    private async Task<IReadOnlyList<KeyValuesSupportB.AuditRow>> AuditOfAsync(string operation)
    {
        var result = (await Sform(KeyValuesSupportB.WithProject(ProjectKey, "story", "kv", "audit", "--operation", operation))).ShouldSucceed();
        var rows = KeyValuesSupportB.AuditRows(result);
        var expected = KeyValuesSupportB.AuditOperations.Single(name => string.Equals(name, operation, StringComparison.OrdinalIgnoreCase));
        if (rows.Any(row => row.Operation != expected))
        {
            throw result.Fail($"listed events of other operations than {expected}");
        }

        return rows;
    }
}

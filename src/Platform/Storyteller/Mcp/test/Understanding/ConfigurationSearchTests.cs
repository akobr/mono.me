using _42.Platform.Storyteller.Mcp.Understanding;

using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Mcp.UnitTests.Understanding;

public class ConfigurationSearchTests
{
    private static readonly JObject Document = JObject.Parse(
        """
        {
          "database": { "host": "db.prod.example", "port": 5432, "password": "@secret(db.password)" },
          "Host": "edge.example",
          "replica": { "host": "@[@db.passwords and more]" },
          "url": "@[https://@db.password/x]",
          "public": "@(db.password, public)",
          "vault": "@(db.password, primaryVault)",
          "rule": { "$binding": "jsone", "$definition": { "v": "@db.password" }, "$context": { "p": "@db.password" } },
          "enabled": true
        }
        """);

    [Theory]
    [InlineData("exe.northwind.invoicing.prod", true)]
    [InlineData("usg.northwind.invoicing", true)]
    [InlineData("rst.invoicing", false)]
    [InlineData("not-a-key", false)]
    public void Subject_MatchesAPartOfTheSubjectName(string key, bool matches)
    {
        new ConfigurationSearch(subject: "NORTH").MatchesKey(key).ShouldBe(matches);
    }

    [Theory]
    [InlineData("unt.invoicing.export", true)]
    [InlineData("uxe.northwind.invoicing.prod.export", true)]
    [InlineData("exe.northwind.invoicing.prod", false)]
    public void Unit_MatchesOnlyKeysWithAUnit(string key, bool matches)
    {
        new ConfigurationSearch(unit: "port").MatchesKey(key).ShouldBe(matches);
    }

    [Fact]
    public void KeyFilters_AllMustMatch()
    {
        var search = new ConfigurationSearch(subject: "north", context: "prod", keyText: "voic");

        search.MatchesKey("exe.northwind.invoicing.prod").ShouldBeTrue();
        search.MatchesKey("exe.northwind.invoicing.test").ShouldBeFalse();
        search.MatchesKey("cnt.northwind.prod").ShouldBeFalse();
        new ConfigurationSearch().MatchesKey("not-a-key").ShouldBeTrue();
    }

    [Fact]
    public void Property_MatchesTheLastNameWithoutCase()
    {
        var pointers = new ConfigurationSearch(property: "host").FindMatches("sbt.a", 1, Document).Select(match => match.Pointer);

        pointers.ShouldBe(["/database/host", "/Host", "/replica/host"]);
    }

    [Fact]
    public void Property_APointerSuffix_MatchesTheEndOfThePath()
    {
        var match = new ConfigurationSearch(property: "/database/host").FindMatches("sbt.a", 3, Document).ShouldHaveSingleItem();

        match.ShouldSatisfyAllConditions(
            () => match.AnnotationKey.ShouldBe("sbt.a"),
            () => match.Version.ShouldBe(3),
            () => match.Pointer.ShouldBe("/database/host"),
            () => match.Value!.Value<string>().ShouldBe("db.prod.example"));
    }

    [Fact]
    public void ValueText_MatchesScalarsWithoutCase()
    {
        new ConfigurationSearch(valueText: "PROD").FindMatches("sbt.a", 1, Document).Select(match => match.Pointer).ShouldBe(["/database/host"]);
        new ConfigurationSearch(valueText: "543").FindMatches("sbt.a", 1, Document).Select(match => match.Pointer).ShouldBe(["/database/port"]);
        new ConfigurationSearch(valueText: "true").FindMatches("sbt.a", 1, Document).Select(match => match.Pointer).ShouldBe(["/enabled"]);
    }

    [Fact]
    public void BindsKey_MatchesTheValuesStoreReferences_AsStored()
    {
        var matches = new ConfigurationSearch(bindsKey: "DB.Password").FindMatches("sbt.a", 1, Document).ToDictionary(match => match.Pointer!);

        // not the Key Vault source, not a different key that starts alike
        matches.Keys.ShouldBe(["/database/password", "/url", "/public", "/rule/$definition/v", "/rule/$context/p"]);
        matches["/database/password"].Value!.Value<string>().ShouldBe("@secret(db.password)");
        matches["/database/password"].Note.ShouldBeNull();
        matches["/rule/$context/p"].Note.ShouldBeNull();

        // the resolver binds the program's result again: a string of the program is read when the program emits it
        matches["/rule/$definition/v"].Note.ShouldBe(ConfigurationSearch.PossibleReadNote);
    }

    [Fact]
    public void BindsKey_InTheProgramOfAnObjectBinding_IsAPossibleRead()
    {
        var document = JObject.Parse("""{"conn": {"$binding": "jsone", "$definition": {"password": "@db.password"}}}""");

        var match = new ConfigurationSearch(bindsKey: "db.password").FindMatches("sbt.a", 1, document).ShouldHaveSingleItem();

        match.Pointer.ShouldBe("/conn/$definition/password");
        match.Note.ShouldBe(ConfigurationSearch.PossibleReadNote);
    }

    [Fact]
    public void Filters_Combine()
    {
        new ConfigurationSearch(property: "password", bindsKey: "db.password").FindMatches("sbt.a", 1, Document)
            .ShouldHaveSingleItem().Pointer.ShouldBe("/database/password");
    }

    [Fact]
    public void LargeContainers_AreReportedByTheirKind()
    {
        var document = new JObject { ["big"] = new JArray(Enumerable.Range(0, 400).Select(index => $"item-{index}")), ["small"] = new JArray(1, 2) };

        var matches = new ConfigurationSearch(property: "/big").FindMatches("sbt.a", 1, document).Concat(new ConfigurationSearch(property: "small").FindMatches("sbt.a", 1, document)).ToList();

        matches[0].Value.ShouldBeNull();
        matches[0].ValueKind.ShouldBe("array");
        matches[1].Value!.Type.ShouldBe(JTokenType.Array);
    }

    [Fact]
    public async Task KeyFiltersOnly_ReadNoDocument()
    {
        var api = new FakeListing(Summaries(3), pages: 1);

        var result = await new ConfigurationSearch(subject: "s1").RunAsync(api.ListAsync, api.ReadAsync, 50, CancellationToken.None);

        result.Items.ShouldHaveSingleItem().AnnotationKey.ShouldBe("sbt.s1");
        result.Items[0].Pointer.ShouldBeNull();
        result.IsTruncated.ShouldBeFalse();
        api.Reads.ShouldBe(0);
    }

    [Fact]
    public async Task Pages_StopAfterFive()
    {
        var api = new FakeListing(Summaries(2), pages: 10);

        var result = await new ConfigurationSearch().RunAsync(api.ListAsync, api.ReadAsync, 200, CancellationToken.None);

        api.PagesListed.ShouldBe(ConfigurationSearch.MaxPages);
        result.PagesRead.ShouldBe(5);
        result.Count.ShouldBe(10);
        result.IsTruncated.ShouldBeTrue();
        result.TruncatedBy.ShouldBe(ConfigurationSearch.ByPages);
        result.Message.ShouldNotBeNull().ShouldContain("5 API pages");
    }

    [Fact]
    public async Task DocumentReads_StopAt200_FourAtATime()
    {
        var api = new FakeListing(Summaries(150), pages: 2);

        var result = await new ConfigurationSearch(property: "absent").RunAsync(api.ListAsync, api.ReadAsync, 50, CancellationToken.None);

        api.Reads.ShouldBe(ConfigurationSearch.MaxDocumentReads);
        api.MaxConcurrentReads.ShouldBeLessThanOrEqualTo(ConfigurationSearch.MaxConcurrentReads);
        api.MaxConcurrentReads.ShouldBeGreaterThan(1);
        result.DocumentsRead.ShouldBe(200);
        result.TruncatedBy.ShouldBe(ConfigurationSearch.ByDocumentReads);
    }

    [Fact]
    public async Task DocumentReads_SkipVersionZeroAndEmptyDocuments()
    {
        var summaries = new List<Sdk.ConfigurationSummary>
        {
            new() { AnnotationKey = "sbt.cache", Version = 0, HasContent = false },
            new() { AnnotationKey = "sbt.empty", Version = 2, HasContent = false },
            new() { AnnotationKey = "sbt.full", Version = 2, HasContent = true },
        };
        var api = new FakeListing(summaries, pages: 1);

        var result = await new ConfigurationSearch(property: "name").RunAsync(api.ListAsync, api.ReadAsync, 50, CancellationToken.None);

        api.ReadKeys.ShouldBe(["sbt.full@2"]);
        result.Items.ShouldHaveSingleItem().AnnotationKey.ShouldBe("sbt.full");
    }

    [Fact]
    public async Task MaxResults_StopsTheSearch_OnlyWhenThereAreMore()
    {
        var api = new FakeListing(Summaries(10), pages: 1);

        var truncated = await new ConfigurationSearch(property: "name").RunAsync(api.ListAsync, api.ReadAsync, 3, CancellationToken.None);
        var exact = await new ConfigurationSearch(property: "name").RunAsync(api.ListAsync, api.ReadAsync, 10, CancellationToken.None);

        truncated.Count.ShouldBe(3);
        truncated.TruncatedBy.ShouldBe(ConfigurationSearch.ByMaxResults);
        exact.Count.ShouldBe(10);
        exact.IsTruncated.ShouldBeFalse();
        exact.TruncatedBy.ShouldBeNull();
    }

    [Theory]
    [InlineData(null, 50)]
    [InlineData(0, 1)]
    [InlineData(1000, 200)]
    public void MaxResults_IsClamped(int? maxResults, int expected)
    {
        ConfigurationSearch.ClampMaxResults(maxResults).ShouldBe(expected);
    }

    private static List<Sdk.ConfigurationSummary> Summaries(int count)
    {
        return Enumerable.Range(0, count)
            .Select(index => new Sdk.ConfigurationSummary { AnnotationKey = $"sbt.s{index}", Version = 1, HasContent = true })
            .ToList();
    }

    // A listing whose every page holds the same summaries; each document is {"name": key}.
    private sealed class FakeListing(List<Sdk.ConfigurationSummary> page, int pages)
    {
        private int _concurrent;

        public int PagesListed { get; private set; }

        public int Reads => ReadKeys.Count;

        public List<string> ReadKeys { get; } = [];

        public int MaxConcurrentReads { get; private set; }

        public Task<Sdk.ConfigurationsResponse> ListAsync(string? continuationToken, CancellationToken cancellationToken)
        {
            PagesListed++;
            return Task.FromResult(new Sdk.ConfigurationsResponse
            {
                Configurations = page,
                ContinuationToken = PagesListed < pages ? $"page-{PagesListed + 1}" : null,
            });
        }

        public async Task<JObject?> ReadAsync(Sdk.ConfigurationSummary summary, CancellationToken cancellationToken)
        {
            var now = Interlocked.Increment(ref _concurrent);

            lock (ReadKeys)
            {
                MaxConcurrentReads = Math.Max(MaxConcurrentReads, now);
                ReadKeys.Add($"{summary.AnnotationKey}@{summary.Version}");
            }

            await Task.Delay(5, cancellationToken);
            Interlocked.Decrement(ref _concurrent);
            return new JObject { ["name"] = summary.AnnotationKey };
        }
    }
}

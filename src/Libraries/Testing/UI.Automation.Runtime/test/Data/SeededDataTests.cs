using _42.Testing.UI.Automation.Runtime.Data;

namespace _42.Testing.UI.Automation.Runtime.UnitTests.Data;

public sealed class SeededDataTests
{
    private const string TestId = "42.Platform.UI.Admin.AutomationTests/Tests.Annotations.AnnotationsTests/Annotations_CreateResponsibility_AppearsInGrid";

    // FNV-1a over (42 little-endian, the UTF-8 id, 1 little-endian), then the MurmurHash3 finalizer; checked against an
    // independent implementation.
    private const int PinnedSeed = 1993078458;

    [Theory]
    [InlineData("", 0x811c9dc5u)]
    [InlineData("a", 0xe40c292cu)]
    [InlineData("foobar", 0xbf9cf968u)]
    public void Fnv1a32_MatchesTheReferenceVectors(string value, uint expected)
    {
        StableHash.Fnv1a32(value).ShouldBe(expected);
    }

    [Fact]
    public void Combine_IsPinned_SoASeedReproducesInEveryProcess()
    {
        // string.GetHashCode is randomized per process; this value must never change between runs or machines.
        StableHash.Combine(42, TestId, 1).ShouldBe(StableHash.Combine(42, TestId, 1));
        StableHash.Combine(42, TestId, 1).ShouldBe(PinnedSeed);
        StableHash.Combine(42, TestId, 1).ShouldBeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void TwoInstances_WithTheSameSeedAndTest_GenerateTheSameData()
    {
        var first = new SeededData(42, TestId);
        var second = new SeededData(42, TestId);

        Generate(first).ShouldBe(Generate(second));
        first.Seed.ShouldBe(second.Seed);
    }

    [Fact]
    public void AnotherTest_AnotherSessionSeed_OrARetry_GenerateOtherData()
    {
        var data = Generate(new SeededData(42, TestId));

        Generate(new SeededData(42, TestId + "2")).ShouldNotBe(data);
        Generate(new SeededData(43, TestId)).ShouldNotBe(data);
        Generate(new SeededData(42, TestId, attempt: 2)).ShouldNotBe(data);
    }

    [Fact]
    public void Slug_UsesTheKeyCharset_AndNormalizesThePrefix()
    {
        var data = new SeededData(7, TestId);

        data.Slug("at-20261031-142500-abcd").ShouldMatch("^at-20261031-142500-abcd-[a-z]{2,}-[a-z]{2,}$");
        data.Slug("At Run_1!", words: 1).ShouldMatch("^at-run-1-[a-z]{2,}$");
        data.Slug("x", words: 0).ShouldBe("x");
    }

    [Fact]
    public void Word_Sentence_AndHex_KeepTheirShape()
    {
        var data = new SeededData(7, TestId);

        for (var index = 0; index < 50; index++)
        {
            data.Word().ShouldMatch("^[a-z]{2,}$");
            data.Sentence(20).Length.ShouldBeLessThanOrEqualTo(20);
            data.Hex(6).ShouldMatch("^[0-9a-f]{6}$");
        }
    }

    [Fact]
    public void NewSessionSeed_IsNotNegative()
    {
        for (var index = 0; index < 20; index++)
        {
            SeededData.NewSessionSeed().ShouldBeGreaterThanOrEqualTo(0);
        }
    }

    private static string Generate(SeededData data)
    {
        return string.Join("|", data.Slug("at"), data.Word(), data.Sentence(60), data.Hex(), data.Faker.Name.FirstName(), data.Random.Number(1, 1_000_000));
    }
}

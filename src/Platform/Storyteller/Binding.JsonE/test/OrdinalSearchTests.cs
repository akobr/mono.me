using System;
using System.Diagnostics;
using System.Linq;
using System.Text.Json.Nodes;
using FluentAssertions;
using Json.JsonE;
using Xunit;

namespace _42.Platform.Storyteller.Binding.JsonE.UnitTests;

public class OrdinalSearchTests
{
    [Theory]
    [InlineData("", "")]
    [InlineData("abc", "")]
    [InlineData("", "a")]
    [InlineData("abc", "abcd")]
    [InlineData("foobar", "foo")]
    [InlineData("foobar", "bar")]
    [InlineData("foobar", "obb")]
    [InlineData("aA", "A")]
    [InlineData("😀x", "\uDE00")]
    public void Contains_MatchesOrdinalContains_OnSmallInputs(string source, string value)
    {
        OrdinalSearch.Contains(source, value).Should().Be(source.Contains(value, StringComparison.Ordinal));
    }

    [Fact]
    public void Contains_MatchesOrdinalContains_OnRandomLongNeedles()
    {
        // Small alphabets make long partial matches and many candidates; needles are longer than the short-needle cutoff.
        var random = new Random(42);
        for (var round = 0; round < 2_000; round++)
        {
            var alphabet = "ab😀c".Substring(0, random.Next(2, 6));
            var source = RandomText(random, alphabet, random.Next(0, 600));
            var value = random.Next(3) == 0 && source.Length > OrdinalSearch.ShortNeedleLength
                ? source.Substring(random.Next(0, source.Length - OrdinalSearch.ShortNeedleLength), OrdinalSearch.ShortNeedleLength + 1)
                : RandomText(random, alphabet, random.Next(OrdinalSearch.ShortNeedleLength + 1, 200));

            OrdinalSearch.Contains(source, value).Should().Be(source.Contains(value, StringComparison.Ordinal), $"round {round}");
        }
    }

    [Fact]
    public void Contains_PeriodicWorstCase_IsLinear()
    {
        // string.Contains needs about 0.6 s for this pair; the linear search scans each character a bounded number of times.
        var source = string.Concat(Enumerable.Repeat("ab", 200_000));
        var value = string.Concat(Enumerable.Repeat("ab", 100_000)) + "aa";
        var stopwatch = Stopwatch.StartNew();

        var found = OrdinalSearch.Contains(source, value);

        stopwatch.Stop();
        found.Should().BeFalse();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(100));
    }

    [Fact]
    public void InOperator_UsesTheLinearSearch()
    {
        var context = new JsonObject
        {
            ["s"] = string.Concat(Enumerable.Repeat("ab", 200_000)),
            ["t"] = string.Concat(Enumerable.Repeat("ab", 100_000)) + "aa",
        };
        var stopwatch = Stopwatch.StartNew();

        var result = Json.JsonE.JsonE.Evaluate(JsonNode.Parse("""{ "$eval": "t in s || t in s || t in s" }"""), context, meter: null);

        stopwatch.Stop();
        result!.ToJsonString().Should().Be("false");
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(300));
    }

    private static string RandomText(Random random, string alphabet, int length)
    {
        return new string(Enumerable.Range(0, length).Select(_ => alphabet[random.Next(alphabet.Length)]).ToArray());
    }
}

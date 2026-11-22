using System.Globalization;
using _42.Platform.Storyteller.Notifying;
using static _42.Platform.Storyteller.Backend.Core.UnitTests.Transferring.TransferTestData;

namespace _42.Platform.Storyteller.Backend.Core.UnitTests.Notifying;

public class ViewInboxIdsTests
{
    // SHA-256 of "abc" starts with these 32 hex characters.
    private const string HashOfAbc = "ba7816bf8f01cfea414140de5dae2223";

    [Fact]
    public void NewId_IsTheTimeAndThirtyTwoHexCharacters()
    {
        var id = ViewInboxIds.NewId(new DateTimeOffset(2026, 10, 1, 8, 5, 9, 123, TimeSpan.Zero).AddTicks(4567));

        id.Length.ShouldBe(ViewInboxIds.Length);
        id.Length.ShouldBe(53);
        id[..21].ShouldBe("202610010805091234567");
        Guid.TryParseExact(id[21..], "N", out _).ShouldBeTrue();
        ViewInboxIds.IsValid(id).ShouldBeTrue();
    }

    [Fact]
    public void NewId_OfTheSameTime_IsNew()
    {
        ViewInboxIds.NewId(Start).ShouldNotBe(ViewInboxIds.NewId(Start));
    }

    [Fact]
    public void Ids_SortInTheOrderOfTheirTimes()
    {
        // each time is later than the one before it by less and less: a year, a day, an hour, a second, a tick
        DateTimeOffset[] times =
        [
            new(2025, 12, 31, 23, 59, 59, TimeSpan.Zero),
            new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero),
            new(2026, 10, 2, 7, 0, 0, TimeSpan.Zero),
            new(2026, 10, 2, 9, 59, 59, TimeSpan.Zero),
            new(2026, 10, 2, 10, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 2, 10, 0, 0, TimeSpan.Zero).AddTicks(1),
        ];

        var newIds = times.Select(ViewInboxIds.NewId).ToList();
        var keyedIds = times.Select(time => ViewInboxIds.ForKey(time, "ViewExport.0123")).ToList();

        newIds.OrderBy(id => id, StringComparer.Ordinal).ShouldBe(newIds);
        keyedIds.OrderBy(id => id, StringComparer.Ordinal).ShouldBe(keyedIds);
    }

    [Fact]
    public void Ids_OfATimeWithAnotherOffset_AreThoseOfTheSameInstantInUtc()
    {
        var inPrague = new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.FromHours(2));
        var inNewYork = new DateTimeOffset(2026, 10, 1, 4, 0, 0, TimeSpan.FromHours(-4));

        ViewInboxIds.ForKey(inPrague, "ViewExport.0123").ShouldBe(ViewInboxIds.ForKey(Start, "ViewExport.0123"));
        ViewInboxIds.ForKey(inNewYork, "ViewExport.0123").ShouldBe(ViewInboxIds.ForKey(Start, "ViewExport.0123"));
        ViewInboxIds.NewId(inPrague)[..21].ShouldBe("202610010800000000000");

        // an hour of local time that comes later on the clock but earlier in time sorts first
        var earlier = new DateTimeOffset(2026, 10, 1, 9, 30, 0, TimeSpan.FromHours(2));
        string.CompareOrdinal(ViewInboxIds.NewId(earlier), ViewInboxIds.NewId(Start)).ShouldBeLessThan(0);
    }

    [Fact]
    public void Ids_DoNotFollowTheCultureOfTheThread()
    {
        var culture = CultureInfo.CurrentCulture;

        try
        {
            // the Thai calendar counts the year 2569
            CultureInfo.CurrentCulture = new CultureInfo("th-TH");

            ViewInboxIds.NewId(Start)[..21].ShouldBe("202610010800000000000");
            ViewInboxIds.ForKey(Start, "abc").ShouldBe("202610010800000000000" + HashOfAbc);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Fact]
    public void ForKey_EndsWithTheLowerCaseHashOfTheKey()
    {
        var id = ViewInboxIds.ForKey(Start, "abc");

        id.ShouldBe("202610010800000000000" + HashOfAbc);
        ViewInboxIds.IsValid(id).ShouldBeTrue();
    }

    [Fact]
    public void ForKey_GivesTheSameIdForTheSameKeyAndTime()
    {
        var id = ViewInboxIds.ForKey(Start, "ViewExport.0123");

        ViewInboxIds.ForKey(Start, "ViewExport.0123").ShouldBe(id);
        ViewInboxIds.ForKey(Start, "ViewImport.0123").ShouldNotBe(id);
        ViewInboxIds.ForKey(Start, "viewexport.0123").ShouldNotBe(id);
        ViewInboxIds.ForKey(Start.AddTicks(1), "ViewExport.0123").ShouldNotBe(id);
    }

    [Theory]
    [InlineData("ViewExport.0123")]
    [InlineData("")]
    [InlineData("a key with spaces / slashes ?&# and a long tail: 0123456789012345678901234567890123456789")]
    [InlineData("klíč ключ 鍵")]
    public void ForKey_IsValid_WhateverTheKey(string key)
    {
        ViewInboxIds.IsValid(ViewInboxIds.ForKey(Start, key)).ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("20261001080000000000ba7816bf8f01cfea414140de5dae2223")]
    [InlineData("2026100108000000000000ba7816bf8f01cfea414140de5dae2223")]
    [InlineData("202610010800000000000ba7816bf8f01cfea414140de5dae222")]
    [InlineData("202610010800000000000ba7816bf8f01cfea414140de5dae22233")]
    [InlineData("202610010800000000000BA7816BF8F01CFEA414140DE5DAE2223")]
    [InlineData("202610010800000000000ga7816bf8f01cfea414140de5dae2223")]
    [InlineData("20261001080000000000aba7816bf8f01cfea414140de5dae2223")]
    [InlineData("202610010800000000000ba7816bf8f01cfea414140de5dae2223\n")]
    [InlineData(" 202610010800000000000ba7816bf8f01cfea414140de5dae2223")]
    [InlineData("aca.202610010800000000000.ba7816bf8f01cfea414140de5dae2223")]
    [InlineData("202610010800000000000/ba7816bf8f01cfea414140de5dae222")]
    public void IsValid_RefusesEveryOtherShape(string? id)
    {
        ViewInboxIds.IsValid(id).ShouldBeFalse();
    }

    [Fact]
    public void IsValid_RefusesTheDigitsOfAnotherScript()
    {
        // Arabic-Indic zeros are digits, but not the digits of an id
        var id = new string('٠', 21) + HashOfAbc;

        id.Length.ShouldBe(ViewInboxIds.Length);
        ViewInboxIds.IsValid(id).ShouldBeFalse();
    }
}

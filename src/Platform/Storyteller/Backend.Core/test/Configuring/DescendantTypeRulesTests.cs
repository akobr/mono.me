using _42.Platform.Storyteller.Configuring;

namespace _42.Platform.Storyteller.Backend.Core.UnitTests.Configuring;

public class DescendantTypeRulesTests
{
    // One key of every type, and the types that descend from it (AnnotationHierarchy).
    private static readonly IReadOnlyDictionary<string, string[]> Descendants = new Dictionary<string, string[]>
    {
        ["rst.payments"] = ["unt", "usg", "exe", "uxe"],
        ["sbt.shop"] = ["cnt", "usg", "exe", "uxe"],
        ["usg.shop.payments"] = ["exe", "uxe"],
        ["cnt.shop.prod"] = ["exe", "uxe"],
        ["unt.payments.nightly"] = ["uxe"],
        ["exe.shop.payments.prod"] = ["uxe"],
        ["uxe.shop.payments.prod.nightly"] = [],
    };

    public static TheoryData<string, string, bool> Pairs()
    {
        var data = new TheoryData<string, string, bool>();

        foreach (var (key, descendants) in Descendants)
        {
            foreach (var type in AnnotationTypeCodes.ValidCodes.Keys)
            {
                data.Add(key, type, descendants.Contains(type));
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void CanDescend_FollowsTheAnnotationHierarchy(string key, string type, bool expected)
    {
        DescendantTypeRules.CanDescend(AnnotationKey.Parse(key), type).ShouldBe(expected);
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void EnsureCanDescend_ThrowsInvalidKey_ForEveryPairThatCannotDescend(string key, string type, bool expected)
    {
        var ensure = () => DescendantTypeRules.EnsureCanDescend(AnnotationKey.Parse(key), type);

        if (expected)
        {
            ensure.ShouldNotThrow();
            return;
        }

        ensure.ShouldThrow<InvalidInputException>().ErrorCode.ShouldBe(ErrorCodes.InvalidKey);
    }

    [Theory]
    [InlineData("rst.payments", "UXE", true)]
    [InlineData("RST.payments", "unt", true)]
    [InlineData("SBT.shop", "Cnt", true)]
    [InlineData("EXE.s.r.c", "RST", false)]
    public void CanDescend_TakesTypeCodesInAnyCase(string key, string type, bool expected)
    {
        DescendantTypeRules.CanDescend(AnnotationKey.Parse(key), type).ShouldBe(expected);
    }

    [Fact]
    public void GetCannotDescendMessage_NamesTheTypeAndTheKey()
    {
        DescendantTypeRules.GetCannotDescendMessage(AnnotationKey.Parse("exe.s.r.c"), "rst")
            .ShouldBe("The type rst cannot descend from exe.s.r.c, so it has no descendant-type schema there.");
    }

    [Fact]
    public void GetCannotDescendMessage_LowerCasesTheTypeCode()
    {
        // the write and the import pass the type as given and as normalized; both produce the same text
        DescendantTypeRules.GetCannotDescendMessage(AnnotationKey.Parse("exe.s.r.c"), "RST")
            .ShouldBe(DescendantTypeRules.GetCannotDescendMessage(AnnotationKey.Parse("exe.s.r.c"), "rst"));
    }

    [Fact]
    public void EnsureCanDescend_ThrowsWithTheMessage()
    {
        var exception = Should.Throw<InvalidInputException>(() => DescendantTypeRules.EnsureCanDescend(AnnotationKey.Parse("exe.s.r.c"), "rst"));

        exception.ErrorCode.ShouldBe(ErrorCodes.InvalidKey);
        exception.Message.ShouldBe("The type rst cannot descend from exe.s.r.c, so it has no descendant-type schema there.");
    }

    [Fact]
    public void EnsureCanDescend_AllowsADeepPair()
    {
        Should.NotThrow(() => DescendantTypeRules.EnsureCanDescend(AnnotationKey.Parse("exe.s.r.c"), "uxe"));
    }
}

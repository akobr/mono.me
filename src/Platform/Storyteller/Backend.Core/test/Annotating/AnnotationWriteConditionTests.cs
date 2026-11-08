using _42.Platform.Storyteller.Annotating;

namespace _42.Platform.Storyteller.Backend.Core.UnitTests.Annotating;

public class AnnotationWriteConditionTests
{
    private const string Current = "\"2\"";
    private const string Earlier = "\"1\"";

    private static readonly AnnotationKey Key = AnnotationKey.Parse("sbt.northwind");

    public static TheoryData<string, bool, string?, bool> Cases => new()
    {
        // condition, the annotation exists, its marker, holds
        { "none", true, Current, true },
        { "none", false, null, true },
        { "match current", true, Current, true },
        { "match earlier", true, Current, false },
        { "match earlier or current", true, Current, true },
        { "match current", false, null, false },
        { "match nothing", true, Current, false },
        { "match any", true, Current, true },
        { "match any", false, null, false },
        { "none match any", false, null, true },
        { "none match any", true, Current, false },
        { "none match current", true, Current, false },
        { "none match earlier", true, Current, true },
        { "none match current", false, null, true },
        { "match any and none match any", true, Current, false },
        { "match any and none match any", false, null, false },
        { "match current and none match earlier", true, Current, true },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Holds_AsRfc9110Says(string condition, bool exists, string? marker, bool holds)
    {
        Condition(condition).Holds(exists, marker).ShouldBe(holds);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Check_ThrowsAPreconditionFailure_WhenItDoesNotHold(string condition, bool exists, string? marker, bool holds)
    {
        var check = () => Condition(condition).Check(Key, exists, marker);

        if (holds)
        {
            check.ShouldNotThrow();
            return;
        }

        var failure = check.ShouldThrow<PreconditionFailedException>();
        failure.ErrorCode.ShouldBe(ErrorCodes.AnnotationPreconditionFailed);
        failure.Message.ShouldContain("'sbt.northwind'");
    }

    [Fact]
    public void Check_SaysWhatFailed()
    {
        Should.Throw<PreconditionFailedException>(() => AnnotationWriteCondition.Matching(Earlier).Check(Key, true, Current))
            .Message.ShouldContain("changed since it was read");
        Should.Throw<PreconditionFailedException>(() => AnnotationWriteCondition.Matching(Earlier).Check(Key, false, null))
            .Message.ShouldContain("does not exist or is deleted");
        Should.Throw<PreconditionFailedException>(() => AnnotationWriteCondition.CreateOnly.Check(Key, true, Current))
            .Message.ShouldContain("already exists");
    }

    [Fact]
    public void None_IsEmpty_AndAllowsACreate()
    {
        AnnotationWriteCondition.None.IsEmpty.ShouldBeTrue();
        AnnotationWriteCondition.None.AllowsCreate.ShouldBeTrue();
        AnnotationWriteCondition.CreateOnly.IsEmpty.ShouldBeFalse();
        AnnotationWriteCondition.CreateOnly.AllowsCreate.ShouldBeTrue();
        AnnotationWriteCondition.Matching(Current).AllowsCreate.ShouldBeFalse();
    }

    // Markers are compared as they are stored: ordinal.
    [Fact]
    public void Markers_CompareOrdinally()
    {
        AnnotationWriteCondition.Matching("\"a\"").Holds(true, "\"A\"").ShouldBeFalse();
    }

    private static AnnotationWriteCondition Condition(string name)
    {
        return name switch
        {
            "none" => AnnotationWriteCondition.None,
            "match current" => AnnotationWriteCondition.Matching(Current),
            "match earlier" => AnnotationWriteCondition.Matching(Earlier),
            "match earlier or current" => AnnotationWriteCondition.Matching(Earlier, Current),
            "match nothing" => new AnnotationWriteCondition { IfMatch = new AnnotationVersionMatch(false, []) },
            "match any" => new AnnotationWriteCondition { IfMatch = AnnotationVersionMatch.Anything },
            "none match any" => AnnotationWriteCondition.CreateOnly,
            "none match current" => new AnnotationWriteCondition { IfNoneMatch = AnnotationVersionMatch.Of(Current) },
            "none match earlier" => new AnnotationWriteCondition { IfNoneMatch = AnnotationVersionMatch.Of(Earlier) },
            "match any and none match any" => new AnnotationWriteCondition { IfMatch = AnnotationVersionMatch.Anything, IfNoneMatch = AnnotationVersionMatch.Anything },
            "match current and none match earlier" => new AnnotationWriteCondition { IfMatch = AnnotationVersionMatch.Of(Current), IfNoneMatch = AnnotationVersionMatch.Of(Earlier) },
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
        };
    }
}

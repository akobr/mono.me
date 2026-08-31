using FluentAssertions;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

public class AnnotationKeyExtensionsTests
{
    [Fact]
    public void TryGetAncestorKey_ResponsibilityAncestorOfUnit_ReturnsResponsibilityKey()
    {
        var key = AnnotationKey.CreateUnit("myresp", "myunit");

        var ancestor = key.TryGetAncestorKey(AnnotationType.Responsibility);

        ancestor!.ToString().Should().Be("rst.myresp");
    }

    [Fact]
    public void TryGetAncestorKey_SubjectAncestorOfContext_ReturnsSubjectKey()
    {
        var key = AnnotationKey.CreateContext("mysubject", "myctx");

        var ancestor = key.TryGetAncestorKey(AnnotationType.Subject);

        ancestor!.ToString().Should().Be("sbt.mysubject");
    }

    [Theory]
    [InlineData(AnnotationType.Responsibility, "rst.myresp")]
    [InlineData(AnnotationType.Subject, "sbt.mysubject")]
    [InlineData(AnnotationType.Usage, "usg.mysubject.myresp")]
    [InlineData(AnnotationType.Context, "cnt.mysubject.myctx")]
    public void TryGetAncestorKey_ExecutionAncestors_ReturnExpectedKeys(AnnotationType type, string expected)
    {
        var key = AnnotationKey.CreateExecution("mysubject", "myresp", "myctx");

        var ancestor = key.TryGetAncestorKey(type);

        ancestor!.ToString().Should().Be(expected);
    }

    [Theory]
    [InlineData(AnnotationType.Responsibility, "rst.myresp")]
    [InlineData(AnnotationType.Subject, "sbt.mysubject")]
    [InlineData(AnnotationType.Usage, "usg.mysubject.myresp")]
    [InlineData(AnnotationType.Context, "cnt.mysubject.myctx")]
    [InlineData(AnnotationType.Execution, "exe.mysubject.myresp.myctx")]
    [InlineData(AnnotationType.Unit, "unt.myresp.myunit")]
    public void TryGetAncestorKey_UnitOfExecutionAncestors_ReturnExpectedKeys(AnnotationType type, string expected)
    {
        var key = AnnotationKey.CreateUnitOfExecution("mysubject", "myresp", "myctx", "myunit");

        var ancestor = key.TryGetAncestorKey(type);

        ancestor!.ToString().Should().Be(expected);
    }

    [Theory]
    [InlineData(AnnotationType.Subject)]
    [InlineData(AnnotationType.Usage)]
    [InlineData(AnnotationType.Context)]
    [InlineData(AnnotationType.Execution)]
    [InlineData(AnnotationType.Unit)]
    [InlineData(AnnotationType.UnitOfExecution)]
    public void TryGetAncestorKey_InvalidAncestorOfResponsibility_ReturnsNull(AnnotationType type)
    {
        var key = AnnotationKey.CreateResponsibility("myresp");

        var ancestor = key.TryGetAncestorKey(type);

        ancestor.Should().BeNull();
    }

    [Theory]
    [InlineData(AnnotationType.Responsibility)]
    [InlineData(AnnotationType.Usage)]
    [InlineData(AnnotationType.Context)]
    [InlineData(AnnotationType.Execution)]
    [InlineData(AnnotationType.Unit)]
    [InlineData(AnnotationType.UnitOfExecution)]
    public void TryGetAncestorKey_InvalidAncestorOfSubject_ReturnsNull(AnnotationType type)
    {
        var key = AnnotationKey.CreateSubject("mysubject");

        var ancestor = key.TryGetAncestorKey(type);

        ancestor.Should().BeNull();
    }

    [Fact]
    public void TryGetAncestorKey_UnitOfExecutionRequestedAsAncestor_ReturnsNull()
    {
        var key = AnnotationKey.CreateUnitOfExecution("mysubject", "myresp", "myctx", "myunit");

        var ancestor = key.TryGetAncestorKey(AnnotationType.UnitOfExecution);

        ancestor.Should().BeNull();
    }

    [Fact]
    public void TryGetAncestorKey_OwnType_ReturnsEquivalentKey()
    {
        var key = AnnotationKey.CreateExecution("mysubject", "myresp", "myctx");

        var ancestor = key.TryGetAncestorKey(AnnotationType.Execution);

        ancestor!.ToString().Should().Be(key.ToString());
    }
}

using FluentAssertions;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

public class AnnotationHierarchyTests
{
    [Fact]
    public void Responsibility_HasNoAncestors()
    {
        var key = AnnotationKey.CreateResponsibility("myresp");
        var ancestors = AnnotationHierarchy.GetAncestorSchemaSources(key);
        ancestors.Should().BeEmpty();
    }

    [Fact]
    public void Subject_HasNoAncestors()
    {
        var key = AnnotationKey.CreateSubject("mysubject");
        var ancestors = AnnotationHierarchy.GetAncestorSchemaSources(key);
        ancestors.Should().BeEmpty();
    }

    [Fact]
    public void Unit_HasResponsibilityAncestor()
    {
        var key = AnnotationKey.CreateUnit("myresp", "myunit");
        var ancestors = AnnotationHierarchy.GetAncestorSchemaSources(key);

        ancestors.Should().HaveCount(1);
        ancestors[0].Ancestor.ToString().Should().Be("rst.myresp");
        ancestors[0].DescendantTypeCode.Should().Be(AnnotationTypeCodes.Unit);
    }

    [Fact]
    public void Context_HasSubjectAncestor()
    {
        var key = AnnotationKey.CreateContext("mysubject", "mycontext");
        var ancestors = AnnotationHierarchy.GetAncestorSchemaSources(key);

        ancestors.Should().HaveCount(1);
        ancestors[0].Ancestor.ToString().Should().Be("sbt.mysubject");
        ancestors[0].DescendantTypeCode.Should().Be(AnnotationTypeCodes.Context);
    }

    [Fact]
    public void Usage_HasSubjectAndResponsibilityAncestors()
    {
        var key = AnnotationKey.CreateUsage("mysubject", "myresp");
        var ancestors = AnnotationHierarchy.GetAncestorSchemaSources(key);

        ancestors.Should().HaveCount(2);
        ancestors[0].Ancestor.ToString().Should().Be("sbt.mysubject");
        ancestors[0].DescendantTypeCode.Should().Be(AnnotationTypeCodes.Usage);
        ancestors[1].Ancestor.ToString().Should().Be("rst.myresp");
        ancestors[1].DescendantTypeCode.Should().Be(AnnotationTypeCodes.Usage);
    }

    [Fact]
    public void Execution_HasFourAncestors()
    {
        var key = AnnotationKey.CreateExecution("mysubject", "myresp", "myctx");
        var ancestors = AnnotationHierarchy.GetAncestorSchemaSources(key);

        ancestors.Should().HaveCount(4);
        ancestors[0].Ancestor.ToString().Should().Be("sbt.mysubject");
        ancestors[0].DescendantTypeCode.Should().Be(AnnotationTypeCodes.Execution);
        ancestors[1].Ancestor.ToString().Should().Be("rst.myresp");
        ancestors[1].DescendantTypeCode.Should().Be(AnnotationTypeCodes.Execution);
        ancestors[2].Ancestor.ToString().Should().Be("cnt.mysubject.myctx");
        ancestors[2].DescendantTypeCode.Should().Be(AnnotationTypeCodes.Execution);
        ancestors[3].Ancestor.ToString().Should().Be("usg.mysubject.myresp");
        ancestors[3].DescendantTypeCode.Should().Be(AnnotationTypeCodes.Execution);
    }

    [Fact]
    public void UnitOfExecution_HasSixAncestors()
    {
        var key = AnnotationKey.CreateUnitOfExecution("mysubject", "myresp", "myctx", "myunit");
        var ancestors = AnnotationHierarchy.GetAncestorSchemaSources(key);

        ancestors.Should().HaveCount(6);
        ancestors[0].Ancestor.ToString().Should().Be("sbt.mysubject");
        ancestors[0].DescendantTypeCode.Should().Be(AnnotationTypeCodes.UnitOfExecution);
        ancestors[1].Ancestor.ToString().Should().Be("rst.myresp");
        ancestors[1].DescendantTypeCode.Should().Be(AnnotationTypeCodes.UnitOfExecution);
        ancestors[2].Ancestor.ToString().Should().Be("cnt.mysubject.myctx");
        ancestors[2].DescendantTypeCode.Should().Be(AnnotationTypeCodes.UnitOfExecution);
        ancestors[3].Ancestor.ToString().Should().Be("usg.mysubject.myresp");
        ancestors[3].DescendantTypeCode.Should().Be(AnnotationTypeCodes.UnitOfExecution);
        ancestors[4].Ancestor.ToString().Should().Be("unt.myresp.myunit");
        ancestors[4].DescendantTypeCode.Should().Be(AnnotationTypeCodes.UnitOfExecution);
        ancestors[5].Ancestor.ToString().Should().Be("exe.mysubject.myresp.myctx");
        ancestors[5].DescendantTypeCode.Should().Be(AnnotationTypeCodes.UnitOfExecution);
    }
}

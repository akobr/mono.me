using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using _42.Platform.Cli.Commands.Storyteller;
using _42.Platform.Storyteller.Sdk;
using Moq;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Commands;

// The list asks for the active annotations unless --inactive, --deleted or --all asks for more, and marks the others.
public class StorytellerListCommandTests
{
    private readonly AnnotationCommandFixture _fixture = new();

    [Fact]
    public async Task List_ByDefault_AsksForActiveAnnotationsOnly()
    {
        _fixture.Api
            .Setup(api => api.GetAnnotationsAsync("org1", "proj1", "default", null, null, null))
            .ReturnsAsync(Response(AnnotationCommandFixture.Active("sbt.northwind")));

        var exitCode = await Command().OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _fixture.Line("- ").ShouldBe("- sbt.northwind");
    }

    [Fact]
    public async Task List_WithInactive_AsksForThemAndMarksThem()
    {
        _fixture.Api
            .Setup(api => api.GetAnnotationsAsync("org1", "proj1", "default", null, true, null))
            .ReturnsAsync(Response(
                AnnotationCommandFixture.Active("sbt.active"),
                Inactive("sbt.disabled", annotation => annotation.IsDisabled = true),
                Inactive("sbt.scheduled", annotation => annotation.ValidFrom = DateTimeOffset.UtcNow.AddYears(1)),
                Inactive("sbt.expired", annotation => annotation.ExpiresAt = DateTimeOffset.UtcNow.AddYears(-1))));

        var command = Command();
        command.IncludeInactive = true;
        await command.OnExecuteAsync();

        Listed().ShouldBe(
        [
            "- sbt.active",
            "- sbt.disabled [disabled]",
            "- sbt.scheduled [scheduled]",
            "- sbt.expired [expired]",
        ]);
    }

    [Fact]
    public async Task List_All_AsksForInactiveAndDeletedAnnotations()
    {
        _fixture.Api
            .Setup(api => api.GetAnnotationsAsync("org1", "proj1", "default", null, true, true))
            .ReturnsAsync(Response(
                AnnotationCommandFixture.Active("sbt.active"),
                AnnotationCommandFixture.Deleted("sbt.gone")));

        var command = Command();
        command.IncludeAll = true;
        await command.OnExecuteAsync();

        Listed().ShouldBe(["- sbt.active", "- sbt.gone [deleted]"]);
    }

    [Fact]
    public async Task List_PassesTheContinuationToken()
    {
        _fixture.Api
            .Setup(api => api.GetAnnotationsAsync("org1", "proj1", "default", "t1", null, null))
            .ReturnsAsync(Response());

        var command = Command();
        command.ContinuationToken = "t1";
        var exitCode = await command.OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _fixture.Api.Verify(api => api.GetAnnotationsAsync("org1", "proj1", "default", "t1", null, null), Times.Once);
    }

    [Fact]
    public async Task Subjects_WithDeleted_AskForDeletedOnes()
    {
        _fixture.Api
            .Setup(api => api.GetSubjectsAsync("org1", "proj1", "default", "northwind", null, null, true))
            .ReturnsAsync(new AnnotationsResponse_Subject { Annotations = [] });

        var command = Command();
        command.QuerySubjects = "northwind";
        command.IncludeDeleted = true;
        var exitCode = await command.OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
    }

    [Fact]
    public async Task Descendants_WithInactive_AskForThem()
    {
        _fixture.Api
            .Setup(api => api.GetDescendantsAsync("org1", "proj1", "default", "sbt.northwind", "all", null, true, null))
            .ReturnsAsync(Response(Inactive("cnt.northwind.prod", annotation => annotation.IsDisabled = true)));

        var command = Command();
        command.QueryDescendants = "sbt.northwind";
        command.IncludeInactive = true;
        await command.OnExecuteAsync();

        Listed().ShouldBe(["- cnt.northwind.prod [disabled]"]);
    }

    private static Annotation Inactive(string key, Action<Annotation> change)
    {
        var annotation = AnnotationCommandFixture.Active(key);
        change(annotation);
        return annotation;
    }

    private static AnnotationsResponse Response(params Annotation[] annotations)
    {
        return new AnnotationsResponse { Annotations = annotations.ToList(), Count = annotations.Length };
    }

    private StorytellerListCommand Command()
    {
        return new StorytellerListCommand(_fixture.Console.Object, _fixture.Api.Object, _fixture.Context.Object);
    }

    private List<string> Listed()
    {
        return _fixture.Console.Lines.Where(line => line.StartsWith("- ", StringComparison.Ordinal)).ToList();
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using _42.Platform.Cli.Commands.Storyteller;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;
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

    [Fact]
    public async Task Tree_WithoutRoot_IsTheTreeOfSubjects()
    {
        SetupSubjects();

        var command = Command();
        command.ShowAsTree = true;
        var exitCode = await command.OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _fixture.Api.Verify(api => api.GetSubjectsAsync("org1", "proj1", "default", null, null, null, null), Times.Once);
    }

    [Theory]
    [InlineData("subject")]
    [InlineData("sbt")]
    [InlineData("SUBJECT")]
    [InlineData(" Sbt ")]
    public async Task Tree_WithSubjectRoot_IsTheTreeOfSubjects(string root)
    {
        SetupSubjects();

        var command = Command();
        command.ShowAsTree = true;
        command.TreeBaseAnnotationType = root;
        var exitCode = await command.OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _fixture.Api.Verify(api => api.GetSubjectsAsync("org1", "proj1", "default", null, null, null, null), Times.Once);
    }

    [Theory]
    [InlineData("responsibility")]
    [InlineData("rst")]
    [InlineData("Responsibility")]
    [InlineData("RST")]
    public async Task Tree_WithResponsibilityRoot_IsTheTreeOfResponsibilities(string root)
    {
        SetupResponsibilities();

        var command = Command();
        command.ShowAsTree = true;
        command.TreeBaseAnnotationType = root;
        var exitCode = await command.OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _fixture.Api.Verify(api => api.GetResponsibilitiesAsync("org1", "proj1", "default", null, null, null, null), Times.Once);
    }

    // --tree-root names the root of a tree, so it asks for the tree without --tree.
    [Theory]
    [InlineData("rst", true)]
    [InlineData("subject", false)]
    public async Task TreeRoot_WithoutTree_RendersTheTree(string root, bool isResponsibility)
    {
        SetupSubjects();
        SetupResponsibilities();

        var command = Command();
        command.TreeBaseAnnotationType = root;
        var exitCode = await command.OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _fixture.Api.Verify(api => api.GetResponsibilitiesAsync("org1", "proj1", "default", null, null, null, null), Times.Exactly(isResponsibility ? 1 : 0));
        _fixture.Api.Verify(api => api.GetSubjectsAsync("org1", "proj1", "default", null, null, null, null), Times.Exactly(isResponsibility ? 0 : 1));
        _fixture.Api.Verify(api => api.GetAnnotationsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool?>(), It.IsAny<bool?>()), Times.Never);
    }

    // Any other value ends before the first call (the API mock is strict) with the accepted values on stderr.
    [Theory]
    [InlineData("context", true)]
    [InlineData("cnt", true)]
    [InlineData("subjects", true)]
    [InlineData("responsibilty", false)]
    [InlineData("", true)]
    [InlineData("  ", false)]
    public async Task TreeRoot_Unknown_IsRefusedWithTheAcceptedValues(string root, bool showAsTree)
    {
        _fixture.Console.UseInnerConsole(redirectedInput: false);

        var command = Command();
        command.ShowAsTree = showAsTree;
        command.TreeBaseAnnotationType = root;
        var exitCode = await command.OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _fixture.Console.ErrorLines.ShouldBe([$"Unknown tree root '{root}'; use subject (sbt) or responsibility (rst)."]);
        _fixture.Console.Lines.ShouldBeEmpty();
        _fixture.Api.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null, true, StoryTreeRoot.Subject)]
    [InlineData("subject", true, StoryTreeRoot.Subject)]
    [InlineData("SBT", true, StoryTreeRoot.Subject)]
    [InlineData("responsibility", true, StoryTreeRoot.Responsibility)]
    [InlineData(" rst ", true, StoryTreeRoot.Responsibility)]
    [InlineData("usg", false, StoryTreeRoot.Subject)]
    [InlineData("", false, StoryTreeRoot.Subject)]
    public void TryParseTreeRoot_TakesOnlyTheTwoRoots(string? value, bool expected, StoryTreeRoot expectedRoot)
    {
        StorytellerListCommand.TryParseTreeRoot(value, out var root).ShouldBe(expected);
        root.ShouldBe(expectedRoot);
    }

    [Fact]
    public void Help_NamesTheValuesOfTreeRoot()
    {
        var app = new CommandLineApplication<StorytellerListCommand>(NullConsole.Singleton);
        app.Conventions.UseDefaultConventions();

        var treeRoot = app.Options.Single(option => option.LongName == "tree-root");

        treeRoot.ShortName.ShouldBe("b");
        treeRoot.Description.ShouldBe("The root of the tree: subject (sbt, the default) or responsibility (rst). Implies --tree.");
    }

    // The command line as the host builds it: edit is a sub-command of sform story, and its help lists it.
    [Fact]
    public void Edit_IsASubCommandOfStory_AndItsHelpListsIt()
    {
        var app = new CommandLineApplication<StorytellerListCommand>(NullConsole.Singleton);
        app.Conventions.UseDefaultConventions();

        var edit = app.Commands.Single(command => command.Name == "edit");

        // the help wraps the descriptions to the width of the console
        var help = Regex.Replace(app.GetHelpText(), @"\s+", " ");

        edit.ShouldBeAssignableTo<CommandLineApplication<StorytellerEditCommand>>();
        edit.Arguments.Single().Name.ShouldBe(nameof(StorytellerEditCommand.AnnotationKey));
        edit.Options.ShouldNotContain(option => option.LongName == "force");
        help.ShouldContain("edit Edit the content of an annotation in your preferred editor. The saved document replaces the content.");
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

    // No subject, so the tree asks for no executions.
    private void SetupSubjects()
    {
        _fixture.Api
            .Setup(api => api.GetSubjectsAsync("org1", "proj1", "default", null, null, null, null))
            .ReturnsAsync(new AnnotationsResponse_Subject { Annotations = [] });
    }

    private void SetupResponsibilities()
    {
        _fixture.Api
            .Setup(api => api.GetResponsibilitiesAsync("org1", "proj1", "default", null, null, null, null))
            .ReturnsAsync(new AnnotationsResponse_Responsibility { Annotations = [] });
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

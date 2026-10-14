using System.Collections.Generic;
using System.Threading.Tasks;
using _42.Platform.Cli.Commands.Storyteller;
using _42.Platform.Cli.Output;
using _42.Platform.Storyteller.Sdk;
using Moq;
using Sharprompt;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Commands;

public class StorytellerDeleteCommandTests
{
    private const string Key = "sbt.northwind";

    private readonly AnnotationCommandFixture _fixture = new();

    [Fact]
    public async Task Delete_PrintsTheDescendantsAndThePurgeDate()
    {
        var deletion = AnnotationCommandFixture.Deletion(Key, descendantCount: 2);
        _fixture.Api
            .Setup(api => api.DeleteAnnotationAsync("org1", "proj1", "default", Key))
            .ReturnsAsync(new AnnotationDeletionResult { Deletion = deletion, DeletedKeys = ["cnt.northwind.prod", "usg.northwind.billing", Key] });

        var exitCode = await Execute();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _fixture.Line("Deleted").ShouldBe("Deleted 'sbt.northwind' and 2 descendants.");
        _fixture.Line("Restore").ShouldBe(
            $"Restore it with 'sform story restore sbt.northwind' before {AuditConsoleExtensions.FormatDate(deletion.PurgeAt!.Value)}; it is purged automatically after that.");
    }

    [Fact]
    public async Task Delete_OfADeletedKey_ChangesNothing()
    {
        _fixture.Api
            .Setup(api => api.DeleteAnnotationAsync("org1", "proj1", "default", Key))
            .ReturnsAsync(new AnnotationDeletionResult { Deletion = AnnotationCommandFixture.Deletion(Key), DeletedKeys = [] });

        var exitCode = await Execute();

        exitCode.ShouldBe(ExitCodes.WARNING_NO_WORK_NEEDED);
        _fixture.Line("The annotation").ShouldStartWith("The annotation 'sbt.northwind' is already deleted (");
    }

    [Fact]
    public async Task Permanent_OfAnActiveKey_ConfirmsThenDeletesAndPurges()
    {
        ActiveKey(descendants: 3);
        Confirms(true);
        var calls = new List<string>();
        _fixture.Api
            .SetupSequence(api => api.PurgeAnnotationAsync("org1", "proj1", "default", Key))
            .ThrowsAsync(AnnotationCommandFixture.Error(409, "AnnotationNotDeleted", "The annotation is not deleted."))
            .ReturnsAsync(new AnnotationPurgeResult { PurgedKeys = ["cnt.a.b", "usg.a.b", "exe.a.b.c", Key] });
        _fixture.Api
            .Setup(api => api.DeleteAnnotationAsync("org1", "proj1", "default", Key))
            .Callback(() => calls.Add("delete"))
            .ReturnsAsync(new AnnotationDeletionResult { Deletion = AnnotationCommandFixture.Deletion(Key), DeletedKeys = [Key] });

        var exitCode = await Execute(permanent: true);

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        calls.ShouldBe(["delete"]);
        _fixture.Api.Verify(api => api.PurgeAnnotationAsync("org1", "proj1", "default", Key), Times.Exactly(2));
        _fixture.Console.Mock.Verify(console => console.Confirm(It.Is<ConfirmOptions>(options =>
            options.Message.Contains("its 3 descendants") && options.DefaultValue == false)));
        _fixture.Line("Deleted").ShouldBe("Deleted 'sbt.northwind' and 3 descendants permanently.");
    }

    [Fact]
    public async Task Permanent_OfADeletedKey_PurgesWithoutDeletingAgain()
    {
        DeletedKey(AnnotationCommandFixture.Deletion(Key, descendantCount: 1));
        Confirms(true);
        _fixture.Api
            .Setup(api => api.PurgeAnnotationAsync("org1", "proj1", "default", Key))
            .ReturnsAsync(new AnnotationPurgeResult { PurgedKeys = ["cnt.northwind.prod", Key] });

        var exitCode = await Execute(permanent: true);

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _fixture.Console.Mock.Verify(console => console.Confirm(It.Is<ConfirmOptions>(options => options.Message.Contains("its 1 deleted descendant"))));
        _fixture.Api.Verify(api => api.DeleteAnnotationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Permanent_Declined_DeletesNothing()
    {
        DeletedKey(AnnotationCommandFixture.Deletion(Key));
        Confirms(false);

        var exitCode = await Execute(permanent: true);

        exitCode.ShouldBe(ExitCodes.WARNING_ABORTED);
        _fixture.Line("Nothing").ShouldBe("Nothing was deleted.");
        _fixture.Api.Verify(api => api.PurgeAnnotationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Permanent_FailingAfterTheSoftDelete_SaysTheKeyIsStillRestorable()
    {
        ActiveKey(descendants: 0);
        var deletion = AnnotationCommandFixture.Deletion(Key);
        _fixture.Api
            .SetupSequence(api => api.PurgeAnnotationAsync("org1", "proj1", "default", Key))
            .ThrowsAsync(AnnotationCommandFixture.Error(409, "AnnotationNotDeleted", "The annotation is not deleted."))
            .ThrowsAsync(AnnotationCommandFixture.Error(503));
        _fixture.Api
            .Setup(api => api.DeleteAnnotationAsync("org1", "proj1", "default", Key))
            .ReturnsAsync(new AnnotationDeletionResult { Deletion = deletion, DeletedKeys = [Key] });

        await Should.ThrowAsync<ApiException>(() => Execute(permanent: true, yes: true));

        _fixture.Line("The annotation").ShouldBe("The annotation 'sbt.northwind' was deleted, but not permanently.");
        _fixture.Line("Restore").ShouldContain(AuditConsoleExtensions.FormatDate(deletion.PurgeAt!.Value));
    }

    [Fact]
    public async Task Permanent_WithoutTheRole_ChangesNothing()
    {
        ActiveKey(descendants: 0);
        _fixture.Api
            .Setup(api => api.PurgeAnnotationAsync("org1", "proj1", "default", Key))
            .ThrowsAsync(AnnotationCommandFixture.Error(403, "AccessDenied", "Denied."));

        var exitCode = await Execute(permanent: true, yes: true);

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _fixture.Line("Only").ShouldBe("Only an Administrator or Owner can delete permanently.");
        _fixture.Api.Verify(api => api.DeleteAnnotationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _fixture.Console.Mock.Verify(console => console.Confirm(It.IsAny<ConfirmOptions>()), Times.Never);
    }

    [Fact]
    public async Task Permanent_OfAnUnknownKey_AnswersNotFound()
    {
        _fixture.Api
            .Setup(api => api.GetAnnotationAsync("org1", "proj1", "default", Key, true, null))
            .ThrowsAsync(AnnotationCommandFixture.Error(404));
        _fixture.Api
            .Setup(api => api.GetDeletedAnnotationAsync("org1", "proj1", "default", Key))
            .ThrowsAsync(AnnotationCommandFixture.Error(404));

        var exitCode = await Execute(permanent: true);

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _fixture.Line("The annotation").ShouldBe("The annotation 'sbt.northwind' has not been found.");
    }

    private void ActiveKey(int descendants)
    {
        _fixture.Api
            .Setup(api => api.GetAnnotationAsync("org1", "proj1", "default", Key, true, null))
            .ReturnsAsync(new Annotation { AnnotationKey = Key });

        var children = new List<Annotation>();

        for (var index = 0; index < descendants; index++)
        {
            children.Add(new Annotation { AnnotationKey = $"cnt.northwind.c{index}" });
        }

        _fixture.Api
            .Setup(api => api.GetDescendantsAsync("org1", "proj1", "default", Key, "all", null, true, null))
            .ReturnsAsync(new AnnotationsResponse { Annotations = children, Count = children.Count });
    }

    private void DeletedKey(AnnotationDeletion deletion)
    {
        _fixture.Api
            .Setup(api => api.GetAnnotationAsync("org1", "proj1", "default", Key, true, null))
            .ThrowsAsync(AnnotationCommandFixture.Error(404));
        _fixture.Api
            .Setup(api => api.GetDeletedAnnotationAsync("org1", "proj1", "default", Key))
            .ReturnsAsync(AnnotationCommandFixture.Deleted(Key, deletion));
    }

    private void Confirms(bool answer)
    {
        _fixture.Console.Mock.Setup(console => console.Confirm(It.IsAny<ConfirmOptions>())).Returns(answer);
    }

    private Task<int> Execute(bool permanent = false, bool yes = false)
    {
        var command = new StorytellerDeleteCommand(_fixture.Console.Object, _fixture.Context.Object, _fixture.Api.Object)
        {
            AnnotationKey = Key,
            Permanent = permanent,
            Yes = yes,
        };

        return command.OnExecuteAsync();
    }
}

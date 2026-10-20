using System.IO.Abstractions;
using System.Linq;
using System.Threading.Tasks;
using _42.Platform.Cli.Commands.Storyteller;
using _42.Platform.Cli.Output;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Commands;

// get of a deleted key, restore of many keys, and history of a purged key
public class StorytellerReadCommandsTests
{
    private readonly AnnotationCommandFixture _fixture = new();

    [Fact]
    public async Task Get_OfADeletedKey_SaysWhenAndHowToRestore()
    {
        const string key = "sbt.northwind";
        var deletion = AnnotationCommandFixture.Deletion(key);
        _fixture.Api
            .Setup(api => api.GetAnnotationAsync("org1", "proj1", "default", key, true, null))
            .ThrowsAsync(AnnotationCommandFixture.Error(404));
        _fixture.Api
            .Setup(api => api.GetDeletedAnnotationAsync("org1", "proj1", "default", key))
            .ReturnsAsync(AnnotationCommandFixture.Deleted(key, deletion));

        var command = new StorytellerGetCommand(_fixture.Console.Object, _fixture.Context.Object, _fixture.Api.Object, Mock.Of<IFileSystem>())
        {
            AnnotationKey = key,
        };

        var exitCode = await command.OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _fixture.Line("The annotation").ShouldBe(
            $"The annotation 'sbt.northwind' is deleted ({AuditConsoleExtensions.FormatDeletion(deletion)})"
            + $" and will be purged on {AuditConsoleExtensions.FormatDate(deletion.PurgeAt!.Value)}."
            + " Restore it with 'sform story restore sbt.northwind'.");
    }

    [Fact]
    public async Task Restore_SendsTheKeysInChunksAndFailsWhenAnyKeyFailed()
    {
        var keys = Enumerable.Range(0, 150).Select(index => $"sbt.s{index}").ToArray();
        var requests = new System.Collections.Generic.List<AnnotationRestoreRequest>();
        _fixture.Api
            .Setup(api => api.RestoreAnnotationsAsync("org1", "proj1", "default", It.IsAny<AnnotationRestoreRequest>()))
            .Callback<string, string, string, AnnotationRestoreRequest>((_, _, _, request) => requests.Add(request))
            .ReturnsAsync((string _, string _, string _, AnnotationRestoreRequest request) => new AnnotationRestoreResult
            {
                Annotations = request.Keys.Where(key => key != "sbt.s7").Select(key => new Annotation { AnnotationKey = key }).ToList(),
                CreatedKeys = [],
                Failed = request.Keys.Contains("sbt.s7")
                    ? [new AnnotationKeyFailure { Key = "sbt.s7", ErrorCode = "NotFound", Message = "It was deleted permanently." }]
                    : [],
            });

        var command = new StorytellerRestoreCommand(_fixture.Console.Object, _fixture.Context.Object, _fixture.Api.Object)
        {
            AnnotationKeys = keys,
            WithoutDescendants = true,
        };

        var exitCode = await command.OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        requests.Select(request => request.Keys.Count).ShouldBe([100, 50]);
        requests.ShouldAllBe(request => !request.Descendants);
        _fixture.Console.Lines.ShouldContain("- sbt.s7: It was deleted permanently.");
        _fixture.Console.Lines.Count(line => line.StartsWith("- sbt.s")).ShouldBe(150);
    }

    [Fact]
    public void Restore_AcceptsManyKeys()
    {
        var services = new ServiceCollection()
            .AddSingleton(_fixture.Console.Object)
            .AddSingleton(_fixture.Context.Object)
            .AddSingleton(_fixture.Api.Object)
            .BuildServiceProvider();
        var app = new CommandLineApplication<StorytellerRestoreCommand>();
        app.Conventions.UseDefaultConventions().UseConstructorInjection(services);

        app.Parse("sbt.a", "usg.a.b", "--without-descendants");

        app.Model.AnnotationKeys.ShouldBe(["sbt.a", "usg.a.b"]);
        app.Model.WithoutDescendants.ShouldBeTrue();
    }

    [Fact]
    public async Task Restore_RejectedLater_StillReportsTheEarlierRequests()
    {
        var keys = Enumerable.Range(0, 150).Select(index => $"sbt.s{index}").ToArray();
        _fixture.Api
            .SetupSequence(api => api.RestoreAnnotationsAsync("org1", "proj1", "default", It.IsAny<AnnotationRestoreRequest>()))
            .ReturnsAsync(new AnnotationRestoreResult { Annotations = [AnnotationCommandFixture.Active("sbt.s0")], CreatedKeys = [], Failed = [] })
            .ThrowsAsync(AnnotationCommandFixture.Error(400, "InvalidKey", "Invalid annotation keys."));

        var command = new StorytellerRestoreCommand(_fixture.Console.Object, _fixture.Context.Object, _fixture.Api.Object)
        {
            AnnotationKeys = keys,
        };

        var exitCode = await command.OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _fixture.Console.Lines.ShouldContain("- sbt.s0");
        _fixture.Console.Lines.ShouldContain("Invalid annotation keys.");
    }

    [Fact]
    public async Task Restore_WithoutTheManagementScope_ReportsTheEarlierRequestsAndTheServerMessage()
    {
        var keys = Enumerable.Range(0, 150).Select(index => $"sbt.s{index}").ToArray();
        _fixture.Api
            .SetupSequence(api => api.RestoreAnnotationsAsync("org1", "proj1", "default", It.IsAny<AnnotationRestoreRequest>()))
            .ReturnsAsync(new AnnotationRestoreResult { Annotations = [AnnotationCommandFixture.Active("sbt.s0")], CreatedKeys = [], Failed = [] })
            .ThrowsAsync(AnnotationCommandFixture.MissingManagementScope("Annotation.Manage"));

        var command = new StorytellerRestoreCommand(_fixture.Console.Object, _fixture.Context.Object, _fixture.Api.Object)
        {
            AnnotationKeys = keys,
        };

        var exitCode = await command.OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.WARNING_UNAUTHORIZED_ACCESS);
        _fixture.Console.Lines.ShouldContain("- sbt.s0");
        _fixture.Console.Lines.ShouldContain($"! {AnnotationCommandFixture.MissingManagementScopeMessage("Annotation.Manage")}");
        _fixture.Console.Lines.ShouldContain("Error code: AccessDenied");
    }

    [Fact]
    public async Task History_OfAPurgedKey_AnswersNotFound()
    {
        _fixture.Api
            .Setup(api => api.GetAnnotationHistoryAsync("org1", "proj1", "default", "sbt.gone"))
            .ThrowsAsync(AnnotationCommandFixture.Error(404));

        var command = new StorytellerHistoryCommand(_fixture.Console.Object, _fixture.Context.Object, _fixture.Api.Object)
        {
            AnnotationKey = "sbt.gone",
        };

        var exitCode = await command.OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _fixture.Line("The annotation").ShouldContain("deleted permanently");
    }
}

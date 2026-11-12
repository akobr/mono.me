using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.DbCreator.Commands;
using _42.Platform.Storyteller.DbCreator.Logic;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using Moq;

namespace _42.Platform.Storyteller.DbCreator.UnitTests.Commands;

// 2sInitDb without a command: the checks before anything is written, the seed of the set core, and their exit codes.
// What it writes is covered by the Cosmos DB suite (OrganizationsContainerStructureTests).
public class InitDbCommandTests
{
    private readonly SeedFixture _fixture = new();
    private readonly Mock<IAnnotationService> _annotations = new();

    [Fact]
    public async Task Root_AnInvalidBaseAccount_ExitsWithTwoBeforeTheDatabaseIsTouched()
    {
        _fixture.BaseAccount.Id = "not:an-entra-id";

        (await Command().OnExecuteAsync()).ShouldBe(ExitCodes.ERROR_WRONG_INPUT);

        _fixture.Containers.Invocations.ShouldBeEmpty();
    }

    [Fact]
    public async Task Root_ADatabaseOfTheOldLayout_ExitsWithThreeBeforeAnythingIsWritten()
    {
        _fixture.LayoutProblems.Add("The container 'org.fabrikam' is of the layout before the shared container.");

        (await Command().OnExecuteAsync()).ShouldBe(ExitCodes.ERROR_DATABASE_LAYOUT);

        _fixture.Containers.Verify(factory => factory.CreateContainerIfNotExistsAsync(It.IsAny<string>()), Times.Never);
        _fixture.Containers.Verify(factory => factory.EnsureOrganizationsContainerAsync(), Times.Never);
    }

    [Fact]
    public async Task Root_AFailedDocumentOfTheSetCore_ExitsWithFourWithoutTheReview()
    {
        _fixture.AddSet(
            CoreDbStructureBuilder.CORE_SET,
            """{ "Organization": "42dotnet", "Project": "main" }""",
            ("annotations/sbt.42.json", "{}"),
            ("annotations/sbt.bad.json", "{}"));
        _fixture.Importer.FailingKeys.Add("sbt.bad");

        (await Command().OnExecuteAsync()).ShouldBe(ExitCodes.ERROR_SEED_FAILED);

        // the access is seeded before the set, the set is left without its record, and the review is not rendered
        _fixture.Container.Verify(
            container => container.CreateItemAsync(It.IsAny<It.IsAnyType>(), It.IsAny<PartitionKey?>(), It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
        _fixture.Importer.Calls.ShouldHaveSingleItem().Organization.ShouldBe("42dotnet");
        _fixture.Records.Saves.ShouldBe(0);
        _annotations.Invocations.ShouldBeEmpty();
    }

    private InitDbCommand Command()
    {
        return new InitDbCommand(
            _fixture.Structure(),
            new GetBaseAnnotationsReview(_annotations.Object, Options.Create(_fixture.BaseAccount)),
            Options.Create(_fixture.BaseAccount));
    }
}

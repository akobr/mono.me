using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.DbCreator.Commands;
using _42.Platform.Storyteller.DbCreator.Logic;
using Microsoft.Extensions.Options;
using Moq;

namespace _42.Platform.Storyteller.DbCreator.UnitTests.Commands;

// 2sInitDb without a command: the checks before anything is written and their exit codes. What it writes is covered by
// the Cosmos DB suite (OrganizationsContainerStructureTests).
public class InitDbCommandTests
{
    private readonly SeedFixture _fixture = new();

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

    private InitDbCommand Command()
    {
        return new InitDbCommand(
            _fixture.Structure(),
            new GetBaseAnnotationsReview(new Mock<IAnnotationService>().Object, Options.Create(_fixture.BaseAccount)),
            Options.Create(_fixture.BaseAccount));
    }
}

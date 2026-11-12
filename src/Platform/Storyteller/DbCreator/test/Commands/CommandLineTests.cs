using _42.Platform.Storyteller.DbCreator.Commands;
using _42.Platform.Storyteller.DbCreator.DataSets;
using McMaster.Extensions.CommandLineUtils;
using McMaster.Extensions.CommandLineUtils.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace _42.Platform.Storyteller.DbCreator.UnitTests.Commands;

// The command line as the tool parses it: the commands built by the tool's own registrations (AddCosmosDbAnnotations
// and AddDbCreator) through McMaster's constructor injection, which builds the model of every command on the path, the
// root included. Building them opens no connection.
public sealed class CommandLineTests : IDisposable
{
    private readonly ServiceProvider _services;

    public CommandLineTests()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // the emulator's well-known key on a port nothing listens on: never reached
                ["ConnectionStrings:cosmos"] = "AccountEndpoint=https://localhost:1/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddCosmosDbAnnotations(configuration, "cosmos");
        services.AddDbCreator(configuration);
        _services = services.BuildServiceProvider();
    }

    [Fact]
    public void Seed_TakesTheSetsAndEveryOption()
    {
        var command = Parse<SeedCommand>(
            "seed",
            "large",
            "core",
            "--path",
            "one",
            "--path",
            "two",
            "--organization",
            "acme",
            "--project",
            "billing",
            "--view",
            "preview",
            "--owner",
            "user_01owner",
            "--dry-run",
            "--force",
            "--again");

        var request = command.CreateRequest();

        request.Sets.ShouldBe(["large", "core"]);
        request.Paths.ShouldBe(["one", "two"]);
        (request.Organization, request.Project, request.View, request.Owner).ShouldBe(("acme", "billing", "preview", "user_01owner"));
        (request.Options.DryRun, request.Options.Force, request.Options.Again).ShouldBe((true, true, true));
    }

    [Fact]
    public void Seed_WithoutArguments_IsTheDefaultRequest()
    {
        Parse<SeedCommand>("seed").CreateRequest().ShouldBeEquivalentTo(new DataSetSeedRequest());
    }

    [Fact]
    public void Sets_TakesThePaths()
    {
        Parse<SetsCommand>("sets", "--path", "one", "--path", "two").Paths.ShouldBe(["one", "two"]);
        Parse<SetsCommand>("sets").Paths.ShouldBeNull();
    }

    [Fact]
    public void NoCommand_IsTheRoot()
    {
        Parse<InitDbCommand>().ShouldNotBeNull();
    }

    [Theory]
    [InlineData("seed", "--path")]
    [InlineData("seed", "--organization")]
    [InlineData("seed", "--project", "a", "--project", "b")]
    [InlineData("sets", "--path")]
    public void AMissingOrRepeatedValue_IsAParsingError(params string[] args)
    {
        // Program maps it to ERROR_INPUT_PARSING; before, it ended as a crash
        var exception = Should.Throw<CommandParsingException>(() => Application().Parse(args));

        exception.ShouldNotBeOfType<UnrecognizedCommandParsingException>();
    }

    [Theory]
    [InlineData("seeed")]
    [InlineData("seed", "--unknown")]
    [InlineData("sets", "large")]
    public void AnUnknownCommandOrOption_IsUnrecognized(params string[] args)
    {
        Should.Throw<UnrecognizedCommandParsingException>(() => Application().Parse(args));
    }

    [Fact]
    public void EveryCommand_IsBuiltByTheContainer()
    {
        var application = Application();
        var commands = application.Commands.Prepend<CommandLineApplication>(application).ToList();

        commands.Select(command => command.Name).ShouldBe(["2sInitDb", SeedCommand.Name, SetsCommand.Name], ignoreOrder: true);

        foreach (var command in commands)
        {
            ((IModelAccessor)command).GetModel().ShouldNotBeNull(command.Name);
        }
    }

    public void Dispose()
    {
        _services.Dispose();
    }

    private TCommand Parse<TCommand>(params string[] args)
        where TCommand : class
    {
        var result = Application().Parse(args);
        return ((IModelAccessor)result.SelectedCommand).GetModel().ShouldBeOfType<TCommand>();
    }

    private CommandLineApplication<InitDbCommand> Application()
    {
        var application = new CommandLineApplication<InitDbCommand>(NullConsole.Singleton);
        application.Conventions.UseDefaultConventions().UseConstructorInjection(_services);
        return application;
    }
}

using _42.Platform.Storyteller.Api.Exhibitions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// The settings of the exhibition (section Exhibition): all three, or none; anything else stops the start.
public class ExhibitionOptionsTests
{
    private const string Organization = "fourth-wall";
    private const string Project = "toons-and-tyrants";
    private const string Owner = "user_01JEXHIBITIONOWNER";

    // Without the settings the deployment has no exhibition; a blank setting is not set.
    [Theory]
    [InlineData(null, null, null)]
    [InlineData("", "", "")]
    [InlineData(" ", null, "")]
    public void None_IsValidAndNotConfigured(string? organization, string? project, string? owner)
    {
        var options = new ExhibitionOptions { Organization = organization, Project = project, Owner = owner };

        options.IsValid.ShouldBeTrue();
        options.IsConfigured.ShouldBeFalse();
    }

    [Theory]
    [InlineData(Organization, Project)]
    [InlineData("exhibition", "exhibition")]
    [InlineData("42", "a-1")]
    public void AllThree_AreValidAndConfigured(string organization, string project)
    {
        var options = new ExhibitionOptions { Organization = organization, Project = project, Owner = Owner };

        options.IsValid.ShouldBeTrue();
        options.IsConfigured.ShouldBeTrue();
        options.ProjectKey.ShouldBe($"{organization}.{project}");
    }

    [Theory]
    [InlineData(Organization, null, null)]
    [InlineData(null, Project, null)]
    [InlineData(null, null, Owner)]
    [InlineData(Organization, Project, null)]
    [InlineData(Organization, null, Owner)]
    [InlineData(null, Project, Owner)]
    [InlineData(Organization, Project, " ")]
    [InlineData("", Project, Owner)]
    public void OneOrTwo_AreNotValid(string? organization, string? project, string? owner)
    {
        var options = new ExhibitionOptions { Organization = organization, Project = project, Owner = owner };

        options.IsValid.ShouldBeFalse();
        options.IsConfigured.ShouldBeFalse();
    }

    // The names are compared as written, and a valid name is lower-case and has no dot (NameRules.IsValidName).
    [Theory]
    [InlineData("Fourth-Wall", Project)]
    [InlineData(Organization, "Toons-And-Tyrants")]
    [InlineData("fourth.wall", Project)]
    [InlineData(Organization, "toons.and.tyrants")]
    [InlineData(" fourth-wall", Project)]
    [InlineData(Organization, "toons-and-tyrants ")]
    [InlineData("f", Project)]
    [InlineData(Organization, "-toons")]
    public void AllThreeWithANameThatIsNotValid_AreNotValid(string organization, string project)
    {
        var options = new ExhibitionOptions { Organization = organization, Project = project, Owner = Owner };

        options.IsValid.ShouldBeFalse();
        options.IsConfigured.ShouldBeTrue();
    }

    // The owner is compared as written too, and no account id has white space around it (a registration and the seed
    // trim it): a padded owner, from a paste, would start and never match.
    [Theory]
    [InlineData(" " + Owner)]
    [InlineData(Owner + " ")]
    [InlineData(Owner + "\n")]
    [InlineData("\t" + Owner + "\r\n")]
    public void AllThreeWithAnOwnerInWhiteSpace_AreNotValid(string owner)
    {
        var options = new ExhibitionOptions { Organization = Organization, Project = Project, Owner = owner };

        options.IsValid.ShouldBeFalse();
        options.IsConfigured.ShouldBeTrue();
    }

    // The section binds by its setting names; in the environment they are Exhibition__Organization and so on.
    [Fact]
    public void AddExhibition_BindsTheSection()
    {
        using var services = new ServiceCollection().AddExhibition(Configuration(Organization, Project, Owner)).BuildServiceProvider();

        var options = services.GetRequiredService<IOptions<ExhibitionOptions>>().Value;

        options.Organization.ShouldBe(Organization);
        options.Project.ShouldBe(Project);
        options.Owner.ShouldBe(Owner);
        options.IsConfigured.ShouldBeTrue();
        options.ProjectKey.ShouldBe($"{Organization}.{Project}");
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData(Organization, Project, Owner)]
    public async Task AddExhibition_NoneOrAllThree_Starts(string? organization, string? project, string? owner)
    {
        using var host = new HostBuilder().ConfigureServices(services => services.AddExhibition(Configuration(organization, project, owner))).Build();

        await host.StartAsync();
        await host.StopAsync();

        host.Services.GetRequiredService<IOptions<ExhibitionOptions>>().Value.IsConfigured.ShouldBe(organization is not null);
    }

    // The message names the three settings and never a value: the owner is the id of a person's account.
    [Theory]
    [InlineData(Organization, null, null)]
    [InlineData(null, Project, null)]
    [InlineData(null, null, Owner)]
    [InlineData(Organization, Project, null)]
    [InlineData(Organization, null, Owner)]
    [InlineData(null, Project, Owner)]
    [InlineData("Fourth-Wall", Project, Owner)]
    [InlineData(Organization, "toons.and.tyrants", Owner)]
    [InlineData(Organization, Project, Owner + " ")]
    [InlineData(Organization, Project, " " + Owner)]
    public async Task AddExhibition_OneOrTwoOrANameOrAnOwnerThatIsNotValid_StopsTheStart(string? organization, string? project, string? owner)
    {
        using var host = new HostBuilder().ConfigureServices(services => services.AddExhibition(Configuration(organization, project, owner))).Build();

        var exception = await Should.ThrowAsync<OptionsValidationException>(() => host.StartAsync());

        exception.Message.ShouldContain("Exhibition:Organization", Case.Sensitive);
        exception.Message.ShouldContain("Exhibition:Project", Case.Sensitive);
        exception.Message.ShouldContain("Exhibition:Owner", Case.Sensitive);
        exception.Message.ShouldContain("white space", Case.Sensitive);

        foreach (var value in new[] { organization, project, owner }.OfType<string>())
        {
            exception.Message.ShouldNotContain(value, Case.Insensitive);
        }
    }

    private static IConfiguration Configuration(string? organization, string? project, string? owner)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Exhibition:Organization"] = organization,
                ["Exhibition:Project"] = project,
                ["Exhibition:Owner"] = owner,
            })
            .Build();
    }
}

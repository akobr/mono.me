using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Configuring;
using _42.Platform.Storyteller.DbCreator.Commands;
using _42.Platform.Storyteller.DbCreator.DataSets;
using _42.Platform.Storyteller.DbCreator.Logic;
using Microsoft.Extensions.DependencyInjection;

namespace _42.Platform.Storyteller.DbCreator.IntegrationTests;

// The services of a seed test. Every test seeds into an organization of its own, so the tests share the database of
// the fixture without seeing each other.
[Collection(IntegrationTestCollection.Name)]
public abstract class SeedTestsBase(DbCreatorFixture fixture)
{
    protected DbCreatorFixture Fixture => fixture;

    protected IServiceProvider Services => fixture.Services;

    protected DataSetSeeder Seeder => Services.GetRequiredService<DataSetSeeder>();

    protected IDataSetSeedRecordStore Records => Services.GetRequiredService<IDataSetSeedRecordStore>();

    protected IAccessService Access => Services.GetRequiredService<IAccessService>();

    protected IViewService Views => Services.GetRequiredService<IViewService>();

    protected IAnnotationService Annotations => Services.GetRequiredService<IAnnotationService>();

    protected IConfigurationService Configurations => Services.GetRequiredService<IConfigurationService>();

    protected IConfigurationSchemaService Schemas => Services.GetRequiredService<IConfigurationSchemaService>();

    protected IConfigurationTemplateService Templates => Services.GetRequiredService<IConfigurationTemplateService>();

    protected static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    // valid by the name rules: 2 to 63 lower-case letters, digits or hyphens
    protected static string NewOrganization() => $"dc-{Guid.NewGuid():N}"[..14];

    // an AuthKit-like id without ':', its own user name and name (DataSetOwner)
    protected static string NewOwner() => $"dbcreator-owner-{Guid.NewGuid():N}";

    protected static FullKey Key(DataSetTarget target, string key)
    {
        return FullKey.Create(AnnotationKey.Parse(key), target.Organization, target.Project, target.View);
    }

    protected static void Log(string message)
    {
        TestContext.Current.TestOutputHelper?.WriteLine(message);
    }

    // The command as the tool runs it (the containers ensured, the output on the console), for its exit code.
    protected SeedCommand Command()
    {
        return new SeedCommand(Services.GetRequiredService<CoreDbStructureBuilder>(), Seeder);
    }
}

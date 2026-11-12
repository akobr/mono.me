using _42.Platform.Storyteller.DbCreator.DataSets;

namespace _42.Platform.Storyteller.DbCreator.UnitTests.DataSets;

public class DataSetTargetTests
{
    [Fact]
    public void Resolve_TakesTheGivenNamesOverTheManifests()
    {
        var sets = Sets(("showcase", "fabrikam", "invoicing-suite", "preview"));

        DataSetTarget.Resolve(sets, " contoso ", "billing", "draft").ShouldBe(new DataSetTarget("contoso", "billing", "draft"));
        DataSetTarget.Resolve(sets, "contoso").ShouldBe(new DataSetTarget("contoso", "invoicing-suite", "preview"));
        DataSetTarget.Resolve(sets, organization: " ", project: null, view: string.Empty).ShouldBe(new DataSetTarget("fabrikam", "invoicing-suite", "preview"));
    }

    [Fact]
    public void Resolve_TakesTheManifestsThatAgreeAndDefaultsTheView()
    {
        var sets = Sets(("showcase", "fabrikam", "invoicing-suite", null), ("extra", "fabrikam", null, null));

        var target = DataSetTarget.Resolve(sets);

        target.ShouldBe(new DataSetTarget("fabrikam", "invoicing-suite", "default"));
        target.IsDefaultView.ShouldBeTrue();
        target.ProjectKey.ShouldBe("fabrikam.invoicing-suite");
        target.ToString().ShouldBe("fabrikam/invoicing-suite/default");
    }

    [Fact]
    public void Resolve_RefusesManifestsThatDisagree()
    {
        var sets = Sets(("showcase", "fabrikam", "invoicing-suite", "preview"), ("other", "contoso", "invoicing-suite", "draft"));

        Should.Throw<DataSetException>(() => DataSetTarget.Resolve(sets))
            .Message.ShouldBe("The sets name different organizations to seed into ('fabrikam' in showcase, 'contoso' in other): give --organization.");
        Should.Throw<DataSetException>(() => DataSetTarget.Resolve(sets, "fabrikam"))
            .Message.ShouldBe("The sets name different views to seed into ('preview' in showcase, 'draft' in other): give --view.");

        // the given names settle it
        DataSetTarget.Resolve(sets, "fabrikam", view: "default").ShouldBe(new DataSetTarget("fabrikam", "invoicing-suite", "default"));
    }

    [Fact]
    public void Resolve_RefusesATargetWithoutAnOrganizationOrAProject()
    {
        var sets = Sets(("bare", null, null, null));

        Should.Throw<DataSetException>(() => DataSetTarget.Resolve(sets, project: "main")).Message.ShouldContain("No organization");
        Should.Throw<DataSetException>(() => DataSetTarget.Resolve(sets, "fabrikam")).Message.ShouldContain("No project");
        DataSetTarget.Resolve(sets, "fabrikam", "main").ShouldBe(new DataSetTarget("fabrikam", "main", "default"));
    }

    [Theory]
    [InlineData("Fabrikam", "main", "default", true, "organization name 'Fabrikam' is not valid")]
    [InlineData("login", "main", "default", true, "organization name 'login' is reserved")]
    [InlineData("fabrikam", "Main", "default", false, "project name 'Main' is not valid")]
    [InlineData("fabrikam", "access", "default", false, "project name 'access' is reserved")]
    [InlineData("fabrikam", "main", "views", false, "view name 'views' is reserved")]
    [InlineData("fabrikam", "main", "pre.view", false, "view name 'pre.view' is not valid")]
    public void EnsureNames_RefusesANameAgainstTheRules(string organization, string project, string view, bool isNewOrganization, string message)
    {
        var target = new DataSetTarget(organization, project, view);

        Should.Throw<DataSetException>(() => target.EnsureNames(isNewOrganization)).Message.ShouldContain(message);
    }

    [Fact]
    public void EnsureNames_ChecksAnExistingOrganizationNot()
    {
        // an organization from before the rules can still get a seeded project
        Should.NotThrow(() => new DataSetTarget("Legacy_Org", "main", "default").EnsureNames(isNewOrganization: false));
        Should.NotThrow(() => new DataSetTarget("fabrikam", "invoicing-suite", "preview").EnsureNames(isNewOrganization: true));
    }

    private static List<DataSet> Sets(params (string Name, string? Organization, string? Project, string? View)[] sets)
    {
        return sets
            .Select(set => new DataSet(
                new DataSetManifest { Name = set.Name, Organization = set.Organization, Project = set.Project, View = set.View },
                SetFiles.Folder(set.Name),
                DataSetSource.BuiltIn))
            .ToList();
    }
}

using System.IO.Abstractions.TestingHelpers;
using _42.Testing.UI.Automation.Runtime.Settings;

namespace _42.Platform.Automation.Shared.UnitTests;

// The shared keys of Target: bound from the settings, checked by Validate with the messages both suites show, and the
// project of a role.
public sealed class PlatformTargetSettingsTests
{
    private const string NameRule = "2 to 63 lower-case letters, digits and hyphens, not starting with a hyphen.";

    [Fact]
    public void Bind_ReadsTheSharedKeys_WithRoleProjectsCaseInsensitive()
    {
        var target = Load("""
            {
              "Target": {
                "BaseUrl": "https://api.example.test/api",
                "Organization": "acme",
                "Project": "billing",
                "View": "staging",
                "RoleProjects": { "contributor": "acme/shipping", "Reader": "northwind/portal/default" }
              }
            }
            """).Bind<PlatformTargetSettings>(PlatformTargetSettings.Section);

        target.BaseUrl.ShouldBe("https://api.example.test/api");
        target.Organization.ShouldBe("acme");
        target.Project.ShouldBe("billing");
        target.View.ShouldBe("staging");
        target.RoleProjectOf(Role.Contributor).ShouldBe("acme/shipping");
        target.RoleProjectOf(Role.Reader).ShouldBe("northwind/portal/default");
        target.RoleProjects["CONTRIBUTOR"].ShouldBe("acme/shipping");

        var errors = new List<string>();
        target.Validate(errors);
        errors.ShouldBeEmpty();
    }

    [Fact]
    public void Validate_ListsEveryNameThatIsNotAPlatformName()
    {
        var target = new PlatformTargetSettings { Organization = string.Empty, Project = "Billing", View = "-staging" };
        var errors = new List<string>();

        target.Validate(errors);

        errors.ShouldBe([
            $"Target:Organization '' is not a Platform name: {NameRule}",
            $"Target:Project 'Billing' is not a Platform name: {NameRule}",
            $"Target:View '-staging' is not a Platform name: {NameRule}",
        ]);
    }

    [Theory]
    [InlineData("Any")]
    [InlineData("Writer")]
    [InlineData("1")]
    public void Validate_RefusesARoleProjectsEntry_OfARoleNoTestActsAs(string role)
    {
        var target = Valid();
        target.RoleProjects = new Dictionary<string, string> { [role] = "acme/shipping" };
        var errors = new List<string>();

        target.Validate(errors);

        errors.ShouldHaveSingleItem().ShouldBe(
            $"Target:RoleProjects:{role} is not a role a test acts as; use one of Editor, Admin, Secrets, Owner, Contributor, Reader.");
    }

    [Theory]
    [InlineData("acme")]
    [InlineData("acme/shipping/default/extra")]
    [InlineData("acme/Shipping")]
    [InlineData("acme//default")]
    [InlineData("")]
    public void Validate_RefusesARoleProjectsEntry_ThatIsNotAPathOfPlatformNames(string project)
    {
        var target = Valid();
        target.RoleProjects = new Dictionary<string, string> { ["Reader"] = project };
        var errors = new List<string>();

        target.Validate(errors);

        errors.ShouldHaveSingleItem().ShouldBe(
            $"Target:RoleProjects:Reader '{project}' is not <organization>/<project>[/<view>] of Platform names: 2 to 63 lower-case letters, digits and hyphens each.");
    }

    [Fact]
    public void RoleProjectOf_IsNull_ForAny_AndForARoleWithoutAnEntry()
    {
        var target = Valid();
        target.RoleProjects = new Dictionary<string, string> { ["Any"] = "acme/shipping", ["reader"] = "northwind/portal" };

        target.RoleProjectOf(Role.Any).ShouldBeNull();
        target.RoleProjectOf(Role.Contributor).ShouldBeNull();
        target.RoleProjectOf(Role.Reader).ShouldBe("northwind/portal");
    }

    [Fact]
    public void RoleProjects_SetToNull_IsEmpty()
    {
        var target = new PlatformTargetSettings { RoleProjects = null! };

        target.RoleProjects.ShouldBeEmpty();
        target.RoleProjects["Reader"] = "acme/shipping";
        target.RoleProjectOf(Role.Reader).ShouldBe("acme/shipping");
    }

    private static PlatformTargetSettings Valid()
    {
        return new PlatformTargetSettings { Organization = "acme", Project = "billing", View = "default" };
    }

    private static UiAutomationSettings Load(string json)
    {
        var baseDirectory = MockUnixSupport.Path(@"C:\suite\bin");
        var files = new MockFileSystem();
        files.AddFile(files.Path.Combine(baseDirectory, "testsettings.json"), new MockFileData(json));
        return UiAutomationSettingsLoader.Load(new UiAutomationSettingsOptions
        {
            BaseDirectory = baseDirectory,
            FileSystem = files,
            EnvironmentVariables = new Dictionary<string, string?>(),
        });
    }
}

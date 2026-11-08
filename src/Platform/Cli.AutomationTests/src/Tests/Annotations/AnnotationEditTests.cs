using _42.Platform.Cli.AutomationTests.Running;
using _42.Testing.UI.Automation.Runtime.TUnit;

namespace _42.Platform.Cli.AutomationTests.Tests.Annotations;

// Get and Set: sform story set and sform story get of single annotations.
[Area(Area.Annotations)]
public sealed class AnnotationEditTests : CliTest
{
    // ANN-SET-01: story set creates an annotation, and story get returns it.
    // As an Editor, story set <rst> in the project prints the new annotation, and story get <rst> returns it with its key,
    // its type (Responsibility) and its name (the part after rst.), in the project and view of the run.
    [Test]
    [Elementary]
    [UserRole(Role.Editor)]
    [Covers("story set", "arg0")]
    [Covers("story get", "arg0")]
    public async Task Story_SetGet_RoundTrip()
    {
        var key = CliData.ResponsibilityKey();

        var set = (await Sform(InProject("story", "set", key))).ShouldSucceed();
        await Assert.That(set.JsonNode()["AnnotationKey"]!.GetValue<string>()).IsEqualTo(key);

        var annotation = (await Sform(InProject("story", "get", key))).ShouldSucceed().JsonNode();
        await Assert.That(annotation["AnnotationKey"]!.GetValue<string>()).IsEqualTo(key);
        await Assert.That(annotation["AnnotationType"]?.GetValue<string>()).IsEqualTo("Responsibility");
        await Assert.That(annotation["Name"]!.GetValue<string>()).IsEqualTo(key["rst.".Length..]);
        await Assert.That(annotation["ProjectName"]!.GetValue<string>()).IsEqualTo(Project);
        await Assert.That(annotation["ViewName"]!.GetValue<string>()).IsEqualTo(View);
    }

    // ANN-SET-08: A Reader's story set is refused with exit 44.
    // As a Reader of the project (Target:RoleProjects:Reader, or a Reader of Target:Project), story set <rst> ends with 44
    // and creates nothing: story get <rst> finds no annotation.
    [Test]
    [Elementary]
    [UserRole(Role.Reader)]
    [Covers("story set")]
    public async Task Story_SetAsReader_Unauthorized()
    {
        var key = CliData.ResponsibilityKey();

        (await Sform(InProject("story", "set", key))).ShouldExitWith(CliExit.Unauthorized);

        (await Sform(InProject("story", "get", key)))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain($"The annotation '{key}' has not been found.");
    }
}

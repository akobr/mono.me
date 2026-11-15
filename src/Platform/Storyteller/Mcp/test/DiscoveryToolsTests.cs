using _42.Platform.Storyteller.Mcp.UnitTests.Support;

using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Mcp.UnitTests;

// list_projects through the host, the SDK and a recording API: the settings of each project reach the assistant as the
// API returns them, so it can talk in the project's type names.
public sealed class DiscoveryToolsTests : IDisposable
{
    private const string ProjectsJson =
        """
        [
          {
            "Key": "house.main",
            "Role": "Administrator",
            "Description": "The CRM of the house.",
            "Settings": {
              "Types": {
                "rst": { "Label": "Solution", "Plural": "Solutions", "Color": "pink", "Icon": "i-lucide-building-2" },
                "exe": { "Label": "Job" }
              },
              "DefaultTimeZone": "Europe/Prague",
              "DateFormat": "dd.mm.yyyy",
              "Version": 3,
              "Modified": { "At": "2026-11-15T08:00:00+00:00", "By": { "Kind": "Account", "Id": "user-1", "Name": "Ada" } }
            }
          },
          {
            "Key": "house.lab",
            "Role": "Reader",
            "Settings": { "Types": {}, "Version": 0 }
          }
        ]
        """;

    private readonly McpTestHost _host = new();

    [Fact]
    public async Task ListProjects_ReturnsTheSettingsOfEachProject()
    {
        _host.Api.Respond = _ => RecordingApiHandler.Json(ProjectsJson);
        await using var client = await _host.ConnectAsync();

        var result = await client.CallAsync("list_projects");

        result.IsError.ShouldNotBe(true, result.Text());
        var request = _host.Api.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Get);
        request.Uri.ToString().ShouldBe("https://api.test/api/v1/access/projects");

        var items = result.Structured()["Items"].ShouldBeOfType<JArray>();
        items.Count.ShouldBe(2);

        var main = (JObject)items[0];
        main.Value<string>("Key").ShouldBe("house.main");
        main.Value<string>("Role").ShouldBe("Administrator");
        var settings = main["Settings"].ShouldBeOfType<JObject>();
        JToken.DeepEquals(
                settings["Types"]!["rst"],
                JObject.Parse("""{ "Label": "Solution", "Plural": "Solutions", "Color": "pink", "Icon": "i-lucide-building-2" }"""))
            .ShouldBeTrue(settings.ToString());
        JToken.DeepEquals(settings["Types"]!["exe"], JObject.Parse("""{ "Label": "Job" }""")).ShouldBeTrue(settings.ToString());
        settings.Value<string>("DefaultTimeZone").ShouldBe("Europe/Prague");
        settings.Value<string>("DateFormat").ShouldBe("dd.mm.yyyy");
        settings.Value<long>("Version").ShouldBe(3);
        settings["Modified"]!["By"]!.Value<string>("Name").ShouldBe("Ada");

        // never set: no types, no time zone (UTC) and no date format (yyyy-mm-dd)
        var lab = (JObject)items[1];
        lab.Value<string>("Key").ShouldBe("house.lab");
        var defaults = lab["Settings"].ShouldBeOfType<JObject>();
        defaults["Types"].ShouldBeOfType<JObject>().Count.ShouldBe(0);
        defaults["DefaultTimeZone"].ShouldBeNull();
        defaults["DateFormat"].ShouldBeNull();
        defaults["Modified"].ShouldBeNull();
        defaults.Value<long>("Version").ShouldBe(0);

        result.Text().ShouldContain("\"Label\":\"Solution\"");
    }

    [Fact]
    public async Task ListProjects_TellsTheAssistantToTalkInTheTypeNames()
    {
        await using var client = await _host.ConnectAsync();

        var tool = (await client.ListToolsAsync()).Single(tool => tool.Name == "list_projects");

        tool.Description.ShouldNotBeNull();
        tool.Description.ShouldContain("Settings");
        tool.Description.ShouldContain("DefaultTimeZone");
        tool.Description.ShouldContain("DateFormat");
        tool.Description.ShouldContain("type codes");
        tool.ProtocolTool.Annotations!.ReadOnlyHint.ShouldBe(true);
        client.ServerInstructions.ShouldNotBeNull().ShouldContain("Settings.Types of list_projects");
    }

    public void Dispose()
    {
        _host.Dispose();
    }
}

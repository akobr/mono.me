using System.Text.Json.Nodes;
using _42.Testing.UI.Automation.Runtime.Access;

namespace _42.Testing.UI.Automation.Runtime.UnitTests.Access;

public sealed class StorageStateMergeTests
{
    private const string App = "https://admin.example.test";

    private const string Cached = """
        {
          "cookies": [ { "name": "wos-session", "value": "R1", "domain": "auth.example.test", "path": "/" } ],
          "origins": [
            {
              "origin": "https://admin.example.test",
              "localStorage": [
                { "name": "workos:refresh-token:client", "value": "R1" },
                { "name": "2s-admin:preferences", "value": "{\"pinned\":false}" }
              ]
            }
          ]
        }
        """;

    [Fact]
    public void Merge_TakesTheCookiesAndTheSignInItemsOfTheContext_AndKeepsTheOtherItemsOfTheCache()
    {
        const string current = """
            {
              "cookies": [ { "name": "wos-session", "value": "R2", "domain": "auth.example.test", "path": "/" } ],
              "origins": [
                {
                  "origin": "https://admin.example.test",
                  "localStorage": [
                    { "name": "2s-admin:preferences", "value": "{\"pinned\":true}" },
                    { "name": "workos:refresh-token:client", "value": "R2" },
                    { "name": "2s-admin:project-settings", "value": "{}" }
                  ]
                },
                { "origin": "https://auth.example.test", "localStorage": [ { "name": "workos:user", "value": "ada" } ] }
              ]
            }
            """;

        var merged = JsonNode.Parse(StorageStateMerge.Merge(Cached, current, IsSignInItem).ShouldNotBeNull())!.AsObject();

        merged["cookies"]!.AsArray().ShouldHaveSingleItem()!["value"]!.GetValue<string>().ShouldBe("R2");
        var origins = merged["origins"]!.AsArray();
        Items(origins, App).ShouldBe(
        [
            ("2s-admin:preferences", "{\"pinned\":false}"),
            ("workos:refresh-token:client", "R2"),
        ]);
        Items(origins, "https://auth.example.test").ShouldBe([("workos:user", "ada")]);
    }

    [Fact]
    public void Merge_IsNull_WhenTheContextLostEverySignInItem()
    {
        const string current = """
            {
              "cookies": [],
              "origins": [ { "origin": "https://admin.example.test", "localStorage": [ { "name": "2s-admin:preferences", "value": "{}" } ] } ]
            }
            """;

        StorageStateMerge.Merge(Cached, current, IsSignInItem).ShouldBeNull();
    }

    [Fact]
    public void Merge_WithoutSignInItems_CarriesOnlyTheCookies()
    {
        const string current = """
            {
              "cookies": [ { "name": "session", "value": "S2", "domain": "admin.example.test", "path": "/" } ],
              "origins": [ { "origin": "https://admin.example.test", "localStorage": [ { "name": "theme", "value": "dark" } ] } ]
            }
            """;
        const string cached = """
            {
              "cookies": [ { "name": "session", "value": "S1", "domain": "admin.example.test", "path": "/" } ],
              "origins": [ { "origin": "https://admin.example.test", "localStorage": [ { "name": "theme", "value": "light" } ] } ]
            }
            """;

        var merged = JsonNode.Parse(StorageStateMerge.Merge(cached, current, static (_, _) => false).ShouldNotBeNull())!.AsObject();

        merged["cookies"]!.AsArray().ShouldHaveSingleItem()!["value"]!.GetValue<string>().ShouldBe("S2");
        Items(merged["origins"]!.AsArray(), App).ShouldBe([("theme", "light")]);
    }

    private static bool IsSignInItem(string origin, string name)
    {
        return name.StartsWith("workos:", StringComparison.Ordinal);
    }

    private static (string Name, string Value)[] Items(JsonArray origins, string origin)
    {
        return origins
            .OfType<JsonObject>()
            .Where(state => (string?)state["origin"] == origin)
            .SelectMany(state => state["localStorage"]!.AsArray().OfType<JsonObject>())
            .Select(item => (item["name"]!.GetValue<string>(), item["value"]!.GetValue<string>()))
            .ToArray();
    }
}

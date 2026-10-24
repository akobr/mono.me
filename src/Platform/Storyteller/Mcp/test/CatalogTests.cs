using System.Text.Json;
using System.Text.RegularExpressions;

using _42.Platform.Storyteller.Mcp.UnitTests.Support;

using Microsoft.Extensions.DependencyInjection;

using ModelContextProtocol.Server;

namespace _42.Platform.Storyteller.Mcp.UnitTests;

// The rules every tool of the catalog follows, checked on the tools as the server registers them.
public sealed partial class CatalogTests : IDisposable
{
    private static readonly string[] DestructiveVerbs = ["delete", "purge", "reset"];

    private readonly McpTestHost _host = new(new Dictionary<string, string?> { ["Mcp:SecretTools:Enabled"] = "true" });

    public static IEnumerable<string> ReadVerbs => ["get", "list", "find", "search", "explain", "suggest", "diff"];

    [Fact]
    public void Tools_HaveUniqueSnakeCaseNames()
    {
        var names = Tools().Select(tool => tool.ProtocolTool.Name).ToList();

        names.ShouldBeUnique();
        names.ShouldAllBe(name => SnakeCase().IsMatch(name));
    }

    [Fact]
    public void Tools_DescribeThemselvesAndEveryParameter()
    {
        foreach (var tool in Tools().Select(tool => tool.ProtocolTool))
        {
            tool.Description.ShouldNotBeNullOrWhiteSpace(tool.Name);
            tool.Title.ShouldNotBeNullOrWhiteSpace(tool.Name);

            if (tool.InputSchema.TryGetProperty("properties", out var properties))
            {
                foreach (var property in properties.EnumerateObject())
                {
                    property.Value.TryGetProperty("description", out var description).ShouldBeTrue($"{tool.Name}.{property.Name}");
                    description.GetString().ShouldNotBeNullOrWhiteSpace($"{tool.Name}.{property.Name}");
                }
            }
        }
    }

    [Fact]
    public void Tools_AreClosedWorldAndCarryExplicitHints()
    {
        foreach (var tool in Tools().Select(tool => tool.ProtocolTool))
        {
            var annotations = tool.Annotations.ShouldNotBeNull(tool.Name);
            annotations.OpenWorldHint.ShouldBe(false, tool.Name);
            annotations.ReadOnlyHint.ShouldNotBeNull(tool.Name);

            if (annotations.ReadOnlyHint is false)
            {
                annotations.DestructiveHint.ShouldNotBeNull(tool.Name);
            }
        }
    }

    [Fact]
    public void Tools_ThatDeletePurgeOrReset_AreDestructive()
    {
        foreach (var tool in Tools().Select(tool => tool.ProtocolTool).Where(tool => DestructiveVerbs.Contains(tool.Name.Split('_')[0])))
        {
            tool.Annotations!.ReadOnlyHint.ShouldBe(false, tool.Name);
            tool.Annotations.DestructiveHint.ShouldBe(true, tool.Name);
        }
    }

    [Fact]
    public void Tools_ThatRead_AreReadOnly_ExceptTheSecretTools()
    {
        var secretTools = new[] { "reveal_key_value", "get_resolved_configuration", "check_configuration_bindings" };

        foreach (var tool in Tools().Select(tool => tool.ProtocolTool).Where(tool => ReadVerbs.Contains(tool.Name.Split('_')[0])))
        {
            tool.Annotations!.ReadOnlyHint.ShouldBe(!secretTools.Contains(tool.Name), tool.Name);
        }
    }

    [Fact]
    public void Tools_OrganizationAndProjectParameters_AreRequired()
    {
        foreach (var tool in Tools().Select(tool => tool.ProtocolTool))
        {
            if (!tool.InputSchema.TryGetProperty("properties", out var properties))
            {
                continue;
            }

            var required = tool.InputSchema.TryGetProperty("required", out var list)
                ? list.EnumerateArray().Select(item => item.GetString()).ToHashSet()
                : [];

            foreach (var name in new[] { "organization", "project" })
            {
                if (properties.TryGetProperty(name, out _))
                {
                    required.ShouldContain(name, $"{tool.Name}.{name}");
                }
            }

            if (properties.TryGetProperty("view", out var view))
            {
                view.TryGetProperty("default", out var defaultView).ShouldBeTrue($"{tool.Name}.view");
                defaultView.ValueKind.ShouldBe(JsonValueKind.String);
            }
        }
    }

    public void Dispose()
    {
        _host.Dispose();
    }

    [GeneratedRegex("^[a-z]+(_[a-z]+)*$")]
    private static partial Regex SnakeCase();

    private List<McpServerTool> Tools()
    {
        _host.CreateClient().Dispose();
        return _host.Services.GetServices<McpServerTool>().ToList();
    }
}

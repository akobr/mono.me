using System.Text.Json;
using System.Text.RegularExpressions;

using _42.Platform.Storyteller.Api.ErrorReporting;
using _42.Platform.Storyteller.Api.V1;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// The Sentry transaction of a function: its route template, never the URL with organization and project names.
public class FunctionTransactionNamesTests
{
    [Fact]
    public void All_EveryHttpFunction_HasATemplateWithoutConstraints()
    {
        var http = FunctionTransactionNames.All.Where(function => function.Value.Kind == "http").ToList();

        http.Count.ShouldBeGreaterThan(100);

        foreach (var (name, trigger) in http)
        {
            var transaction = FunctionTransactionNames.Get(name, method: null);
            transaction.ShouldMatch(@"^[A-Z|]+ /api/", $"{name}: {transaction}");

            // a parameter is {name} or {*name}: no constraint, default or optional marker
            Regex.Matches(trigger.Template!, @"\{[^}]*\}")
                .ShouldAllBe(parameter => Regex.IsMatch(parameter.Value, @"^\{\*?[A-Za-z][A-Za-z0-9]*\}$"), $"{name}: {trigger.Template}");
        }
    }

    [Theory]
    [InlineData(nameof(KeyValuesHttp.SetKeyValue), "put", "PUT /api/v1/{organization}/{project}/{view}/key-values/{key}")]
    [InlineData(nameof(KeyValuesHttp.SetKeyValue), null, "PUT /api/v1/{organization}/{project}/{view}/key-values/{key}")]
    [InlineData(nameof(KeyValuesHttp.GetKeyValue), "GET", "GET /api/v1/{organization}/{project}/{view}/key-values/{key}")]
    [InlineData(nameof(AnnotationsHttp.SetAnnotation), "POST", "POST /api/v1/{organization}/{project}/{view}/annotations/{key}")]
    [InlineData("ProcessViewTransfer", null, "queue ProcessViewTransfer")]
    [InlineData("KeyValueProtectionTimer", null, "timer KeyValueProtectionTimer")]
    [InlineData("RenderSwaggerDocument", "GET", "RenderSwaggerDocument")]
    public void Get_AFunction_IsNamedByItsTemplate(string function, string? method, string expected)
    {
        FunctionTransactionNames.Get(function, method).ShouldBe(expected);
    }

    [Fact]
    public void Get_AFunctionOfSeveralMethods_WithoutTheRequest_NamesThemAll()
    {
        var several = FunctionTransactionNames.All.FirstOrDefault(function => function.Value.Methods.Count > 1);

        if (several.Key is null)
        {
            return;
        }

        FunctionTransactionNames.Get(several.Key, method: null).ShouldStartWith(string.Join('|', several.Value.Methods) + " /api/");
    }

    [Theory]
    [InlineData("v1/{organization:regex(^(?!access$))}/{project}", "v1/{organization}/{project}")]
    [InlineData("v1/{organization}/{project}/{view:regex(^(?!usage$))}/key-values", "v1/{organization}/{project}/{view}/key-values")]
    [InlineData("v1/{organization}/{project}/{view}/annotations/{key:regex(\\.)}", "v1/{organization}/{project}/{view}/annotations/{key}")]
    [InlineData("v1/files/{*path}", "v1/files/{*path}")]
    [InlineData("v1/items/{id?}", "v1/items/{id}")]
    [InlineData("v1/items/{page=1}", "v1/items/{page}")]
    public void Normalize_DropsConstraintsDefaultsAndOptionalMarkers(string route, string expected)
    {
        FunctionTransactionNames.Normalize(route).ShouldBe(expected);
    }

    // The transaction names assume the default route prefix "api": host.json must not set another.
    [Fact]
    public void HostJson_HasNoRoutePrefix()
    {
        using var host = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "host.json")));

        var hasPrefix = host.RootElement.TryGetProperty("extensions", out var extensions)
            && extensions.TryGetProperty("http", out var http)
            && http.TryGetProperty("routePrefix", out _);

        hasPrefix.ShouldBeFalse();
        FunctionTransactionNames.RoutePrefix.ShouldBe("api");
    }
}

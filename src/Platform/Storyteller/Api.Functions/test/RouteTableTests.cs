using System.Reflection;
using System.Text.RegularExpressions;

using _42.Platform.Storyteller.Api.V1;

using Microsoft.Azure.Functions.Worker;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

public partial class RouteTableTests
{
    [Fact]
    public void HttpRoutes_AreUniquePerMethodAndShape()
    {
        var routes = typeof(AccessHttp).Assembly
            .GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            .Where(method => method.GetCustomAttribute<FunctionAttribute>() is not null)
            .SelectMany(method => method.GetParameters()
                .Select(parameter => parameter.GetCustomAttribute<HttpTriggerAttribute>())
                .Where(trigger => trigger?.Route is not null)
                .SelectMany(trigger => trigger!.Methods!.Select(verb => (
                    Function: method.Name,
                    Key: $"{verb.ToUpperInvariant()} {Parameter().Replace(trigger.Route!, "{}")}"))))
            .ToList();

        routes.ShouldNotBeEmpty();
        routes.GroupBy(route => route.Key)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key}: {string.Join(", ", group.Select(route => route.Function))}")
            .ShouldBeEmpty();
    }

    // The functions read their JSON bodies themselves (RequestBodyExtensions): a FromBody binding fails an empty body,
    // a JSON null or a body without a content type with an exception before the function runs, which answers 500.
    [Fact]
    public void Functions_ReadTheirJsonBodiesThemselves_NotThroughAFromBodyBinding()
    {
        var functions = typeof(AccessHttp).Assembly
            .GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            .Where(method => method.GetCustomAttribute<FunctionAttribute>() is not null)
            .ToList();

        functions.ShouldNotBeEmpty();
        functions
            .SelectMany(method => method.GetParameters()
                .Where(parameter => parameter.IsDefined(typeof(Microsoft.Azure.Functions.Worker.Http.FromBodyAttribute), false)
                    || parameter.IsDefined(typeof(Microsoft.AspNetCore.Mvc.FromBodyAttribute), false))
                .Select(parameter => $"{method.DeclaringType!.Name}.{method.Name}({parameter.Name})"))
            .ShouldBeEmpty();
    }

    [Theory]
    [InlineData("GET v1/{}/{}/views")]
    [InlineData("POST v1/{}/{}/views")]
    [InlineData("PUT v1/{}/{}/views/{}")]
    [InlineData("GET v1/{}/{}/{}/configurations")]
    [InlineData("GET v1/{}/{}/{}/configuration-schemas")]
    [InlineData("GET v1/{}/{}/{}/templates")]
    [InlineData("GET v1/{}/{}/{}/key-values")]
    [InlineData("GET v1/{}/{}/{}/key-values/{}")]
    [InlineData("PUT v1/{}/{}/{}/key-values/{}")]
    [InlineData("DELETE v1/{}/{}/{}/key-values/{}")]
    [InlineData("POST v1/{}/{}/{}/key-values/restore")]
    [InlineData("POST v1/{}/{}/{}/key-values/purge")]
    [InlineData("POST v1/{}/{}/{}/key-values/protect")]
    [InlineData("POST v1/{}/{}/{}/key-values/export")]
    [InlineData("POST v1/{}/{}/{}/key-values/import")]
    [InlineData("GET v1/{}/{}/{}/key-values/{}/versions")]
    [InlineData("GET v1/{}/{}/{}/key-values/{}/versions/{}")]
    [InlineData("GET v1/{}/{}/{}/key-values/{}/versions/{}/diff")]
    [InlineData("GET v1/{}/{}/{}/key-values/{}/versions/{}/diff/{}")]
    [InlineData("GET v1/{}/{}/key-values/policy")]
    [InlineData("PUT v1/{}/{}/key-values/policy")]
    [InlineData("GET v1/{}/{}/key-values/policy/byok/setup")]
    [InlineData("POST v1/{}/{}/key-values/policy/byok/verify")]
    [InlineData("POST v1/{}/{}/key-values/policy/rotate")]
    [InlineData("GET v1/{}/{}/key-values/recipients")]
    [InlineData("POST v1/{}/{}/key-values/recipients")]
    [InlineData("PATCH v1/{}/{}/key-values/recipients/{}")]
    [InlineData("DELETE v1/{}/{}/key-values/recipients/{}")]
    [InlineData("GET v1/{}/{}/key-values/audit")]
    [InlineData("GET v1/{}/{}/usage/key-values")]
    [InlineData("GET v1/{}/{}/bindings")]
    public void Phase0cRoutes_AreRegistered(string expected)
    {
        var routes = typeof(AccessHttp).Assembly
            .GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            .SelectMany(method => method.GetParameters().Select(parameter => parameter.GetCustomAttribute<HttpTriggerAttribute>()))
            .Where(trigger => trigger?.Route is not null)
            .SelectMany(trigger => trigger!.Methods!.Select(verb => $"{verb.ToUpperInvariant()} {Parameter().Replace(trigger.Route!, "{}")}"));

        routes.ShouldContain(expected);
    }

    [Theory]
    [InlineData("GET v1/{}/{}/{}/annotations/deleted")]
    [InlineData("GET v1/{}/{}/{}/annotations/deleted/{}")]
    [InlineData("DELETE v1/{}/{}/{}/annotations/deleted/{}")]
    [InlineData("POST v1/{}/{}/{}/annotations/deleted/lookup")]
    [InlineData("POST v1/{}/{}/{}/annotations/deleted/restore")]
    [InlineData("GET v1/{}/{}/{}/annotations/{}/history")]
    [InlineData("DELETE v1/{}/{}/{}/annotations/{}")]
    public void DeletedAnnotationRoutes_AreRegistered(string expected)
    {
        var routes = typeof(AccessHttp).Assembly
            .GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            .SelectMany(method => method.GetParameters().Select(parameter => parameter.GetCustomAttribute<HttpTriggerAttribute>()))
            .Where(trigger => trigger?.Route is not null)
            .SelectMany(trigger => trigger!.Methods!.Select(verb => $"{verb.ToUpperInvariant()} {Parameter().Replace(trigger.Route!, "{}")}"));

        routes.ShouldContain(expected);
    }

    [Theory]
    [InlineData("GET v1/access/points/{}/machine-authentication")]
    [InlineData("PUT v1/access/points/{}/machine-authentication")]
    public void MachineAuthenticationRoutes_AreRegistered(string expected)
    {
        var routes = typeof(AccessHttp).Assembly
            .GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            .SelectMany(method => method.GetParameters().Select(parameter => parameter.GetCustomAttribute<HttpTriggerAttribute>()))
            .Where(trigger => trigger?.Route is not null)
            .SelectMany(trigger => trigger!.Methods!.Select(verb => $"{verb.ToUpperInvariant()} {Parameter().Replace(trigger.Route!, "{}")}"));

        routes.ShouldContain(expected);
    }

    [Theory]
    [InlineData("GET v1/{}/{}/access/issuers")]
    [InlineData("GET v1/{}/{}/access/issuers/{}")]
    [InlineData("PUT v1/{}/{}/access/issuers/{}")]
    [InlineData("DELETE v1/{}/{}/access/issuers/{}")]
    [InlineData("PUT v1/{}/{}/access/machines/{}/federation-rules")]
    [InlineData("POST v1/{}/{}/access/token")]
    public void FederationRoutes_AreRegistered(string expected)
    {
        var routes = typeof(AccessHttp).Assembly
            .GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            .SelectMany(method => method.GetParameters().Select(parameter => parameter.GetCustomAttribute<HttpTriggerAttribute>()))
            .Where(trigger => trigger?.Route is not null)
            .SelectMany(trigger => trigger!.Methods!.Select(verb => $"{verb.ToUpperInvariant()} {Parameter().Replace(trigger.Route!, "{}")}"));

        routes.ShouldContain(expected);
    }

    // The token endpoint is anonymous: it authorizes nothing itself and publishes no security scheme.
    [Fact]
    public void TokenEndpoint_IsAnonymousAndPublishesNoSecurity()
    {
        var method = typeof(FederationHttp).GetMethod(nameof(FederationHttp.PostMachineToken))!;

        method.GetParameters().Select(parameter => parameter.GetCustomAttribute<HttpTriggerAttribute>()).OfType<HttpTriggerAttribute>()
            .ShouldHaveSingleItem().AuthLevel.ShouldBe(AuthorizationLevel.Anonymous);
        method.GetCustomAttributes<Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes.OpenApiSecurityAttribute>().ShouldBeEmpty();
    }

    // The literal segments of the new routes can never be an annotation key, so they never shadow a key route.
    [Theory]
    [InlineData("deleted")]
    [InlineData("lookup")]
    [InlineData("restore")]
    [InlineData("history")]
    public void LiteralSegments_AreNotAnnotationKeys(string segment)
    {
        AnnotationKey.TryParse(segment, out _).ShouldBeFalse();
    }

    // A key named like a collection action ("restore", "export", ...) still reaches the key functions: the actions
    // are POST only, and no route with a {key} segment accepts POST, so the method decides between the two shapes.
    [Fact]
    public void KeyValueActions_ArePostOnly_AndKeyRoutesNeverPost()
    {
        var triggers = typeof(KeyValuesHttp)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .SelectMany(method => method.GetParameters().Select(parameter => parameter.GetCustomAttribute<HttpTriggerAttribute>()))
            .Where(trigger => trigger?.Route is not null)
            .Select(trigger => trigger!)
            .ToList();

        var keyRoutes = triggers.Where(trigger => trigger.Route!.Contains("/key-values/{key}", StringComparison.Ordinal)).ToList();
        var actionRoutes = triggers.Where(trigger => !trigger.Route!.Contains("{key}", StringComparison.Ordinal) && trigger.Route!.Length > Definitions.Routes.KeyValues.V1.KeyValues.Length).ToList();

        keyRoutes.ShouldNotBeEmpty();
        keyRoutes.SelectMany(trigger => trigger.Methods!).ShouldAllBe(method => !string.Equals(method, "post", StringComparison.OrdinalIgnoreCase));
        actionRoutes.Select(trigger => trigger.Route).ShouldBe(
            [
                Definitions.Routes.KeyValues.V1.Restore,
                Definitions.Routes.KeyValues.V1.Purge,
                Definitions.Routes.KeyValues.V1.Protect,
                Definitions.Routes.KeyValues.V1.Export,
                Definitions.Routes.KeyValues.V1.Import,
            ],
            ignoreOrder: true);
        actionRoutes.SelectMany(trigger => trigger.Methods!).ShouldAllBe(method => string.Equals(method, "post", StringComparison.OrdinalIgnoreCase));
    }

    // Literal segments after the view: "views" is a reserved view name, so they never meet the view routes.
    [Theory]
    [InlineData("POST v1/{}/{}/{}/exports")]
    [InlineData("GET v1/{}/{}/{}/exports/{}")]
    [InlineData("GET v1/{}/{}/{}/exports/{}/archive")]
    [InlineData("POST v1/{}/{}/{}/imports")]
    [InlineData("GET v1/{}/{}/{}/imports/{}")]
    public void ViewTransferRoutes_AreRegistered(string expected)
    {
        var routes = typeof(AccessHttp).Assembly
            .GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            .SelectMany(method => method.GetParameters().Select(parameter => parameter.GetCustomAttribute<HttpTriggerAttribute>()))
            .Where(trigger => trigger?.Route is not null)
            .SelectMany(trigger => trigger!.Methods!.Select(verb => $"{verb.ToUpperInvariant()} {Parameter().Replace(trigger.Route!, "{}")}"));

        routes.ShouldContain(expected);
    }

    [GeneratedRegex("{[^}]+}")]
    private static partial Regex Parameter();
}

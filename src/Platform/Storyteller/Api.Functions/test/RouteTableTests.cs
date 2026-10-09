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

    [Theory]
    [InlineData("GET v1/{}/{}/views")]
    [InlineData("POST v1/{}/{}/views")]
    [InlineData("PUT v1/{}/{}/views/{}")]
    [InlineData("GET v1/{}/{}/{}/configurations")]
    [InlineData("GET v1/{}/{}/{}/configuration-schemas")]
    [InlineData("GET v1/{}/{}/{}/templates")]
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

    [GeneratedRegex("{[^}]+}")]
    private static partial Regex Parameter();
}

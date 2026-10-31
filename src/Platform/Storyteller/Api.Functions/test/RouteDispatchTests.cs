using System.Reflection;

using _42.Platform.Storyteller.Api.V1;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Template;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// The dispatch of the Functions host, modelled: it parses each route as an ASP.NET Core route template, resolves the
// inline constraints with DefaultInlineConstraintResolver, and runs the first function, in the order of the function
// names, whose template, constraints and method match. It doesn't rank a literal segment above a parameter
// (azure-functions-host#9876), so "SetAnnotation" took POST .../annotations/simple from "SetAnnotationsSimple". A request
// that matches exactly one function goes there whatever the order is, which is what these tests require.
public class RouteDispatchTests
{
    private const string Scope = "v1/acme/billing/default";
    private const string Annotations = Scope + "/annotations";

    // The resolver of the host: AddRouting maps "regex" to the real constraint, a bare RouteOptions to a stub that throws.
    private static readonly IInlineConstraintResolver Resolver = new ServiceCollection()
        .AddRouting()
        .BuildServiceProvider()
        .GetRequiredService<IInlineConstraintResolver>();

    private static readonly IReadOnlyList<FunctionRoute> Functions = ReadFunctions();

    [Theory]
    [InlineData("POST", Annotations + "/simple", nameof(AnnotationsHttp.SetAnnotationsSimple))]
    [InlineData("POST", Annotations, nameof(AnnotationsHttp.SetAnnotations))]
    [InlineData("GET", Annotations, nameof(AnnotationsHttp.GetAnnotations))]
    [InlineData("POST", Annotations + "/rst.billing", nameof(AnnotationsHttp.SetAnnotation))]
    [InlineData("PUT", Annotations + "/rst.billing", nameof(AnnotationsHttp.SetAnnotation))]
    [InlineData("GET", Annotations + "/rst.billing", nameof(AnnotationsHttp.GetAnnotation))]
    [InlineData("DELETE", Annotations + "/rst.billing", nameof(AnnotationsHttp.DeleteAnnotation))]
    [InlineData("GET", Annotations + "/deleted", nameof(AnnotationsHttp.GetDeletedAnnotations))]
    [InlineData("GET", Annotations + "/deleted/rst.billing", nameof(AnnotationsHttp.GetDeletedAnnotation))]
    [InlineData("DELETE", Annotations + "/deleted/rst.billing", nameof(AnnotationsHttp.PurgeAnnotation))]
    [InlineData("POST", Annotations + "/deleted/lookup", nameof(AnnotationsHttp.FindDeletedAnnotations))]
    [InlineData("POST", Annotations + "/deleted/restore", nameof(AnnotationsHttp.RestoreAnnotations))]
    [InlineData("GET", Annotations + "/rst.billing/history", nameof(AnnotationsHttp.GetAnnotationHistory))]
    [InlineData("GET", Annotations + "/sbt.northwind/usages", nameof(AnnotationsHttp.GetDescendants))]
    [InlineData("GET", Annotations + "/usg.northwind.billing/all", nameof(AnnotationsHttp.GetDescendants))]
    [InlineData("GET", Annotations + "/exe.northwind.billing.prod/units-of-execution", nameof(AnnotationsHttp.GetDescendants))]
    [InlineData("GET", Annotations + "/uxe.a.b.c.d", nameof(AnnotationsHttp.GetAnnotation))]
    [InlineData("GET", Annotations + "/SBT.Northwind", nameof(AnnotationsHttp.GetAnnotation))]
    [InlineData("GET", Annotations + "/.sbt.northwind.", nameof(AnnotationsHttp.GetAnnotation))]
    [InlineData("GET", Annotations + "/sbt..northwind/history", nameof(AnnotationsHttp.GetAnnotationHistory))]
    [InlineData("POST", Annotations + "/sbt.simple", nameof(AnnotationsHttp.SetAnnotation))]
    [InlineData("GET", Annotations + "/sbt.deleted", nameof(AnnotationsHttp.GetAnnotation))]
    [InlineData("GET", Annotations + "/sbt.deleted/usages", nameof(AnnotationsHttp.GetDescendants))]
    [InlineData("GET", Annotations + "/rst.history/history", nameof(AnnotationsHttp.GetAnnotationHistory))]
    [InlineData("GET", Annotations + "/deleted/sbt.deleted", nameof(AnnotationsHttp.GetDeletedAnnotation))]
    [InlineData("GET", "v1/acme/billing/usage/key-values", nameof(KeyValueUsageHttp.GetKeyValueUsage))]
    [InlineData("GET", Scope + "/key-values", nameof(KeyValuesHttp.GetKeyValues))]
    [InlineData("GET", Scope + "/key-values/db-password", nameof(KeyValuesHttp.GetKeyValue))]
    [InlineData("POST", Scope + "/key-values/restore", nameof(KeyValuesHttp.RestoreKeyValues))]
    public void Request_MatchesExactlyItsFunction(string method, string path, string function)
    {
        Match(method, path).ShouldBe([function]);
    }

    // A key without a dot and an unknown kind of descendants: no route takes them, so the host answers 404.
    [Theory]
    [InlineData("GET", Annotations + "/simple")]
    [InlineData("PUT", Annotations + "/simple")]
    [InlineData("DELETE", Annotations + "/deleted")]
    [InlineData("GET", Annotations + "/nope")]
    [InlineData("GET", Annotations + "/sbt.northwind/bogus")]
    [InlineData("GET", Annotations + "/sbt.northwind/subjects")]
    public void Request_MatchesNoFunction(string method, string path)
    {
        Match(method, path).ShouldBeEmpty();
    }

    // Every route with a sample value in each parameter is matched by its own function only, so a new literal route
    // that a parameter of the same method would take fails here.
    [Fact]
    public void EveryRoute_WithSampleValues_MatchesOnlyItsFunction()
    {
        Functions.ShouldNotBeEmpty();
        Functions
            .SelectMany(function => function.Methods.Select(method => (Function: function, Method: method, Path: function.SamplePath())))
            .Select(request => (request.Method, request.Path, Expected: request.Function.Name, Matched: Match(request.Method, request.Path)))
            .Where(request => request.Matched.Count != 1 || request.Matched[0] != request.Expected)
            .Select(request => $"{request.Method} {request.Path}: {request.Expected} expected, matched {string.Join(", ", request.Matched)}")
            .ShouldBeEmpty();
    }

    // The constraint and GetDescendants accept the same values; the literal "history" of the sibling route is not one.
    [Theory]
    [InlineData("usages", true)]
    [InlineData("contexts", true)]
    [InlineData("executions", true)]
    [InlineData("units", true)]
    [InlineData("units-of-execution", true)]
    [InlineData("all", true)]
    [InlineData("history", false)]
    [InlineData("deleted", false)]
    [InlineData("subjects", false)]
    [InlineData("all-units", false)]
    public void DescendantsConstraint_TakesTheValuesOfGetDescendants(string value, bool accepted)
    {
        Accepts(Definitions.Routes.Constraints.Descendants, Definitions.Parameters.Descendants, value).ShouldBe(accepted);
    }

    [Theory]
    [InlineData("rst.billing", true)]
    [InlineData("uxe.a.b.c.d", true)]
    [InlineData("simple", false)]
    [InlineData("deleted", false)]
    [InlineData("history", false)]
    public void AnnotationKeyConstraint_TakesADottedKeyOnly(string value, bool accepted)
    {
        Accepts(Definitions.Routes.Constraints.AnnotationKey, Definitions.Parameters.Key, value).ShouldBe(accepted);
    }

    [Theory]
    [InlineData("default", true)]
    [InlineData("usages", true)]
    [InlineData("usage", false)]
    public void KeyValuesViewConstraint_RefusesTheUsageLiteral(string value, bool accepted)
    {
        Accepts(Definitions.Routes.Constraints.KeyValuesView, Definitions.Parameters.View, value).ShouldBe(accepted);
    }

    private static bool Accepts(string constraint, string parameter, string value)
    {
        var resolved = Resolver.ResolveConstraint(constraint.TrimStart(':')).ShouldNotBeNull();
        return resolved.Match(null, null, parameter, new RouteValueDictionary { [parameter] = value }, RouteDirection.IncomingRequest);
    }

    private static List<string> Match(string method, string path)
    {
        return Functions.Where(function => function.Matches(method, path)).Select(function => function.Name).ToList();
    }

    private static List<FunctionRoute> ReadFunctions()
    {
        return typeof(AccessHttp).Assembly
            .GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            .Select(method => (Function: method.GetCustomAttribute<FunctionAttribute>(), Trigger: method.GetParameters()
                .Select(parameter => parameter.GetCustomAttribute<HttpTriggerAttribute>())
                .SingleOrDefault(trigger => trigger?.Route is not null)))
            .Where(function => function.Function is not null && function.Trigger is not null)
            .Select(function => new FunctionRoute(function.Function!.Name, function.Trigger!.Methods!, function.Trigger.Route!))
            .ToList();
    }

    private sealed class FunctionRoute
    {
        private readonly RouteTemplate _template;
        private readonly TemplateMatcher _matcher;
        private readonly IReadOnlyList<(string Parameter, IRouteConstraint Constraint)> _constraints;

        public FunctionRoute(string name, IReadOnlyList<string> methods, string route)
        {
            Name = name;
            Methods = methods.Select(method => method.ToUpperInvariant()).ToList();
            _template = TemplateParser.Parse(route);
            _matcher = new TemplateMatcher(_template, new RouteValueDictionary());

            // an inline constraint that doesn't resolve fails the route, as it fails the host
            _constraints = _template.Parameters
                .SelectMany(parameter => parameter.InlineConstraints.Select(inline => (
                    Parameter: parameter.Name!,
                    Constraint: Resolver.ResolveConstraint(inline.Constraint)
                        ?? throw new InvalidOperationException($"The constraint '{inline.Constraint}' of {name} ({route}) doesn't resolve."))))
                .ToList();
        }

        public string Name { get; }

        public IReadOnlyList<string> Methods { get; }

        public bool Matches(string method, string path)
        {
            var values = new RouteValueDictionary();

            return Methods.Contains(method.ToUpperInvariant())
                && _matcher.TryMatch(new PathString($"/{path}"), values)
                && _constraints.All(constraint => constraint.Constraint.Match(null, null, constraint.Parameter, values, RouteDirection.IncomingRequest));
        }

        public string SamplePath()
        {
            return string.Join('/', _template.Segments.Select(segment => string.Concat(segment.Parts.Select(part => part.IsParameter ? Sample(part.Name!) : part.Text))));
        }

        private static string Sample(string parameter) => parameter switch
        {
            Definitions.Parameters.Key => "sbt.sample",
            Definitions.Parameters.Descendants => "usages",
            _ => $"sample-{parameter.ToLowerInvariant()}",
        };
    }
}

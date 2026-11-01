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

    // In the order of the host: by function name, ignoring case.
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
    [InlineData("GET", "v1/access/points/views", nameof(AccessHttp.GetAccessPoint))]
    [InlineData("GET", "v1/access/points/bindings", nameof(AccessHttp.GetAccessPoint))]
    [InlineData("PUT", "v1/access/points/views/machine-authentication", nameof(AccessHttp.PutMachineAuthentication))]
    [InlineData("GET", "v1/acme/billing/views", nameof(ViewsHttp.GetViews))]
    [InlineData("GET", Scope + "/configuration-schema/type/rst", nameof(ConfigurationSchemaHttp.GetConfigurationSchema))]
    [InlineData("GET", Scope + "/configuration-schema/rst.billing", nameof(ConfigurationSchemaHttp.GetAnnotationSchema))]
    [InlineData("GET", Scope + "/configuration-schema/rst.billing/definition", nameof(ConfigurationSchemaHttp.GetCombinedConfigurationSchema))]
    [InlineData("GET", Scope + "/configuration-schema/type/rst/versions", nameof(ConfigurationSchemaHttp.GetSchemaVersions))]
    [InlineData("GET", Scope + "/configuration-schema/sbt.northwind/type/usg", nameof(ConfigurationSchemaHttp.GetDescendantTypeSchema))]
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

    // Two routes of one method never take the same request, also crosswise: a request with the literals of both routes,
    // and a sample value where both have a parameter (GET v1/access/points/views is the access point of an organization
    // "views", not the views of the organization "access").
    [Fact]
    public void NoTwoRoutesOfOneMethod_TakeTheSameRequest()
    {
        Functions
            .SelectMany((first, index) => Functions.Skip(index + 1).Select(second => (First: first, Second: second)))
            .SelectMany(pair => pair.First.Methods.Intersect(pair.Second.Methods)
                .SelectMany(method => pair.First.CrosswisePaths(pair.Second).Select(path => (pair.First, pair.Second, Method: method, Path: path))))
            .Where(request => request.First.Matches(request.Method, request.Path) && request.Second.Matches(request.Method, request.Path))
            .Select(request => $"{request.Method} {request.First.Name} {request.Second.Name}")
            .Distinct()
            .ShouldBeEmpty();
    }

    // The alternation of the constraint is the list of GetDescendants.
    [Fact]
    public void DescendantsConstraint_ListsTheKeysOfDescendantTypes()
    {
        var alternation = Definitions.Routes.Constraints.Descendants.Split('(', ')')[2].Split('|');

        alternation.ShouldBe(AnnotationsHttp.DescendantTypes.Keys, ignoreOrder: true);
    }

    // The routes have only simple segments, the only ones CrosswisePaths and SamplePath compare: no optional, catch-all
    // or multi-part parameter.
    [Fact]
    public void Routes_HaveOnlySimpleSegments()
    {
        Functions.Where(function => !function.HasOnlySimpleSegments).Select(function => function.Name).ShouldBeEmpty();
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
            .OrderBy(function => function.Name, StringComparer.OrdinalIgnoreCase)
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

        public bool HasOnlySimpleSegments => _template.Segments.All(segment => segment.Parts.Count == 1
            && (!segment.Parts[0].IsParameter || (!segment.Parts[0].IsOptional && !segment.Parts[0].IsCatchAll)));

        public IReadOnlyList<string> Methods { get; }

        public bool Matches(string method, string path)
        {
            var values = new RouteValueDictionary();

            return Methods.Contains(method.ToUpperInvariant())
                && _matcher.TryMatch(new PathString($"/{path}"), values)
                && _constraints.All(constraint => constraint.Constraint.Match(null, null, constraint.Parameter, values, RouteDirection.IncomingRequest));
        }

        // The requests that have the literals of both routes, and a sample of either route where both have a parameter;
        // none when a literal differs or a segment has more than one part.
        public IEnumerable<string> CrosswisePaths(FunctionRoute other)
        {
            if (_template.Segments.Count != other._template.Segments.Count)
            {
                return [];
            }

            var pairs = _template.Segments.Zip(other._template.Segments, (mine, theirs) => (Mine: Single(mine), Theirs: Single(theirs))).ToList();

            if (pairs.Any(pair => pair.Mine is null || pair.Theirs is null
                || (!pair.Mine.IsParameter && !pair.Theirs.IsParameter && !string.Equals(pair.Mine.Text, pair.Theirs.Text, StringComparison.OrdinalIgnoreCase))))
            {
                return [];
            }

            return new[] { true, false }
                .Select(useMine => string.Join('/', pairs.Select(pair => (pair.Mine!, pair.Theirs!) switch
                {
                    ({ IsParameter: false } mine, _) => mine.Text,
                    (_, { IsParameter: false } theirs) => theirs.Text,
                    var (mine, theirs) => Sample(useMine ? mine.Name! : theirs.Name!),
                })))
                .Distinct();

            static TemplatePart? Single(TemplateSegment segment) => segment.Parts.Count == 1 ? segment.Parts[0] : null;
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

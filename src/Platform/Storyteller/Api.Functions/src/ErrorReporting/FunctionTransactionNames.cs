using System.Collections.Frozen;
using System.Reflection;

using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Azure.Functions.Worker;

namespace _42.Platform.Storyteller.Api.ErrorReporting;

// The Sentry transaction of a function: the route template of an HTTP function, never its URL with organization and
// project names, so events group by route. Read once from the [Function] methods of the API, the way the host reads them.
internal static class FunctionTransactionNames
{
    // host.json sets no extensions.http.routePrefix, so every route is under the default prefix (a test checks it).
    public const string RoutePrefix = "api";

    private static readonly Lazy<FrozenDictionary<string, FunctionTrigger>> Functions =
        new(() => Read(typeof(FunctionTransactionNames).Assembly));

    // Every function of the assembly, by function name.
    internal static IReadOnlyDictionary<string, FunctionTrigger> All => Functions.Value;

    // "PUT /api/v1/{organization}/{project}/{view}/key-values/{key}", "queue ProcessViewTransfer", or the name of a function
    // of another assembly (the OpenAPI extension's). Without the request's method, the declared methods joined by '|'.
    public static string Get(string functionName, string? method)
    {
        if (!Functions.Value.TryGetValue(functionName, out var trigger))
        {
            return functionName;
        }

        if (trigger.Template is null)
        {
            return $"{trigger.Kind} {functionName}";
        }

        var methods = !string.IsNullOrEmpty(method)
            ? method.ToUpperInvariant()
            : string.Join('|', trigger.Methods);
        var path = $"/{RoutePrefix}/{trigger.Template}";
        return methods.Length > 0 ? $"{methods} {path}" : path;
    }

    // The template without its constraints, defaults and optional markers: {view:regex(^(?!usage$))} becomes {view}.
    internal static string Normalize(string route)
    {
        var pattern = RoutePatternFactory.Parse(route);
        return string.Join('/', pattern.PathSegments.Select(segment => string.Concat(segment.Parts.Select(part => part switch
        {
            RoutePatternLiteralPart literal => literal.Content,
            RoutePatternSeparatorPart separator => separator.Content,
            RoutePatternParameterPart parameter => parameter.IsCatchAll ? $"{{*{parameter.Name}}}" : $"{{{parameter.Name}}}",
            _ => string.Empty,
        }))));
    }

    private static FrozenDictionary<string, FunctionTrigger> Read(Assembly assembly)
    {
        var functions = new Dictionary<string, FunctionTrigger>(StringComparer.Ordinal);

        foreach (var method in assembly.GetTypes().SelectMany(type => type.GetMethods(
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)))
        {
            var function = method.GetCustomAttribute<FunctionAttribute>();

            if (function is null)
            {
                continue;
            }

            var triggers = method.GetParameters()
                .SelectMany(parameter => parameter.GetCustomAttributes())
                .Where(attribute => attribute.GetType().Name.EndsWith("TriggerAttribute", StringComparison.Ordinal))
                .ToList();

            if (triggers.OfType<HttpTriggerAttribute>().FirstOrDefault() is { } http)
            {
                functions[function.Name] = new FunctionTrigger(
                    "http",
                    Normalize(http.Route ?? function.Name),
                    (http.Methods ?? []).Select(name => name.ToUpperInvariant()).ToArray());
            }
            else if (triggers.FirstOrDefault() is { } other)
            {
                // QueueTriggerAttribute: "queue", TimerTriggerAttribute: "timer"
                var kind = other.GetType().Name[..^"TriggerAttribute".Length].ToLowerInvariant();
                functions[function.Name] = new FunctionTrigger(kind, null, []);
            }
        }

        return functions.ToFrozenDictionary(StringComparer.Ordinal);
    }

    internal sealed record FunctionTrigger(string Kind, string? Template, IReadOnlyList<string> Methods);
}

using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;

using _42.Platform.Storyteller.Api.ErrorHandling;
using _42.Platform.Storyteller.Api.Security;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests.TenantIsolation;

// One request through the middlewares of Program.cs, in their order (ExceptionHandling, TransportSecurity in Production,
// MachineAuthentication, BearerAuthentication), to the real handler, which is created with the tenants' access service
// and a tripwire for every other service. The route values are bound by name, [FromQuery] parameters keep their defaults.
// The role check of users runs as in a Release build (UserRoleChecks.Enforce); the caller of the pipeline turns off the
// decoding of unsigned bearer tokens of Debug builds.
internal static partial class TenantPipeline
{
    private static readonly ExceptionHandlingMiddleware ExceptionHandling = new(NullLogger<ExceptionHandlingMiddleware>.Instance);
    private static readonly TransportSecurityMiddleware TransportSecurity = new(new TestHostEnvironment(Environments.Production));
    private static readonly MachineAuthenticationMiddleware MachineAuthentication = new(NullLogger<MachineAuthenticationMiddleware>.Instance);
    private static readonly BearerAuthenticationMiddleware BearerAuthentication = new(NullLogger<BearerAuthenticationMiddleware>.Instance);

    public static async Task<TenantOutcome> InvokeAsync(TenantWorld world, TenantRoute route, TenantCaller? caller, TenantTarget target)
    {
        var function = TenantRoutes.Functions[route.Function];
        var trigger = TenantRoutes.Trigger(function)!;
        var values = RouteValues(trigger.Route!, route, caller, target);
        var headers = caller is { } who ? world.HeadersOf(who) : [];
        var body = route.Body?
            .Replace("{organization}", target.Organization, StringComparison.Ordinal)
            .Replace("{project}", target.Project, StringComparison.Ordinal)
            .Replace("{key}", target.Key, StringComparison.Ordinal)
            .Replace("{userB}", TenantWorld.UserB, StringComparison.Ordinal);

        if (body is not null)
        {
            headers["Content-Type"] = route.ContentType;
        }

        var url = new Uri($"https://localhost/api/{Parameter().Replace(trigger.Route!, match => Uri.EscapeDataString(values.GetValueOrDefault(match.Groups["name"].Value, "x")))}");
        var (context, request) = FunctionTestDoubles.CreateRequest(world.Services, headers, trigger.Methods![0].ToUpperInvariant(), body, url);

        foreach (var (name, value) in route.Query)
        {
            request.Query[name] = value;
        }

        context.Definition = new ExceptionHandlingMiddlewareTests.HttpFunctionDefinition();
        var bindings = (ExceptionHandlingMiddlewareTests.BindingsFeature)ExceptionHandlingMiddlewareTests.BindingsFeature.Create();
        ((TestInvocationFeatures)context.Features).Set(ExceptionHandlingMiddlewareTests.BindingsFeature.FeatureType, bindings);

        var handler = ActivatorUtilities.CreateInstance(new TripwireServiceProvider(world.Services), function.DeclaringType!);
        var arguments = Arguments(function, request, values);
        object? result = null;

        var recorder = new TripwireRecorder();
        TripwireRecorder.Current = recorder;

        using (UserRoleChecks.Enforce())
        {
            await ExceptionHandling.Invoke(context, c =>
                TransportSecurity.Invoke(c, c2 =>
                    MachineAuthentication.Invoke(c2, c3 =>
                        BearerAuthentication.Invoke(c3, async _ => result = await CallAsync(function, handler, arguments)))));
        }

        var answer = bindings.InvocationResult as HttpResponseData ?? result;
        return new TenantOutcome(StatusOf(answer), recorder.Calls, DetailOf(answer));
    }

    // The values of the route's parameters: the target's tenant, and a value of the right shape for every other one.
    private static Dictionary<string, string> RouteValues(string template, TenantRoute route, TenantCaller? caller, TenantTarget target)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (Match match in Parameter().Matches(template))
        {
            var name = match.Groups["name"].Value;
            values[name] = name switch
            {
                "organization" => target.Organization,
                "project" => target.Project,
                "view" => target.View,
                "key" when route.Address == TenantAddress.Key => target.Key,
                "key" => TenantRoutes.AnnotationKey,
                "id" when route.Function == nameof(V1.CertificatesHttp.PostRenewCertificate) => (caller is { } who ? TenantWorld.MachineIdOf(who) : null) ?? "0b7c5d0e-3a5f-4f52-9a3e-6d1e2f3a4b5c",
                "id" => "0b7c5d0e-3a5f-4f52-9a3e-6d1e2f3a4b5c",
                "annotationType" => "sbt",
                "name" => "issuer-1",
                "thumbprint" => TenantWorld.Thumbprint,
                "accountId" => TenantWorld.UserB,
                "descendants" => "usages",
                "viewTo" => "other",
                "version" or "versionFrom" => "1",
                _ => throw new InvalidOperationException($"No value for the route parameter {name} of {route.Function}."),
            };
        }

        return values;
    }

    private static object?[] Arguments(MethodInfo function, HttpRequestData request, IReadOnlyDictionary<string, string> values)
    {
        return function.GetParameters()
            .Select(parameter => parameter.ParameterType == typeof(HttpRequestData)
                ? request
                : values.TryGetValue(parameter.Name!, out var value)
                    ? Convert(value, parameter.ParameterType)
                    : parameter.HasDefaultValue
                        ? parameter.DefaultValue
                        : throw new InvalidOperationException($"No argument for {parameter.Name} of {function.Name}."))
            .ToArray();
    }

    private static object Convert(string value, Type type)
    {
        return type == typeof(uint) ? uint.Parse(value, System.Globalization.CultureInfo.InvariantCulture) : value;
    }

    // A function that throws before its first await throws out of Invoke wrapped; the middlewares must see the exception.
    private static async Task<object?> CallAsync(MethodInfo function, object handler, object?[] arguments)
    {
        Task task;

        try
        {
            task = (Task)function.Invoke(handler, arguments)!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }

        await task;
        return task.GetType().GetProperty(nameof(Task<object>.Result))?.GetValue(task);
    }

    private static int StatusOf(object? result)
    {
        return result switch
        {
            HttpResponseData response => (int)response.StatusCode,
            IStatusCodeActionResult { StatusCode: { } code } => code,
            IActionResult => 200,
            _ => throw new InvalidOperationException($"The function answered {result?.GetType().Name ?? "nothing"}."),
        };
    }

    // The error of a client error, for the messages of the tests; a tripwire's 500 is told by what it reached.
    private static string? DetailOf(object? answer)
    {
        var text = answer switch
        {
            TestHttpResponseData { StatusCode: >= System.Net.HttpStatusCode.BadRequest and < System.Net.HttpStatusCode.InternalServerError } response => ReadBody(response),
            ObjectResult { StatusCode: >= 400 and < 500, Value: { } value } => System.Text.Json.JsonSerializer.Serialize(value, value.GetType()),
            _ => null,
        };

        return string.IsNullOrEmpty(text) ? null : text.Length > 300 ? text[..300] : text;
    }

    private static string ReadBody(TestHttpResponseData response)
    {
        response.Body.Position = 0;
        return new StreamReader(response.Body).ReadToEnd();
    }

    // A route parameter with its optional constraint; constraints hold no braces.
    [GeneratedRegex(@"\{(?<name>\w+)(:[^}]*)?\}")]
    private static partial Regex Parameter();
}

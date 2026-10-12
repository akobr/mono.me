using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Hosting;

namespace _42.Platform.Storyteller.Api.Security;

// Secrets travel only over HTTPS: a plain request is refused outside Development (400 HttpsRequired), and every
// response tells the browser to keep using HTTPS. The scheme comes from the request, or from X-Forwarded-Proto
// behind the platform's own proxy.
public class TransportSecurityMiddleware : IFunctionsWorkerMiddleware
{
    public const string StrictTransportSecurity = "max-age=31536000; includeSubDomains";

    private readonly bool _enforce;

    public TransportSecurityMiddleware(IHostEnvironment environment)
    {
        _enforce = !environment.IsDevelopment();
    }

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var request = await context.GetHttpRequestDataAsync();

        if (request is not null && _enforce && !IsHttps(request))
        {
            throw new InvalidInputException("The API accepts HTTPS only.", ErrorCodes.HttpsRequired);
        }

        await next(context);

        if (request is not null)
        {
            context.GetHttpContext()?.Response.Headers.TryAdd("Strict-Transport-Security", StrictTransportSecurity);
        }
    }

    public static bool IsHttps(HttpRequestData request)
    {
        if (string.Equals(request.Url.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return request.Headers.TryGetValues("X-Forwarded-Proto", out var values)
            && values.Any(value => value.Split(',').Any(scheme => string.Equals(scheme.Trim(), Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)));
    }
}

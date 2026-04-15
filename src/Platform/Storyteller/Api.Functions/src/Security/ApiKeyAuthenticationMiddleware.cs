using System.Net;
using System.Security.Claims;
using _42.Platform.Storyteller.Accessing;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace _42.Platform.Storyteller.Api.Security;

public class ApiKeyAuthenticationMiddleware : IFunctionsWorkerMiddleware
{
    private const string ApiKeyPrefix = "ApiKey ";

    private readonly ILogger<ApiKeyAuthenticationMiddleware> _logger;

    public ApiKeyAuthenticationMiddleware(ILogger<ApiKeyAuthenticationMiddleware> logger)
    {
        _logger = logger;
    }

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var httpReqData = await context.GetHttpRequestDataAsync();

        if (httpReqData is null)
        {
            await next(context);
            return;
        }

        if (!httpReqData.Headers.TryGetValues("Authorization", out var authValues))
        {
            await next(context);
            return;
        }

        var apiKeyValue = authValues.FirstOrDefault(v => v.StartsWith(ApiKeyPrefix, StringComparison.OrdinalIgnoreCase));

        if (apiKeyValue is null)
        {
            // Not an API key request, let other auth handlers deal with it.
            await next(context);
            return;
        }

        var rawKey = apiKeyValue[ApiKeyPrefix.Length..];

        if (string.IsNullOrWhiteSpace(rawKey))
        {
            await RespondUnauthorizedAsync(context, httpReqData);
            return;
        }

        var accessService = context.InstanceServices.GetRequiredService<IAccessService>();
        var result = await accessService.ValidateApiKeyAsync(rawKey);

        if (result is null)
        {
            _logger.LogWarning("Invalid API key presented for {url}", httpReqData.Url.AbsolutePath);
            await RespondUnauthorizedAsync(context, httpReqData);
            return;
        }

        // Synthesize claims so the existing auth pipeline works:
        // - "azp" claim makes TryGetApplicationIdentity() return the machine access ID
        // - role claims make CheckScope() pass
        var claims = new List<Claim>
        {
            new("azp", result.MachineAccessId),
            new("sub", result.MachineAccessId),
        };

        // Map MachineAccessScope to the appropriate role claims
        foreach (var roleClaim in GetRoleClaimsForScope(result.Scope))
        {
            claims.Add(new Claim("roles", roleClaim));
        }

        context.Items[FunctionContextItemKeys.CachedClaims] = claims;

        await next(context);
    }

    private static IEnumerable<string> GetRoleClaimsForScope(MachineAccessScope scope)
    {
        return scope switch
        {
            MachineAccessScope.AnnotationRead => [Scopes.Annotation.Read],
            MachineAccessScope.AnnotationReadWrite => [Scopes.Annotation.Read, Scopes.Annotation.Write],
            MachineAccessScope.ConfigurationRead => [Scopes.Configuration.Read],
            MachineAccessScope.ConfigurationReadWrite => [Scopes.Configuration.Read, Scopes.Configuration.Write],
            MachineAccessScope.DefaultRead => [Scopes.Default.Read, Scopes.Annotation.Read, Scopes.Configuration.Read],
            MachineAccessScope.DefaultReadWrite => [Scopes.Default.Read, Scopes.Default.Write, Scopes.Annotation.Read, Scopes.Annotation.Write, Scopes.Configuration.Read, Scopes.Configuration.Write],
            _ => [Scopes.Default.Read],
        };
    }

    private static async Task RespondUnauthorizedAsync(FunctionContext context, HttpRequestData httpReqData)
    {
        var response = httpReqData.CreateResponse(HttpStatusCode.Unauthorized);
        context.GetInvocationResult().Value = response;
    }
}

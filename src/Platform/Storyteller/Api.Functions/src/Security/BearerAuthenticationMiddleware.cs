using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;

using _42.Platform.Storyteller.Accessing;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller.Api.Security;

public class BearerAuthenticationMiddleware : IFunctionsWorkerMiddleware
{
    private const string BearerPrefix = "Bearer ";
    private const string RetryAfterSeconds = "5";

    private readonly ILogger<BearerAuthenticationMiddleware> _logger;

    public BearerAuthenticationMiddleware(ILogger<BearerAuthenticationMiddleware> logger)
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

        if (context.Items.ContainsKey(FunctionContextItemKeys.CachedClaims))
        {
            await next(context);
            return;
        }

        if (!TryGetBearerToken(httpReqData, out var rawToken))
        {
            await next(context);
            return;
        }

        if (BearerValidationMode.DecodeWithoutValidation)
        {
            var claims = TryReadUnvalidatedClaims(rawToken);

            if (claims is null)
            {
                await next(context);
                return;
            }

            var options = context.InstanceServices.GetRequiredService<IOptions<UserAuthenticationOptions>>().Value;
            StoreIdentity(context, claims, EntraIdBearerTokenValidator.TryGetMachineId(claims, options.ClientId));
            await next(context);
            return;
        }

        var validator = context.InstanceServices.GetRequiredService<IBearerTokenValidator>();
        BearerValidationResult? result;

        try
        {
            result = await validator.ValidateAsync(rawToken, context.CancellationToken);
        }
        catch (BearerKeyRetrievalException exception)
        {
            _logger.LogError(
                "Signing keys could not be retrieved for {Url} ({ExceptionType})",
                httpReqData.Url.AbsolutePath,
                exception.InnerException?.GetType().Name ?? exception.GetType().Name);
            RespondUnavailable(context, httpReqData);
            return;
        }

        if (result is null)
        {
            _logger.LogWarning("Invalid bearer token presented for {Url}", httpReqData.Url.AbsolutePath);
            RespondUnauthorized(context, httpReqData);
            return;
        }

        var validatedClaims = result.Claims as List<Claim> ?? result.Claims.ToList();
        StoreIdentity(context, validatedClaims, result.IsMachine ? result.MachineId : null);
        await next(context);
    }

    protected virtual void AssignResponse(FunctionContext context, HttpResponseData response)
    {
        context.GetInvocationResult().Value = response;
    }

    private static bool TryGetBearerToken(HttpRequestData request, out string rawToken)
    {
        rawToken = string.Empty;

        if (!request.Headers.TryGetValues("Authorization", out var values))
        {
            return false;
        }

        var bearerValue = values.FirstOrDefault(value => value.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase));

        if (bearerValue is null)
        {
            return false;
        }

        rawToken = bearerValue.Length > BearerPrefix.Length
            ? bearerValue[BearerPrefix.Length..]
            : string.Empty;
        return true;
    }

    private static List<Claim>? TryReadUnvalidatedClaims(string rawToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return null;
        }

        var handler = new JwtSecurityTokenHandler();

        if (!handler.CanReadToken(rawToken))
        {
            return null;
        }

        try
        {
            return handler.ReadJwtToken(rawToken).Claims.ToList();
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (SecurityTokenException)
        {
            return null;
        }
    }

    private static void StoreIdentity(FunctionContext context, List<Claim> claims, string? machineId)
    {
        context.Items[FunctionContextItemKeys.CachedClaims] = claims;

        if (!string.IsNullOrEmpty(machineId))
        {
            context.Items[FunctionContextItemKeys.MachineIdentity] = machineId;
        }
    }

    private void RespondUnauthorized(FunctionContext context, HttpRequestData request)
    {
        AssignResponse(context, request.CreateResponse(HttpStatusCode.Unauthorized));
    }

    private void RespondUnavailable(FunctionContext context, HttpRequestData request)
    {
        var response = request.CreateResponse(HttpStatusCode.ServiceUnavailable);
        response.Headers.Add("Retry-After", RetryAfterSeconds);
        AssignResponse(context, response);
    }
}

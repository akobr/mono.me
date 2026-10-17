using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;

using _42.Platform.Storyteller.Accessing;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

        var machineValidators = context.InstanceServices.GetServices<IMachineTokenValidator>().ToList();
        var issuer = machineValidators.Count > 0 ? TryReadIssuer(rawToken) : null;

        // A validator that owns the issuer runs alone (Storyteller tokens never pay a user provider attempt),
        // also under DEV_AUTH, so Federated machines behave locally as in production.
        var owner = issuer is null ? null : machineValidators.FirstOrDefault(candidate => candidate.OwnsIssuer(issuer));

        if (owner is null && BearerValidationMode.DecodeWithoutValidation)
        {
            var claims = TryReadUnvalidatedClaims(rawToken);

            if (claims is null)
            {
                await next(context);
                return;
            }

            // The active provider's normalizer still runs, so decoded AuthKit tokens get name, preferred_username and scp.
            var normalized = context.InstanceServices.GetRequiredService<IBearerClaimsNormalizer>().Normalize(claims);
            StoreIdentity(context, normalized);
            await next(context);
            return;
        }

        BearerValidationResult? result;

        try
        {
            if (owner is not null)
            {
                result = AsMachine(await owner.ValidateAsync(rawToken, context.CancellationToken));
            }
            else
            {
                // Machines may come from a different provider than users (IMachineTokenValidator).
                var validator = context.InstanceServices.GetRequiredService<IBearerTokenValidator>();
                result = await validator.ValidateAsync(rawToken, context.CancellationToken)
                    ?? await ValidateMachineTokenAsync(machineValidators, issuer, rawToken, context.CancellationToken);
            }
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

        StoreIdentity(context, result);
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

    // Only validators that claim the token's issuer are asked, and only machine results count.
    private static async Task<BearerValidationResult?> ValidateMachineTokenAsync(
        IReadOnlyList<IMachineTokenValidator> validators,
        string? issuer,
        string rawToken,
        CancellationToken cancellationToken)
    {
        if (issuer is null)
        {
            return null;
        }

        foreach (var validator in validators.Where(candidate => candidate.CanValidate(issuer)))
        {
            if (AsMachine(await validator.ValidateAsync(rawToken, cancellationToken)) is { } result)
            {
                return result;
            }
        }

        return null;
    }

    private static BearerValidationResult? AsMachine(BearerValidationResult? result)
    {
        return result is { IsMachine: true, MachineId.Length: > 0 } ? result : null;
    }

    private static string? TryReadIssuer(string rawToken)
    {
        var handler = new JwtSecurityTokenHandler();

        try
        {
            return handler.CanReadToken(rawToken) ? handler.ReadJwtToken(rawToken).Issuer : null;
        }
        catch (Exception exception) when (exception is ArgumentException or SecurityTokenException)
        {
            return null;
        }
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

    private static void StoreIdentity(FunctionContext context, BearerValidationResult result)
    {
        context.Items[FunctionContextItemKeys.CachedClaims] = result.Claims as List<Claim> ?? result.Claims.ToList();

        if (result.IsMachine && !string.IsNullOrEmpty(result.MachineId))
        {
            context.Items[FunctionContextItemKeys.MachineIdentity] = result.MachineId;

            // A machine bearer token is an identity provider client-credentials token, unless its validator says
            // otherwise (Storyteller tokens of Federated machines, which also name their project).
            context.Items[FunctionContextItemKeys.MachineCredentialKind] = result.CredentialKind ?? MachineCredentialKind.ClientCredentials;

            if (!string.IsNullOrEmpty(result.Organization))
            {
                context.Items[FunctionContextItemKeys.MachineOrganization] = result.Organization;
            }

            if (!string.IsNullOrEmpty(result.Project))
            {
                context.Items[FunctionContextItemKeys.MachineProject] = result.Project;
            }
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

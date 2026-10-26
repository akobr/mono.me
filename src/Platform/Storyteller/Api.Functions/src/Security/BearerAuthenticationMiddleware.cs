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
    private const string SubjectClaimType = "sub";
    private const string NameClaimType = "name";
    private const string UserNameClaimType = "preferred_username";

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
        var exchangeOptions = context.InstanceServices.GetService<IOptions<TokenExchangeOptions>>()?.Value;

        // Read without validation: the issuer picks the validators, and the audiences count once a validator accepted the
        // token (or DEV_AUTH decoded it).
        var token = TryReadToken(rawToken);
        var issuer = machineValidators.Count > 0 ? token?.Issuer : null;

        // A validator that owns the issuer runs alone (Storyteller tokens never pay a user provider attempt),
        // also under DEV_AUTH, so Federated machines and Storyteller user tokens behave locally as in production.
        var owner = issuer is null ? null : machineValidators.FirstOrDefault(candidate => candidate.OwnsIssuer(issuer));

        if (owner is null && BearerValidationMode.DecodeWithoutValidation)
        {
            if (token is null)
            {
                await next(context);
                return;
            }

            if (IsIssuedToAnActor(exchangeOptions, token))
            {
                RefuseTokenOfAnActor(context, httpReqData);
                return;
            }

            // The active provider's normalizer still runs, so decoded AuthKit tokens get name, preferred_username and scp.
            // Decoded claims never give a delegation: anyone can write act or restrictions into an unsigned token.
            var normalized = context.InstanceServices.GetRequiredService<IBearerClaimsNormalizer>().Normalize(token.Claims.ToList());
            StoreIdentity(context, normalized, delegation: null);
            await next(context);
            return;
        }

        BearerValidationResult? result;

        try
        {
            if (owner is not null)
            {
                result = FromOwner(owner, await owner.ValidateAsync(rawToken, context.CancellationToken));
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

        if (owner is null && IsIssuedToAnActor(exchangeOptions, token))
        {
            RefuseTokenOfAnActor(context, httpReqData);
            return;
        }

        // A Storyteller user token: the account it names must still exist, and gives the names the token does not carry.
        var delegation = owner is not null && !result.IsMachine ? result.Delegation : null;

        if (delegation is not null)
        {
            result = await WithAccountNamesAsync(context, result);

            if (result is null)
            {
                _logger.LogWarning("A Storyteller user token of no account is presented for {Url}", httpReqData.Url.AbsolutePath);
                RespondUnauthorized(context, httpReqData);
                return;
            }
        }

        StoreIdentity(context, result, delegation);
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

    // An owner's results are machines. Only an IUserTokenIssuerValidator (Storyteller) also gives users, and only with the
    // actor that acts for them; a user result of any other owner, or one without a delegation, is refused (401).
    private static BearerValidationResult? FromOwner(IMachineTokenValidator owner, BearerValidationResult? result)
    {
        return owner is IUserTokenIssuerValidator && result is { IsMachine: false, Delegation: not null }
            ? result
            : AsMachine(result);
    }

    // A token whose aud names an actor of the token exchange (one of its SubjectAudiences) was issued to the actor, for the
    // exchange only. Accepting it on API routes would be the token passthrough the MCP specification forbids, so it is
    // refused even when its provider accepts it.
    private static bool IsIssuedToAnActor(TokenExchangeOptions? options, JwtSecurityToken? token)
    {
        return options is not null && token is not null && options.IsIssuedToAnActor(token.Audiences);
    }

    // A Storyteller user token carries no name: one point read of its account adds name and preferred_username, the claims
    // a provider's token has, so audit stamps read "Ada via mcp". Null when the account no longer exists.
    private static async Task<BearerValidationResult?> WithAccountNamesAsync(FunctionContext context, BearerValidationResult result)
    {
        var accountId = result.Claims.FirstOrDefault(claim => claim.Type == SubjectClaimType)?.Value;

        if (string.IsNullOrWhiteSpace(accountId))
        {
            return null;
        }

        var account = await context.InstanceServices.GetRequiredService<IAccessService>().GetAccountAsync(accountId);

        if (account is null)
        {
            return null;
        }

        var claims = result.Claims.Where(claim => claim.Type is not (NameClaimType or UserNameClaimType)).ToList();

        if (!string.IsNullOrWhiteSpace(account.Name))
        {
            claims.Add(new Claim(NameClaimType, account.Name));
        }

        if (!string.IsNullOrWhiteSpace(account.UserName))
        {
            claims.Add(new Claim(UserNameClaimType, account.UserName));
        }

        return result with { Claims = claims };
    }

    private static JwtSecurityToken? TryReadToken(string rawToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return null;
        }

        var handler = new JwtSecurityTokenHandler();

        try
        {
            return handler.CanReadToken(rawToken) ? handler.ReadJwtToken(rawToken) : null;
        }
        catch (Exception exception) when (exception is ArgumentException or SecurityTokenException)
        {
            return null;
        }
    }

    // The delegation comes from the result of the Storyteller token validator only, never from claims: the user acts
    // through that actor, so the audit actor gets Via and the actor's restrictions apply.
    private static void StoreIdentity(FunctionContext context, BearerValidationResult result, BearerDelegation? delegation)
    {
        context.Items[FunctionContextItemKeys.CachedClaims] = result.Claims as List<Claim> ?? result.Claims.ToList();

        if (!result.IsMachine && delegation is not null)
        {
            context.Items[FunctionContextItemKeys.DelegatedActor] = delegation.ActorId;
            context.Items[FunctionContextItemKeys.DelegatedRestrictions] = delegation.Restrictions;
        }

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

    private void RefuseTokenOfAnActor(FunctionContext context, HttpRequestData request)
    {
        _logger.LogWarning("A bearer token issued to an actor of the token exchange is presented for {Url}", request.Url.AbsolutePath);
        RespondUnauthorized(context, request);
    }

    private void RespondUnavailable(FunctionContext context, HttpRequestData request)
    {
        var response = request.CreateResponse(HttpStatusCode.ServiceUnavailable);
        response.Headers.Add("Retry-After", RetryAfterSeconds);
        AssignResponse(context, response);
    }
}

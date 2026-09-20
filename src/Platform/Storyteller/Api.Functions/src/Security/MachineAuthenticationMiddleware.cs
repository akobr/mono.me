using System.Net;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Api.Security;

public class MachineAuthenticationMiddleware : IFunctionsWorkerMiddleware
{
    private const string ApiKeyPrefix = "ApiKey ";

    private readonly ILogger<MachineAuthenticationMiddleware> _logger;

    public MachineAuthenticationMiddleware(ILogger<MachineAuthenticationMiddleware> logger)
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

        var options = context.InstanceServices.GetRequiredService<IOptions<MachineAuthenticationOptions>>().Value;

        // Extract optional certificate header.
        var certificate = TryExtractCertificate(httpReqData, options, out var certError);

        if (certError)
        {
            await RespondUnauthorizedAsync(context, httpReqData);
            return;
        }

        // Extract optional API key.
        string? rawApiKey = null;

        if (httpReqData.Headers.TryGetValues("Authorization", out var authValues))
        {
            var apiKeyValue = authValues.FirstOrDefault(v => v.StartsWith(ApiKeyPrefix, StringComparison.OrdinalIgnoreCase));

            if (apiKeyValue is not null)
            {
                rawApiKey = apiKeyValue[ApiKeyPrefix.Length..];

                if (string.IsNullOrWhiteSpace(rawApiKey))
                {
                    await RespondUnauthorizedAsync(context, httpReqData);
                    return;
                }
            }
        }

        // Pass-through: no machine credentials present.
        if (certificate is null && rawApiKey is null)
        {
            await next(context);
            return;
        }

        // Validate credentials.
        Accessing.Model.ClientCertificateValidationResult? certResult = null;
        ApiKeyValidationResult? apiKeyResult = null;

        if (certificate is not null)
        {
            var validator = context.InstanceServices.GetRequiredService<IClientCertificateValidator>();
            certResult = await validator.ValidateAsync(certificate);

            if (certResult is null)
            {
                _logger.LogWarning("Invalid client certificate presented for {Url}", httpReqData.Url.AbsolutePath);
                await RespondUnauthorizedAsync(context, httpReqData);
                return;
            }
        }

        if (rawApiKey is not null)
        {
            var apiKeyValidator = context.InstanceServices.GetRequiredService<IApiKeyValidator>();
            apiKeyResult = await apiKeyValidator.ValidateAsync(rawApiKey);

            if (apiKeyResult is null)
            {
                _logger.LogWarning("Invalid API key presented for {Url}", httpReqData.Url.AbsolutePath);
                await RespondUnauthorizedAsync(context, httpReqData);
                return;
            }
        }

        // Reconcile identity.
        string organization;
        string project;
        string machineAccessId;
        MachineAccessScope scope;
        string? annotationKey = null;

        if (certResult is not null && apiKeyResult is not null)
        {
            // Both present: triples must match exactly.
            if (certResult.Kind == Accessing.Model.ClientCertificateKind.Machine)
            {
                if (!string.Equals(certResult.Organization, apiKeyResult.Organization, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(certResult.Project, apiKeyResult.Project, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(certResult.MachineAccessId, apiKeyResult.MachineAccessId, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("Certificate and API key identity mismatch");
                    await RespondUnauthorizedAsync(context, httpReqData);
                    return;
                }

                organization = certResult.Organization;
                project = certResult.Project;
                machineAccessId = certResult.MachineAccessId!;
                scope = certResult.Scope;
                annotationKey = certResult.AnnotationKey;
            }
            else
            {
                // Shared certificate: identity comes from the API key.
                if (!string.Equals(certResult.Organization, apiKeyResult.Organization, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(certResult.Project, apiKeyResult.Project, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("Shared certificate and API key organization/project mismatch");
                    await RespondUnauthorizedAsync(context, httpReqData);
                    return;
                }

                organization = apiKeyResult.Organization;
                project = apiKeyResult.Project;
                machineAccessId = apiKeyResult.MachineAccessId;
                scope = apiKeyResult.Scope;
            }
        }
        else if (certResult is not null)
        {
            if (certResult.Kind == Accessing.Model.ClientCertificateKind.Shared)
            {
                // Shared certificate without API key is rejected.
                _logger.LogWarning("Shared certificate presented without API key for {Url}", httpReqData.Url.AbsolutePath);
                await RespondUnauthorizedAsync(context, httpReqData);
                return;
            }

            organization = certResult.Organization;
            project = certResult.Project;
            machineAccessId = certResult.MachineAccessId!;
            scope = certResult.Scope;
            annotationKey = certResult.AnnotationKey;
        }
        else
        {
            // API key only.
            organization = apiKeyResult!.Organization;
            project = apiKeyResult.Project;
            machineAccessId = apiKeyResult.MachineAccessId;
            scope = apiKeyResult.Scope;
        }

        // Resolve project policy and check if presented credentials satisfy it.
        var policyStore = context.InstanceServices.GetRequiredService<IMachineAuthenticationPolicyStore>();
        var policy = await policyStore.GetAsync(organization, project);
        var requiredKind = policy?.CredentialKind ?? options.DefaultCredentialKind;

        var hasCert = certResult is not null;
        var hasApiKey = apiKeyResult is not null;

        var policySatisfied = requiredKind switch
        {
            MachineCredentialKind.ApiKey => hasApiKey,
            MachineCredentialKind.Certificate => hasCert,
            MachineCredentialKind.CertificateAndApiKey => hasCert && hasApiKey,
            _ => false,
        };

        if (!policySatisfied)
        {
            _logger.LogWarning(
                "Policy {RequiredKind} not satisfied (hasCert={HasCert}, hasApiKey={HasApiKey}) for {Url}",
                requiredKind, hasCert, hasApiKey, httpReqData.Url.AbsolutePath);
            await RespondUnauthorizedAsync(context, httpReqData);
            return;
        }

        // Synthesize claims.
        var claims = new List<Claim>
        {
            new("azp", machineAccessId),
            new("sub", machineAccessId),
        };

        var roleClaims = MachineScopeClaims.GetRoleClaimsForScope(scope);

        if (roleClaims.Count == 0)
        {
            await RespondUnauthorizedAsync(context, httpReqData);
            return;
        }

        foreach (var roleClaim in roleClaims)
        {
            claims.Add(new Claim("roles", roleClaim));
        }

        if (annotationKey is not null)
        {
            claims.Add(new Claim("annotationKey", annotationKey));
        }

        context.Items[FunctionContextItemKeys.CachedClaims] = claims;
        context.Items[FunctionContextItemKeys.MachineIdentity] = machineAccessId;

        if (certResult is not null)
        {
            context.Items[FunctionContextItemKeys.PresentingCertificateThumbprint] = certResult.Thumbprint;
        }

        await next(context);
    }

    private static X509Certificate2? TryExtractCertificate(
        HttpRequestData httpReqData,
        MachineAuthenticationOptions options,
        out bool error)
    {
        error = false;

        if (!httpReqData.Headers.TryGetValues(options.CertificateHeaderName, out var certValues))
        {
            return null;
        }

        var certValuesList = certValues.ToList();

        if (certValuesList.Count > 1)
        {
            // Reject duplicate certificate headers.
            error = true;
            return null;
        }

        var base64Der = certValuesList[0];

        if (string.IsNullOrWhiteSpace(base64Der))
        {
            return null;
        }

        try
        {
            var certBytes = Convert.FromBase64String(base64Der);
            return new X509Certificate2(certBytes);
        }
        catch
        {
            error = true;
            return null;
        }
    }

    private static async Task RespondUnauthorizedAsync(FunctionContext context, HttpRequestData httpReqData)
    {
        var response = httpReqData.CreateResponse(HttpStatusCode.Unauthorized);
        context.GetInvocationResult().Value = response;
    }
}

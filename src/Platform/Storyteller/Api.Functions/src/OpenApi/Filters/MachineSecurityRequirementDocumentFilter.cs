using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Abstractions;
using Microsoft.OpenApi.Models;

namespace _42.Platform.Storyteller.Api.OpenApi.Filters;

/// <summary>
/// Writes the machine credentials into the security requirements of the operations, which <c>[OpenApiSecurity]</c>
/// attributes cannot: each attribute is its own requirement (OR), and <c>CertificateAndApiKey</c> needs <c>mtls</c> and
/// <c>apiKey</c> in one requirement (AND).
/// <para>
/// An operation with the <c>manual</c> scheme that <see cref="OperationAccess.UserOnly"/> does not list also takes
/// <c>{apiKey}</c>, <c>{mtls}</c> and <c>{mtls, apiKey}</c>. The certificate renewal takes exactly <c>{mtls}</c> and
/// <c>{mtls, apiKey}</c>. Requirements reference the schemes by id, as a key without a reference would render as
/// <c>{}</c>, which means no authentication; they are compared by those ids, so a second run changes nothing.
/// </para>
/// </summary>
public class MachineSecurityRequirementDocumentFilter : IDocumentFilter
{
    private static readonly string[][] MachineRequirements =
    [
        [Definitions.SecuritySchemas.ApiKey],
        [Definitions.SecuritySchemas.Mtls],
        [Definitions.SecuritySchemas.Mtls, Definitions.SecuritySchemas.ApiKey],
    ];

    private static readonly string[][] RenewalRequirements =
    [
        [Definitions.SecuritySchemas.Mtls],
        [Definitions.SecuritySchemas.Mtls, Definitions.SecuritySchemas.ApiKey],
    ];

    public void Apply(IHttpRequestDataObject req, OpenApiDocument document)
    {
        foreach (var operation in document.Paths?.Values.SelectMany(path => path.Operations.Values) ?? [])
        {
            var id = operation.OperationId ?? string.Empty;

            if (OperationAccess.CertificateRenewal.Contains(id))
            {
                operation.Security = RenewalRequirements.Select(Requirement).ToList();
                continue;
            }

            if (OperationAccess.UserOnly.Contains(id) || !References(operation, Definitions.SecuritySchemas.Manual))
            {
                continue;
            }

            foreach (var schemes in MachineRequirements)
            {
                if (!operation.Security.Any(requirement => IsRequirementOf(requirement, schemes)))
                {
                    operation.Security.Add(Requirement(schemes));
                }
            }
        }
    }

    private static bool References(OpenApiOperation operation, string scheme)
    {
        return operation.Security?.Any(requirement => requirement.Keys.Any(key => key.Reference?.Id == scheme)) == true;
    }

    private static bool IsRequirementOf(OpenApiSecurityRequirement requirement, string[] schemes)
    {
        return requirement.Count == schemes.Length && requirement.Keys.All(key => key.Reference?.Id is { } id && schemes.Contains(id));
    }

    private static OpenApiSecurityRequirement Requirement(string[] schemes)
    {
        var requirement = new OpenApiSecurityRequirement();

        foreach (var scheme in schemes)
        {
            requirement[new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = scheme } }] = [];
        }

        return requirement;
    }
}

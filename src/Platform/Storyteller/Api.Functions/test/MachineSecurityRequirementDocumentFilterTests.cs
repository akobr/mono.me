using _42.Platform.Storyteller.Api.OpenApi.Filters;

using Microsoft.OpenApi.Models;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// The machine credentials in the requirements: OR between the requirement objects, AND inside one, written as "a+b".
public class MachineSecurityRequirementDocumentFilterTests
{
    private const string Manual = Definitions.SecuritySchemas.Manual;
    private const string Integrated = Definitions.SecuritySchemas.Integrated;
    private const string ApiKey = Definitions.SecuritySchemas.ApiKey;
    private const string Mtls = Definitions.SecuritySchemas.Mtls;

    [Fact]
    public void Apply_OperationForMachines_AddsTheApiKeyTheCertificateAndBoth()
    {
        var operation = Operation(Definitions.RouteIds.Configuration.GetConfiguration, [Manual], [Integrated]);

        new MachineSecurityRequirementDocumentFilter().Apply(null!, Document(operation));

        Requirements(operation).ShouldBe([Manual, Integrated, ApiKey, Mtls, $"{Mtls}+{ApiKey}"]);
    }

    [Fact]
    public void Apply_OperationForMachinesWithoutIntegrated_AddsTheMachineRequirementsOnly()
    {
        var operation = Operation(Definitions.RouteIds.Annotations.GetAnnotations, [Manual]);

        new MachineSecurityRequirementDocumentFilter().Apply(null!, Document(operation));

        Requirements(operation).ShouldBe([Manual, ApiKey, Mtls, $"{Mtls}+{ApiKey}"]);
    }

    [Fact]
    public void Apply_Twice_ChangesNothing()
    {
        var machine = Operation(Definitions.RouteIds.Configuration.GetConfiguration, [Manual], [Integrated]);
        var renewal = Operation(Definitions.RouteIds.Access.RenewMachineCertificate);
        var document = Document(machine, renewal);
        var filter = new MachineSecurityRequirementDocumentFilter();

        filter.Apply(null!, document);
        var machineFirst = Requirements(machine);
        var renewalFirst = Requirements(renewal);
        filter.Apply(null!, document);

        Requirements(machine).ShouldBe(machineFirst);
        Requirements(renewal).ShouldBe(renewalFirst);
        machineFirst.ShouldBe([Manual, Integrated, ApiKey, Mtls, $"{Mtls}+{ApiKey}"]);
    }

    [Theory]
    [InlineData(Definitions.RouteIds.Access.GetAccount)]
    [InlineData(Definitions.RouteIds.Access.IssueSharedCertificate)]
    [InlineData(Definitions.RouteIds.KeyValues.SetKeyValuePolicy)]
    public void Apply_UserOnlyOperation_LeavesItsRequirements(string operationId)
    {
        var operation = Operation(operationId, [Manual], [Integrated]);

        new MachineSecurityRequirementDocumentFilter().Apply(null!, Document(operation));

        Requirements(operation).ShouldBe([Manual, Integrated]);
    }

    [Fact]
    public void Apply_OperationWithoutManual_IsLeftAlone()
    {
        var anonymous = Operation(Definitions.RouteIds.Access.ExchangeMachineToken);
        var integratedOnly = Operation(Definitions.RouteIds.Annotations.GetAnnotation, [Integrated]);

        new MachineSecurityRequirementDocumentFilter().Apply(null!, Document(anonymous, integratedOnly));

        anonymous.Security.ShouldBeEmpty();
        Requirements(integratedOnly).ShouldBe([Integrated]);
    }

    // Whatever the attributes say, the renewal takes the certificate, alone or with the API key.
    [Fact]
    public void Apply_Renewal_HasExactlyTheCertificateAndTheCertificateWithTheApiKey()
    {
        var bare = Operation(Definitions.RouteIds.Access.RenewMachineCertificate);
        var attributed = Operation(Definitions.RouteIds.Access.RenewMachineCertificate, [Mtls], [Manual]);
        bare.Security = null;

        new MachineSecurityRequirementDocumentFilter().Apply(null!, Document(bare));
        new MachineSecurityRequirementDocumentFilter().Apply(null!, Document(attributed));

        Requirements(bare).ShouldBe([Mtls, $"{Mtls}+{ApiKey}"]);
        Requirements(attributed).ShouldBe([Mtls, $"{Mtls}+{ApiKey}"]);
    }

    // A key without a reference would render as {}, which means no authentication.
    [Fact]
    public void Apply_WritesReferencesToTheSchemesById()
    {
        var machine = Operation(Definitions.RouteIds.Configuration.GetConfiguration, [Manual]);
        var renewal = Operation(Definitions.RouteIds.Access.RenewMachineCertificate);

        new MachineSecurityRequirementDocumentFilter().Apply(null!, Document(machine, renewal));

        foreach (var requirement in machine.Security.Concat(renewal.Security))
        {
            requirement.ShouldNotBeEmpty();

            foreach (var scheme in requirement.Keys)
            {
                scheme.Reference.ShouldNotBeNull();
                scheme.Reference.Type.ShouldBe(ReferenceType.SecurityScheme);
                scheme.Reference.Id.ShouldBeOneOf(Manual, ApiKey, Mtls);
                requirement[scheme].ShouldBeEmpty();
            }
        }
    }

    private static List<string> Requirements(OpenApiOperation operation)
    {
        return operation.Security.Select(requirement => string.Join("+", requirement.Keys.Select(scheme => scheme.Reference.Id))).ToList();
    }

    // The shape the OpenAPI extension produces for [OpenApiSecurity] attributes: one requirement per attribute.
    private static OpenApiOperation Operation(string operationId, params string[][] requirements)
    {
        var operation = new OpenApiOperation { OperationId = operationId };

        foreach (var schemes in requirements)
        {
            var requirement = new OpenApiSecurityRequirement();

            foreach (var scheme in schemes)
            {
                requirement[new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = scheme } }] = [];
            }

            operation.Security.Add(requirement);
        }

        return operation;
    }

    private static OpenApiDocument Document(params OpenApiOperation[] operations)
    {
        var paths = new OpenApiPaths();

        for (var index = 0; index < operations.Length; index++)
        {
            paths[$"/v1/test/{index}"] = new OpenApiPathItem { Operations = { [OperationType.Get] = operations[index] } };
        }

        return new OpenApiDocument { Paths = paths };
    }
}

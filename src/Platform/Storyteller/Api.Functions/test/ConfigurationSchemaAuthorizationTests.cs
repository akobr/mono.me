using System.Reflection;

using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Api.Security;
using _42.Platform.Storyteller.Api.V1;
using _42.Platform.Storyteller.Configuring;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// The six schema writes govern the validation of every configuration of their type, so a machine needs the management
// scope Schema.Write for them on top of its runtime write scopes (424-57). The check reads the machine document in every
// build: a machine without it is refused (403 AccessDenied) before the body is read and before any service call. Users
// need no management scope; their role decides, as before (the role check runs in Release builds only).
public class ConfigurationSchemaAuthorizationTests
{
    private const string Org = "acme";
    private const string Project = "billing";
    private const string View = "default";
    private const string Key = "exe.northwind.invoicing.prod";
    private const string Type = AnnotationTypeCodes.Execution;
    private const string DescendantType = AnnotationTypeCodes.UnitOfExecution;

    private static readonly SchemaWrite[] Writes =
    [
        new(nameof(ConfigurationSchemaHttp.SetConfigurationSchema), "PUT", "{}", nameof(IConfigurationSchemaService.SetSchemaAsync), (http, request) => http.SetConfigurationSchema(request, Org, Project, View, Type)),
        new(nameof(ConfigurationSchemaHttp.DeleteConfigurationSchema), "DELETE", null, nameof(IConfigurationSchemaService.DeleteSchemaAsync), (http, request) => http.DeleteConfigurationSchema(request, Org, Project, View, Type)),
        new(nameof(ConfigurationSchemaHttp.SetAnnotationSchema), "PUT", "{}", nameof(IConfigurationSchemaService.SetAnnotationSchemaAsync), (http, request) => http.SetAnnotationSchema(request, Org, Project, View, Key)),
        new(nameof(ConfigurationSchemaHttp.DeleteAnnotationSchema), "DELETE", null, nameof(IConfigurationSchemaService.DeleteAnnotationSchemaAsync), (http, request) => http.DeleteAnnotationSchema(request, Org, Project, View, Key)),
        new(nameof(ConfigurationSchemaHttp.SetDescendantTypeSchema), "PUT", "{}", nameof(IConfigurationSchemaService.SetDescendantTypeSchemaAsync), (http, request) => http.SetDescendantTypeSchema(request, Org, Project, View, Key, DescendantType)),
        new(nameof(ConfigurationSchemaHttp.DeleteDescendantTypeSchema), "DELETE", null, nameof(IConfigurationSchemaService.DeleteDescendantTypeSchemaAsync), (http, request) => http.DeleteDescendantTypeSchema(request, Org, Project, View, Key, DescendantType)),
    ];

    private static bool DevAuth => HttpRequestDataExtensions.IsDevelopmentAuthentication;

    public static TheoryData<string> WriteNames()
    {
        return new TheoryData<string>(Writes.Select(write => write.Name));
    }

    [Theory]
    [MemberData(nameof(WriteNames))]
    public async Task Write_ByAMachineWithTheRuntimeScopesOnly_IsAccessDeniedBeforeTheService(string name)
    {
        var write = Write(name);
        var http = Http(Untouched<IConfigurationSchemaService>(), new RoleAccess(AccountRole.None));

        var exception = await Should.ThrowAsync<AccessDeniedException>(() => write.Invoke(http, MachineRequest(write)));

        exception.ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
        exception.Message.ShouldBe("This operation needs the management scope Schema.Write, which the machine was not created with.");
    }

    // The other management scopes do not stand in for it, Template.Write included.
    [Theory]
    [MemberData(nameof(WriteNames))]
    public async Task Write_ByAMachineWithEveryOtherManagementScope_IsAccessDenied(string name)
    {
        var write = Write(name);
        var others = Enum.GetValues<MachineManagementScope>().Where(scope => scope != MachineManagementScope.SchemaWrite).ToList();
        var http = Http(Untouched<IConfigurationSchemaService>(), new RoleAccess(AccountRole.None, managementScopes: others));

        var exception = await Should.ThrowAsync<AccessDeniedException>(() => write.Invoke(http, MachineRequest(write, others)));

        exception.ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
    }

    [Theory]
    [MemberData(nameof(WriteNames))]
    public async Task Write_ByAMachineWithSchemaWrite_ReachesTheService(string name)
    {
        var write = Write(name);
        var (schemas, recorder) = Recording<IConfigurationSchemaService>();
        var http = Http(schemas, new RoleAccess(AccountRole.None, managementScopes: [MachineManagementScope.SchemaWrite]));

        var result = await write.Invoke(http, MachineRequest(write, [MachineManagementScope.SchemaWrite]));

        result.ShouldNotBeOfType<BadRequestObjectResult>();
        recorder.Calls.ShouldHaveSingleItem().Method.ShouldBe(write.Method);
    }

    [Theory]
    [MemberData(nameof(WriteNames))]
    public async Task Write_ByAContributor_NeedsNoManagementScope(string name)
    {
        var write = Write(name);
        var (schemas, recorder) = Recording<IConfigurationSchemaService>();
        var http = Http(schemas, new RoleAccess(AccountRole.Contributor));

        await write.Invoke(http, KeyValueRequests.User(write.HttpMethod, body: write.Body));

        recorder.Calls.ShouldHaveSingleItem().Method.ShouldBe(write.Method);
    }

    [Theory]
    [MemberData(nameof(WriteNames))]
    public async Task Write_ByAReader_IsStillDenied(string name)
    {
        if (DevAuth)
        {
            return;
        }

        var write = Write(name);
        var http = Http(Untouched<IConfigurationSchemaService>(), new RoleAccess(AccountRole.Reader));

        var exception = await Should.ThrowAsync<AccessDeniedException>(() => write.Invoke(http, KeyValueRequests.User(write.HttpMethod, body: write.Body)));

        exception.Message.ShouldBe($"No Contributor access to '{Org}.{Project}'.");
    }

    private static SchemaWrite Write(string name)
    {
        return Writes.Single(write => write.Name == name);
    }

    private static ConfigurationSchemaHttp Http(IConfigurationSchemaService schemas, IAccessService access)
    {
        return new ConfigurationSchemaHttp(schemas, access, NullLogger<ConfigurationSchemaHttp>.Instance);
    }

    // A machine with the runtime write scopes of configurations, and the given management scopes in its credentials.
    private static TestHttpRequestData MachineRequest(SchemaWrite write, IReadOnlyList<MachineManagementScope>? managementScopes = null)
    {
        return KeyValueRequests.Machine(mtls: false, write.HttpMethod, body: write.Body, managementScopes: managementScopes, roles: [Scopes.Configuration.Read, Scopes.Configuration.Write]);
    }

    private static T Untouched<T>()
        where T : class
    {
        return DispatchProxy.Create<T, MachineRestrictionHttpTests.UntouchedProxy>();
    }

    private static (T Service, MachineRestrictionHttpTests.RecordingProxy Recorder) Recording<T>()
        where T : class
    {
        var service = DispatchProxy.Create<T, MachineRestrictionHttpTests.RecordingProxy>();
        return (service, (MachineRestrictionHttpTests.RecordingProxy)(object)service);
    }

    // A schema write: its HTTP method and body, the service method it reaches, and how it is called.
    private sealed record SchemaWrite(
        string Name,
        string HttpMethod,
        string? Body,
        string Method,
        Func<ConfigurationSchemaHttp, TestHttpRequestData, Task<IActionResult>> Invoke);
}

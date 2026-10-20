using System.Reflection;

using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Api.Security;
using _42.Platform.Storyteller.Api.V1;
using _42.Platform.Storyteller.Configuring;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// The template writes: a template is merged into every configuration of its type in the view, so a machine needs the
// management scope Template.Write for them on top of its runtime write scopes (424-57). The check reads the machine
// document in every build: a machine without it is refused (403 AccessDenied) before the body is read and before any
// service call. Users need no management scope; their role decides, as before (the role check runs in Release builds
// only).
public class TemplateHttpTests
{
    private const string Org = "acme";
    private const string Project = "billing";
    private const string View = "default";
    private const string Type = AnnotationTypeCodes.Execution;

    // SetTemplate answers POST and PUT in one function.
    private static readonly TemplateWrite[] Writes =
    [
        new("SetTemplate (POST)", "POST", "{}", nameof(IConfigurationTemplateService.CreateOrUpdateTemplateAsync), (http, request) => http.SetTemplate(request, Org, Project, View, Type)),
        new("SetTemplate (PUT)", "PUT", "{}", nameof(IConfigurationTemplateService.CreateOrUpdateTemplateAsync), (http, request) => http.SetTemplate(request, Org, Project, View, Type)),
        new(nameof(TemplateHttp.PatchTemplate), "PATCH", "[]", nameof(IConfigurationTemplateService.PatchTemplateAsync), (http, request) => http.PatchTemplate(request, Org, Project, View, Type)),
        new(nameof(TemplateHttp.DeleteTemplate), "DELETE", null, nameof(IConfigurationTemplateService.DeleteTemplateAsync), (http, request) => http.DeleteTemplate(request, Org, Project, View, Type)),
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
        var http = Http(Untouched<IConfigurationTemplateService>(), new RoleAccess(AccountRole.None));

        var exception = await Should.ThrowAsync<AccessDeniedException>(() => write.Invoke(http, MachineRequest(write)));

        exception.ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
        exception.Message.ShouldBe("This operation needs the management scope Template.Write, which the machine was not created with.");
    }

    // The other management scopes do not stand in for it, Schema.Write included.
    [Theory]
    [MemberData(nameof(WriteNames))]
    public async Task Write_ByAMachineWithEveryOtherManagementScope_IsAccessDenied(string name)
    {
        var write = Write(name);
        var others = Enum.GetValues<MachineManagementScope>().Where(scope => scope != MachineManagementScope.TemplateWrite).ToList();
        var http = Http(Untouched<IConfigurationTemplateService>(), new RoleAccess(AccountRole.None, managementScopes: others));

        var exception = await Should.ThrowAsync<AccessDeniedException>(() => write.Invoke(http, MachineRequest(write, others)));

        exception.ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
    }

    [Theory]
    [MemberData(nameof(WriteNames))]
    public async Task Write_ByAMachineWithTemplateWrite_ReachesTheService(string name)
    {
        var write = Write(name);
        var (templates, recorder) = Recording<IConfigurationTemplateService>();
        var http = Http(templates, new RoleAccess(AccountRole.None, managementScopes: [MachineManagementScope.TemplateWrite]));

        var result = await write.Invoke(http, MachineRequest(write, [MachineManagementScope.TemplateWrite]));

        result.ShouldNotBeOfType<BadRequestObjectResult>();
        recorder.Calls.ShouldHaveSingleItem().Method.ShouldBe(write.Method);
    }

    [Theory]
    [MemberData(nameof(WriteNames))]
    public async Task Write_ByAContributor_NeedsNoManagementScope(string name)
    {
        var write = Write(name);
        var (templates, recorder) = Recording<IConfigurationTemplateService>();
        var http = Http(templates, new RoleAccess(AccountRole.Contributor));

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
        var http = Http(Untouched<IConfigurationTemplateService>(), new RoleAccess(AccountRole.Reader));

        var exception = await Should.ThrowAsync<AccessDeniedException>(() => write.Invoke(http, KeyValueRequests.User(write.HttpMethod, body: write.Body)));

        exception.Message.ShouldBe($"No Contributor access to '{Org}.{Project}'.");
    }

    private static TemplateWrite Write(string name)
    {
        return Writes.Single(write => write.Name == name);
    }

    private static TemplateHttp Http(IConfigurationTemplateService templates, IAccessService access)
    {
        return new TemplateHttp(templates, access, NullLogger<TemplateHttp>.Instance);
    }

    // A machine with the runtime write scopes of configurations, and the given management scopes in its credentials.
    private static TestHttpRequestData MachineRequest(TemplateWrite write, IReadOnlyList<MachineManagementScope>? managementScopes = null)
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

    // A template write: its HTTP method and body, the service method it reaches, and how it is called.
    private sealed record TemplateWrite(
        string Name,
        string HttpMethod,
        string? Body,
        string Method,
        Func<TemplateHttp, TestHttpRequestData, Task<IActionResult>> Invoke);
}

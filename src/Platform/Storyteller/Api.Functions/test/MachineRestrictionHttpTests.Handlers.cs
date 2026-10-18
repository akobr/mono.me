using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Api.Security;
using _42.Platform.Storyteller.Api.V1;
using _42.Platform.Storyteller.Configuring;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// The handlers in tables. Every handler of one annotation refuses an ancestor and a sibling of the restriction before
// any service is called, and passes the key of its route to the service for the restriction and its descendants. The
// project-wide handlers next to them (type-level schemas, templates, deleted annotations, simple create) refuse the
// restricted machine before any service is called. A new handler of one annotation belongs in KeyedHandlers.
public partial class MachineRestrictionHttpTests
{
    private const string RestrictionType = AnnotationTypeCodes.Execution;
    private const string DescendantType = AnnotationTypeCodes.UnitOfExecution;
    private const string OtherView = "staging";
    private const uint Version = 2;
    private const uint VersionFrom = 1;

    // The service call that carries the key; a null answer of the service is fine, the handler then answers 404 or an
    // empty 200. The descendant (a unit of execution) is left out where it has no descendants to ask for.
    private static readonly KeyedHandler[] KeyedHandlers =
    [
        new(nameof(AnnotationsHttp.GetAnnotation), nameof(IAnnotationService.GetAnnotationAsync), FullKeyArg, (http, key) => http.Annotations.GetAnnotation(MachineRequest(), Org, Project, DefaultView, key)),
        new(nameof(AnnotationsHttp.GetDescendants), nameof(IAnnotationService.GetAnnotationsAsync), DescendantsOfArg, (http, key) => http.Annotations.GetDescendants(MachineRequest(), Org, Project, DefaultView, key), WithDescendant: false),
        new(nameof(AnnotationsHttp.SetAnnotation), nameof(IAnnotationService.SetAnnotationAsync), AnnotationArg, (http, key) => http.Annotations.SetAnnotation(MachineRequest("PUT", AnnotationBody(key)), Org, Project, DefaultView, key)),
        new(nameof(AnnotationsHttp.DeleteAnnotation), nameof(IAnnotationService.DeleteAnnotationAsync), FullKeyArg, (http, key) => http.Annotations.DeleteAnnotation(MachineRequest("DELETE"), Org, Project, DefaultView, key)),
        new(nameof(AnnotationsHttp.GetDeletedAnnotation), nameof(IAnnotationService.GetDeletedAnnotationAsync), FullKeyArg, (http, key) => http.Annotations.GetDeletedAnnotation(MachineRequest(), Org, Project, DefaultView, key)),
        new(nameof(AnnotationsHttp.GetAnnotationHistory), nameof(IAnnotationService.GetAnnotationHistoryAsync), FullKeyArg, (http, key) => http.Annotations.GetAnnotationHistory(MachineRequest(), Org, Project, DefaultView, key)),

        new(nameof(ConfigurationHttp.GetConfiguration), nameof(IConfigurationService.GetRawConfigurationAsync), FullKeyArg, (http, key) => http.Configuration.GetConfiguration(MachineRequest(), Org, Project, DefaultView, key)),
        new(nameof(ConfigurationHttp.GetConfigurationResolved), nameof(IConfigurationService.GetRawConfigurationAsync), FullKeyArg, (http, key) => http.Configuration.GetConfigurationResolved(MachineRequest(), Org, Project, DefaultView, key)),
        new(nameof(ConfigurationHttp.SetConfiguration), nameof(IConfigurationService.CreateOrUpdateConfigurationAsync), FullKeyArg, (http, key) => http.Configuration.SetConfiguration(MachineRequest("PUT", "{}"), Org, Project, DefaultView, key)),
        new(nameof(ConfigurationHttp.PatchConfiguration), nameof(IConfigurationService.PatchConfigurationAsync), FullKeyArg, (http, key) => http.Configuration.PatchConfiguration(MachineRequest("PATCH", "[]"), Org, Project, DefaultView, key)),
        new(nameof(ConfigurationHttp.DeleteConfiguration), nameof(IConfigurationService.ClearConfigurationAsync), FullKeyArg, (http, key) => http.Configuration.DeleteConfiguration(MachineRequest("DELETE"), Org, Project, DefaultView, key)),
        new(nameof(ConfigurationHttp.GetConfigurationVersions), nameof(IConfigurationService.GetConfigurationVersionsAsync), FullKeyArg, (http, key) => http.Configuration.GetConfigurationVersions(MachineRequest(), Org, Project, DefaultView, key)),
        new(nameof(ConfigurationHttp.GetConfigurationVersion), nameof(IConfigurationService.GetConfigurationVersionContentAsync), FullKeyArg, (http, key) => http.Configuration.GetConfigurationVersion(MachineRequest(), Org, Project, DefaultView, key, Version)),
        new(nameof(ConfigurationHttp.GetConfigurationVersionDiff), nameof(IConfigurationService.GetConfigurationVersionChangesAsync), FullKeyArg, (http, key) => http.Configuration.GetConfigurationVersionDiff(MachineRequest(), Org, Project, DefaultView, key, Version)),
        new(nameof(ConfigurationHttp.GetConfigurationVersionDiffCustom), nameof(IConfigurationService.GetConfigurationVersionChangesAsync), FullKeyArg, (http, key) => http.Configuration.GetConfigurationVersionDiffCustom(MachineRequest(), Org, Project, DefaultView, key, Version, VersionFrom)),
        new(nameof(ConfigurationHttp.GetConfigurationViewDiff), nameof(IConfigurationService.GetConfigurationViewChangesAsync), FullKeyArg, (http, key) => http.Configuration.GetConfigurationViewDiff(MachineRequest(), Org, Project, DefaultView, key, OtherView)),

        new(nameof(ConfigurationSchemaHttp.GetAnnotationSchema), nameof(IConfigurationSchemaService.GetAnnotationSchemaAsync), SchemaKeyArg, (http, key) => http.Schemas.GetAnnotationSchema(MachineRequest(), Org, Project, DefaultView, key)),
        new(nameof(ConfigurationSchemaHttp.SetAnnotationSchema), nameof(IConfigurationSchemaService.SetAnnotationSchemaAsync), SchemaKeyArg, (http, key) => http.Schemas.SetAnnotationSchema(MachineRequest("PUT", "{}"), Org, Project, DefaultView, key)),
        new(nameof(ConfigurationSchemaHttp.DeleteAnnotationSchema), nameof(IConfigurationSchemaService.DeleteAnnotationSchemaAsync), SchemaKeyArg, (http, key) => http.Schemas.DeleteAnnotationSchema(MachineRequest("DELETE"), Org, Project, DefaultView, key)),
        new(nameof(ConfigurationSchemaHttp.GetDescendantTypeSchema), nameof(IConfigurationSchemaService.GetDescendantTypeSchemaAsync), SchemaKeyArg, (http, key) => http.Schemas.GetDescendantTypeSchema(MachineRequest(), Org, Project, DefaultView, key, DescendantType), WithDescendant: false),
        new(nameof(ConfigurationSchemaHttp.SetDescendantTypeSchema), nameof(IConfigurationSchemaService.SetDescendantTypeSchemaAsync), SchemaKeyArg, (http, key) => http.Schemas.SetDescendantTypeSchema(MachineRequest("PUT", "{}"), Org, Project, DefaultView, key, DescendantType), WithDescendant: false),
        new(nameof(ConfigurationSchemaHttp.DeleteDescendantTypeSchema), nameof(IConfigurationSchemaService.DeleteDescendantTypeSchemaAsync), SchemaKeyArg, (http, key) => http.Schemas.DeleteDescendantTypeSchema(MachineRequest("DELETE"), Org, Project, DefaultView, key, DescendantType), WithDescendant: false),
        new(nameof(ConfigurationSchemaHttp.GetCombinedConfigurationSchema), nameof(IConfigurationSchemaService.GetCombinedSchemaAsync), SchemaKeyArg, (http, key) => http.Schemas.GetCombinedConfigurationSchema(MachineRequest(), Org, Project, DefaultView, key)),

        new(nameof(ConfigurationSchemaHttp.GetAnnotationSchemaVersions), nameof(IConfigurationSchemaService.GetAnnotationSchemaVersionsAsync), SchemaKeyArg, (http, key) => http.Schemas.GetAnnotationSchemaVersions(MachineRequest(), Org, Project, DefaultView, key)),
        new(nameof(ConfigurationSchemaHttp.GetAnnotationSchemaVersion), nameof(IConfigurationSchemaService.GetAnnotationSchemaVersionContentAsync), SchemaKeyArg, (http, key) => http.Schemas.GetAnnotationSchemaVersion(MachineRequest(), Org, Project, DefaultView, key, Version)),
        new(nameof(ConfigurationSchemaHttp.GetAnnotationSchemaVersionDiff), nameof(IConfigurationSchemaService.GetAnnotationSchemaVersionChangesAsync), SchemaKeyArg, (http, key) => http.Schemas.GetAnnotationSchemaVersionDiff(MachineRequest(), Org, Project, DefaultView, key, Version)),
        new(nameof(ConfigurationSchemaHttp.GetAnnotationSchemaVersionDiffCustom), nameof(IConfigurationSchemaService.GetAnnotationSchemaVersionChangesAsync), SchemaKeyArg, (http, key) => http.Schemas.GetAnnotationSchemaVersionDiffCustom(MachineRequest(), Org, Project, DefaultView, key, Version, VersionFrom)),
        new(nameof(ConfigurationSchemaHttp.GetDescendantTypeSchemaVersions), nameof(IConfigurationSchemaService.GetDescendantTypeSchemaVersionsAsync), SchemaKeyArg, (http, key) => http.Schemas.GetDescendantTypeSchemaVersions(MachineRequest(), Org, Project, DefaultView, key, DescendantType), WithDescendant: false),
        new(nameof(ConfigurationSchemaHttp.GetDescendantTypeSchemaVersion), nameof(IConfigurationSchemaService.GetDescendantTypeSchemaVersionContentAsync), SchemaKeyArg, (http, key) => http.Schemas.GetDescendantTypeSchemaVersion(MachineRequest(), Org, Project, DefaultView, key, DescendantType, Version), WithDescendant: false),
        new(nameof(ConfigurationSchemaHttp.GetDescendantTypeSchemaVersionDiff), nameof(IConfigurationSchemaService.GetDescendantTypeSchemaVersionChangesAsync), SchemaKeyArg, (http, key) => http.Schemas.GetDescendantTypeSchemaVersionDiff(MachineRequest(), Org, Project, DefaultView, key, DescendantType, Version), WithDescendant: false),
        new(nameof(ConfigurationSchemaHttp.GetDescendantTypeSchemaVersionDiffCustom), nameof(IConfigurationSchemaService.GetDescendantTypeSchemaVersionChangesAsync), SchemaKeyArg, (http, key) => http.Schemas.GetDescendantTypeSchemaVersionDiffCustom(MachineRequest(), Org, Project, DefaultView, key, DescendantType, Version, VersionFrom), WithDescendant: false),
    ];

    // Project-wide handlers, even for the type of the restriction or a key inside of it.
    private static readonly ProjectWideHandler[] ProjectWideHandlers =
    [
        new(nameof(ConfigurationSchemaHttp.GetSchemaVersions), http => http.Schemas.GetSchemaVersions(MachineRequest(), Org, Project, DefaultView, RestrictionType)),
        new(nameof(ConfigurationSchemaHttp.GetSchemaVersion), http => http.Schemas.GetSchemaVersion(MachineRequest(), Org, Project, DefaultView, RestrictionType, Version)),
        new(nameof(ConfigurationSchemaHttp.GetSchemaVersionDiff), http => http.Schemas.GetSchemaVersionDiff(MachineRequest(), Org, Project, DefaultView, RestrictionType, Version)),
        new(nameof(ConfigurationSchemaHttp.GetSchemaVersionDiffCustom), http => http.Schemas.GetSchemaVersionDiffCustom(MachineRequest(), Org, Project, DefaultView, RestrictionType, Version, VersionFrom)),
        new(nameof(ConfigurationSchemaHttp.SetConfigurationSchema), http => http.Schemas.SetConfigurationSchema(MachineRequest("PUT", "{}"), Org, Project, DefaultView, RestrictionType)),
        new(nameof(ConfigurationSchemaHttp.DeleteConfigurationSchema), http => http.Schemas.DeleteConfigurationSchema(MachineRequest("DELETE"), Org, Project, DefaultView, RestrictionType)),
        new(nameof(TemplateHttp.GetTemplate), http => http.Templates.GetTemplate(MachineRequest(), Org, Project, DefaultView, RestrictionType)),
        new(nameof(AnnotationsHttp.GetDeletedAnnotations), http => http.Annotations.GetDeletedAnnotations(MachineRequest(), Org, Project, DefaultView)),
        new(nameof(AnnotationsHttp.SetAnnotationsSimple), http => http.Annotations.SetAnnotationsSimple(MachineRequest("POST", FunctionTestDoubles.ToJsonBody<List<string>>([Restriction])), Org, Project, DefaultView)),
    ];

    // Debug builds (DEV_AUTH) skip the role check of users.
    private static bool DevAuth => HttpRequestDataExtensions.IsDevelopmentAuthentication;

    public static TheoryData<string, string> KeyedHandlersOutsideTheRestriction()
    {
        var cases = new TheoryData<string, string>();

        foreach (var handler in KeyedHandlers)
        {
            cases.Add(handler.Name, Ancestor);
            cases.Add(handler.Name, Sibling);
        }

        return cases;
    }

    public static TheoryData<string, string> KeyedHandlersInsideTheRestriction()
    {
        var cases = new TheoryData<string, string>();

        foreach (var handler in KeyedHandlers)
        {
            cases.Add(handler.Name, Restriction);

            if (handler.WithDescendant)
            {
                cases.Add(handler.Name, Descendant);
            }
        }

        return cases;
    }

    public static TheoryData<string> ProjectWideHandlerNames()
    {
        var cases = new TheoryData<string>();

        foreach (var handler in ProjectWideHandlers)
        {
            cases.Add(handler.Name);
        }

        return cases;
    }

    [Theory]
    [MemberData(nameof(KeyedHandlersOutsideTheRestriction))]
    public async Task KeyedHandler_OutsideTheRestriction_IsRefusedBeforeAnyService(string handler, string key)
    {
        var http = UntouchedHandlers();

        await ShouldBeRestrictedAsync(() => Keyed(handler).Invoke(http, key));
    }

    [Theory]
    [MemberData(nameof(KeyedHandlersInsideTheRestriction))]
    public async Task KeyedHandler_TheRestrictionOrADescendant_ReachesTheServiceWithTheKey(string handler, string key)
    {
        var keyed = Keyed(handler);
        var (http, recorders) = RecordingHandlers();

        var result = await keyed.Invoke(http, key);

        result.ShouldNotBeOfType<BadRequestObjectResult>();
        var call = recorders.SelectMany(recorder => recorder.Calls).Where(call => call.Method == keyed.Method).ShouldHaveSingleItem();
        keyed.KeyOf(call).ShouldBe(key);
    }

    [Theory]
    [MemberData(nameof(ProjectWideHandlerNames))]
    public async Task ProjectWideHandler_ByTheRestrictedMachine_IsRefusedBeforeAnyService(string handler)
    {
        var http = UntouchedHandlers();

        await ShouldBeRestrictedAsync(() => ProjectWideHandlers.Single(projectWide => projectWide.Name == handler).Invoke(http));
    }

    // A user keeps the role check of Release builds: deleting needs a Contributor.
    [Fact]
    public async Task DeleteAnnotation_ByAReader_IsDenied()
    {
        if (DevAuth)
        {
            return;
        }

        var http = new AnnotationsHttp(Untouched<IAnnotationService>(), new RoleAccess(AccountRole.Reader), TimeProvider.System, FunctionTestDoubles.ApiJsonOptions, NullLogger<AnnotationsHttp>.Instance);

        var exception = await Should.ThrowAsync<AccessDeniedException>(() => http.DeleteAnnotation(KeyValueRequests.User("DELETE"), Org, Project, DefaultView, Restriction));

        exception.ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
    }

    [Fact]
    public async Task DeleteAnnotation_ByAContributor_ReachesTheService()
    {
        var (annotations, recorder) = Recording<IAnnotationService>();
        var http = new AnnotationsHttp(annotations, new RoleAccess(AccountRole.Contributor), TimeProvider.System, FunctionTestDoubles.ApiJsonOptions, NullLogger<AnnotationsHttp>.Instance);

        await http.DeleteAnnotation(KeyValueRequests.User("DELETE"), Org, Project, DefaultView, Restriction);

        var call = recorder.Calls.ShouldHaveSingleItem();
        call.Method.ShouldBe(nameof(IAnnotationService.DeleteAnnotationAsync));
        FullKeyArg(call).ShouldBe(Restriction);
    }

    private static KeyedHandler Keyed(string name)
    {
        return KeyedHandlers.Single(handler => handler.Name == name);
    }

    private static string FullKeyArg(RecordedCall call) => call.Arg<FullKey>(0).Annotation.ToString();

    private static string SchemaKeyArg(RecordedCall call) => call.Arg<string>(3);

    private static string AnnotationArg(RecordedCall call) => call.Arg<Annotation>(1).AnnotationKey;

    private static string DescendantsOfArg(RecordedCall call) => call.Arg<AnnotationsRequest>(0).DescendantsOf!.ToString();

    // Carries the members of an execution and of a unit of execution; the members of the other type are ignored.
    private static string AnnotationBody(string key)
    {
        return $$"""
            {"AnnotationKey":"{{key}}","Name":"x","AnnotationType":"{{AnnotationKey.Parse(key).Type}}","ProjectName":"x","ViewName":"x",
             "ResponsibilityKey":"rst.invoicing","SubjectKey":"sbt.northwind","ContextKey":"cnt.northwind.prod","UsageKey":"usg.northwind.invoicing",
             "ExecutionKey":"exe.northwind.invoicing.prod","UnitKey":"unt.export","ResponsibilityName":"invoicing","SubjectName":"northwind",
             "ContextName":"prod","UnitName":"export"}
            """;
    }

    private static HttpHandlers UntouchedHandlers()
    {
        return new HttpHandlers(Untouched<IAnnotationService>(), Untouched<IConfigurationService>(), Untouched<IConfigurationSchemaService>(), Untouched<IConfigurationTemplateService>());
    }

    // The configuration answers the reads that the handlers do not check for null: there is content to delete, and no
    // version yet.
    private static (HttpHandlers Http, RecordingProxy[] Recorders) RecordingHandlers()
    {
        var (annotations, annotationCalls) = Recording<IAnnotationService>();
        var (configuration, configurationCalls) = Recording<IConfigurationService>();
        var (schemas, schemaCalls) = Recording<IConfigurationSchemaService>();
        var (templates, templateCalls) = Recording<IConfigurationTemplateService>();
        configurationCalls.Returns(nameof(IConfigurationService.HasConfigurationContentAsync), true);
        configurationCalls.Returns(nameof(IConfigurationService.GetConfigurationVersionsAsync), Array.Empty<ConfigurationVersion>());

        return (new HttpHandlers(annotations, configuration, schemas, templates), [annotationCalls, configurationCalls, schemaCalls, templateCalls]);
    }

    // A handler of one annotation: how it is called with the key of its route, and the service method that gets the key.
    private sealed record KeyedHandler(
        string Name,
        string Method,
        Func<RecordedCall, string> KeyOf,
        Func<HttpHandlers, string, Task<IActionResult>> Invoke,
        bool WithDescendant = true);

    private sealed record ProjectWideHandler(string Name, Func<HttpHandlers, Task<IActionResult>> Invoke);

    // The handlers over the same services, for the restricted machine.
    private sealed class HttpHandlers(
        IAnnotationService annotations,
        IConfigurationService configuration,
        IConfigurationSchemaService schemas,
        IConfigurationTemplateService templates)
    {
        public AnnotationsHttp Annotations { get; } = new(annotations, Restricted(), TimeProvider.System, FunctionTestDoubles.ApiJsonOptions, NullLogger<AnnotationsHttp>.Instance);

        public ConfigurationHttp Configuration { get; } = new(configuration, annotations, Restricted(), new FixedKeyValuePolicies(requireMtls: false), NullLogger<ConfigurationHttp>.Instance);

        public ConfigurationSchemaHttp Schemas { get; } = new(schemas, Restricted(), NullLogger<ConfigurationSchemaHttp>.Instance);

        public TemplateHttp Templates { get; } = new(templates, Restricted(), NullLogger<TemplateHttp>.Instance);
    }
}

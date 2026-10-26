using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Api.Security;
using _42.Platform.Storyteller.Api.V1;
using _42.Platform.Storyteller.Binding;
using _42.Platform.Storyteller.Configuring;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// The handlers in tables. Every handler of one annotation refuses an ancestor and a sibling of the restriction before
// any service is called, and passes the key of its route to the service for the restriction and its descendants. The
// project-wide handlers next to them (the lists, type-level schemas, templates, the composition of a configuration,
// deleted annotations, simple create) refuse the restricted machine before any service is called. A new handler of one
// annotation belongs in KeyedHandlers, a new project-wide one in ProjectWideHandlers, any other in OtherHandlers.
//
// Every row names the machine scopes of its handler (spec 7.5): the runtime scope, and the management scope of a
// management operation, which a machine needs after the runtime scope. The scope theories run every row for an
// unrestricted machine: it is refused (403 AccessDenied, before any service) when a scope of the row is not stored on
// the machine or not carried by its credentials, and served with exactly the scopes of the row.
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
        new(nameof(AnnotationsHttp.GetAnnotation), nameof(IAnnotationService.GetAnnotationAsync), FullKeyArg, (http, key) => http.Annotations.GetAnnotation(http.Request(), Org, Project, DefaultView, key), MachineScope.AnnotationRead),
        new(nameof(AnnotationsHttp.GetDescendants), nameof(IAnnotationService.GetAnnotationsAsync), DescendantsOfArg, (http, key) => http.Annotations.GetDescendants(http.Request(), Org, Project, DefaultView, key), MachineScope.AnnotationRead, WithDescendant: false),
        new(nameof(AnnotationsHttp.SetAnnotation), nameof(IAnnotationService.SetAnnotationAsync), AnnotationArg, (http, key) => http.Annotations.SetAnnotation(http.Request("PUT", AnnotationBody(key)), Org, Project, DefaultView, key), MachineScope.AnnotationWrite),
        new(nameof(AnnotationsHttp.DeleteAnnotation), nameof(IAnnotationService.DeleteAnnotationAsync), FullKeyArg, (http, key) => http.Annotations.DeleteAnnotation(http.Request("DELETE"), Org, Project, DefaultView, key), MachineScope.AnnotationWrite, MachineScope.AnnotationManage),
        new(nameof(AnnotationsHttp.GetDeletedAnnotation), nameof(IAnnotationService.GetDeletedAnnotationAsync), FullKeyArg, (http, key) => http.Annotations.GetDeletedAnnotation(http.Request(), Org, Project, DefaultView, key), MachineScope.AnnotationRead),
        new(nameof(AnnotationsHttp.GetAnnotationHistory), nameof(IAnnotationService.GetAnnotationHistoryAsync), FullKeyArg, (http, key) => http.Annotations.GetAnnotationHistory(http.Request(), Org, Project, DefaultView, key), MachineScope.AnnotationRead),

        new(nameof(ConfigurationHttp.GetConfiguration), nameof(IConfigurationService.GetRawConfigurationAsync), FullKeyArg, (http, key) => http.Configuration.GetConfiguration(http.Request(), Org, Project, DefaultView, key), MachineScope.ConfigurationRead),
        new(nameof(ConfigurationHttp.GetConfigurationResolved), nameof(IConfigurationService.GetRawConfigurationAsync), FullKeyArg, (http, key) => http.Configuration.GetConfigurationResolved(http.Request(), Org, Project, DefaultView, key), MachineScope.ConfigurationRead),
        new(nameof(ConfigurationHttp.SetConfiguration), nameof(IConfigurationService.CreateOrUpdateConfigurationAsync), FullKeyArg, (http, key) => http.Configuration.SetConfiguration(http.Request("PUT", "{}"), Org, Project, DefaultView, key), MachineScope.ConfigurationWrite),
        new(nameof(ConfigurationHttp.PatchConfiguration), nameof(IConfigurationService.PatchConfigurationAsync), FullKeyArg, (http, key) => http.Configuration.PatchConfiguration(http.Request("PATCH", "[]"), Org, Project, DefaultView, key), MachineScope.ConfigurationWrite),
        new(nameof(ConfigurationHttp.DeleteConfiguration), nameof(IConfigurationService.ClearConfigurationAsync), FullKeyArg, (http, key) => http.Configuration.DeleteConfiguration(http.Request("DELETE"), Org, Project, DefaultView, key), MachineScope.ConfigurationWrite),
        new(nameof(ConfigurationHttp.GetConfigurationVersions), nameof(IConfigurationService.GetConfigurationVersionsAsync), FullKeyArg, (http, key) => http.Configuration.GetConfigurationVersions(http.Request(), Org, Project, DefaultView, key), MachineScope.ConfigurationRead),
        new(nameof(ConfigurationHttp.GetConfigurationVersion), nameof(IConfigurationService.GetConfigurationVersionContentAsync), FullKeyArg, (http, key) => http.Configuration.GetConfigurationVersion(http.Request(), Org, Project, DefaultView, key, Version), MachineScope.ConfigurationRead),
        new(nameof(ConfigurationHttp.GetConfigurationVersionDiff), nameof(IConfigurationService.GetConfigurationVersionChangesAsync), FullKeyArg, (http, key) => http.Configuration.GetConfigurationVersionDiff(http.Request(), Org, Project, DefaultView, key, Version), MachineScope.ConfigurationRead),
        new(nameof(ConfigurationHttp.GetConfigurationVersionDiffCustom), nameof(IConfigurationService.GetConfigurationVersionChangesAsync), FullKeyArg, (http, key) => http.Configuration.GetConfigurationVersionDiffCustom(http.Request(), Org, Project, DefaultView, key, Version, VersionFrom), MachineScope.ConfigurationRead),
        new(nameof(ConfigurationHttp.GetConfigurationViewDiff), nameof(IConfigurationService.GetConfigurationViewChangesAsync), FullKeyArg, (http, key) => http.Configuration.GetConfigurationViewDiff(http.Request(), Org, Project, DefaultView, key, OtherView), MachineScope.ConfigurationRead),

        new(nameof(ConfigurationSchemaHttp.GetAnnotationSchema), nameof(IConfigurationSchemaService.GetAnnotationSchemaAsync), SchemaKeyArg, (http, key) => http.Schemas.GetAnnotationSchema(http.Request(), Org, Project, DefaultView, key), MachineScope.ConfigurationRead),
        new(nameof(ConfigurationSchemaHttp.SetAnnotationSchema), nameof(IConfigurationSchemaService.SetAnnotationSchemaAsync), SchemaKeyArg, (http, key) => http.Schemas.SetAnnotationSchema(http.Request("PUT", "{}"), Org, Project, DefaultView, key), MachineScope.ConfigurationWrite, MachineScope.SchemaWrite),
        new(nameof(ConfigurationSchemaHttp.DeleteAnnotationSchema), nameof(IConfigurationSchemaService.DeleteAnnotationSchemaAsync), SchemaKeyArg, (http, key) => http.Schemas.DeleteAnnotationSchema(http.Request("DELETE"), Org, Project, DefaultView, key), MachineScope.ConfigurationWrite, MachineScope.SchemaWrite),
        new(nameof(ConfigurationSchemaHttp.GetDescendantTypeSchema), nameof(IConfigurationSchemaService.GetDescendantTypeSchemaAsync), SchemaKeyArg, (http, key) => http.Schemas.GetDescendantTypeSchema(http.Request(), Org, Project, DefaultView, key, DescendantType), MachineScope.ConfigurationRead, WithDescendant: false),
        new(nameof(ConfigurationSchemaHttp.SetDescendantTypeSchema), nameof(IConfigurationSchemaService.SetDescendantTypeSchemaAsync), SchemaKeyArg, (http, key) => http.Schemas.SetDescendantTypeSchema(http.Request("PUT", "{}"), Org, Project, DefaultView, key, DescendantType), MachineScope.ConfigurationWrite, MachineScope.SchemaWrite, WithDescendant: false),
        new(nameof(ConfigurationSchemaHttp.DeleteDescendantTypeSchema), nameof(IConfigurationSchemaService.DeleteDescendantTypeSchemaAsync), SchemaKeyArg, (http, key) => http.Schemas.DeleteDescendantTypeSchema(http.Request("DELETE"), Org, Project, DefaultView, key, DescendantType), MachineScope.ConfigurationWrite, MachineScope.SchemaWrite, WithDescendant: false),
        new(nameof(ConfigurationSchemaHttp.GetCombinedConfigurationSchema), nameof(IConfigurationSchemaService.GetCombinedSchemaAsync), SchemaKeyArg, (http, key) => http.Schemas.GetCombinedConfigurationSchema(http.Request(), Org, Project, DefaultView, key), MachineScope.ConfigurationRead),

        new(nameof(ConfigurationSchemaHttp.GetAnnotationSchemaVersions), nameof(IConfigurationSchemaService.GetAnnotationSchemaVersionsAsync), SchemaKeyArg, (http, key) => http.Schemas.GetAnnotationSchemaVersions(http.Request(), Org, Project, DefaultView, key), MachineScope.ConfigurationRead),
        new(nameof(ConfigurationSchemaHttp.GetAnnotationSchemaVersion), nameof(IConfigurationSchemaService.GetAnnotationSchemaVersionContentAsync), SchemaKeyArg, (http, key) => http.Schemas.GetAnnotationSchemaVersion(http.Request(), Org, Project, DefaultView, key, Version), MachineScope.ConfigurationRead),
        new(nameof(ConfigurationSchemaHttp.GetAnnotationSchemaVersionDiff), nameof(IConfigurationSchemaService.GetAnnotationSchemaVersionChangesAsync), SchemaKeyArg, (http, key) => http.Schemas.GetAnnotationSchemaVersionDiff(http.Request(), Org, Project, DefaultView, key, Version), MachineScope.ConfigurationRead),
        new(nameof(ConfigurationSchemaHttp.GetAnnotationSchemaVersionDiffCustom), nameof(IConfigurationSchemaService.GetAnnotationSchemaVersionChangesAsync), SchemaKeyArg, (http, key) => http.Schemas.GetAnnotationSchemaVersionDiffCustom(http.Request(), Org, Project, DefaultView, key, Version, VersionFrom), MachineScope.ConfigurationRead),
        new(nameof(ConfigurationSchemaHttp.GetDescendantTypeSchemaVersions), nameof(IConfigurationSchemaService.GetDescendantTypeSchemaVersionsAsync), SchemaKeyArg, (http, key) => http.Schemas.GetDescendantTypeSchemaVersions(http.Request(), Org, Project, DefaultView, key, DescendantType), MachineScope.ConfigurationRead, WithDescendant: false),
        new(nameof(ConfigurationSchemaHttp.GetDescendantTypeSchemaVersion), nameof(IConfigurationSchemaService.GetDescendantTypeSchemaVersionContentAsync), SchemaKeyArg, (http, key) => http.Schemas.GetDescendantTypeSchemaVersion(http.Request(), Org, Project, DefaultView, key, DescendantType, Version), MachineScope.ConfigurationRead, WithDescendant: false),
        new(nameof(ConfigurationSchemaHttp.GetDescendantTypeSchemaVersionDiff), nameof(IConfigurationSchemaService.GetDescendantTypeSchemaVersionChangesAsync), SchemaKeyArg, (http, key) => http.Schemas.GetDescendantTypeSchemaVersionDiff(http.Request(), Org, Project, DefaultView, key, DescendantType, Version), MachineScope.ConfigurationRead, WithDescendant: false),
        new(nameof(ConfigurationSchemaHttp.GetDescendantTypeSchemaVersionDiffCustom), nameof(IConfigurationSchemaService.GetDescendantTypeSchemaVersionChangesAsync), SchemaKeyArg, (http, key) => http.Schemas.GetDescendantTypeSchemaVersionDiffCustom(http.Request(), Org, Project, DefaultView, key, DescendantType, Version, VersionFrom), MachineScope.ConfigurationRead, WithDescendant: false),
    ];

    // Project-wide handlers, even for the type of the restriction or a key inside of it.
    private static readonly ProjectWideHandler[] ProjectWideHandlers =
    [
        new(nameof(AnnotationsHttp.GetAnnotations), http => http.Annotations.GetAnnotations(http.Request(), Org, Project, DefaultView), MachineScope.AnnotationRead),
        new(nameof(AnnotationsHttp.GetResponsibilities), http => http.Annotations.GetResponsibilities(http.Request(), Org, Project, DefaultView), MachineScope.AnnotationRead),
        new(nameof(AnnotationsHttp.GetSubjects), http => http.Annotations.GetSubjects(http.Request(), Org, Project, DefaultView), MachineScope.AnnotationRead),
        new(nameof(AnnotationsHttp.GetUsages), http => http.Annotations.GetUsages(http.Request(), Org, Project, DefaultView), MachineScope.AnnotationRead),
        new(nameof(AnnotationsHttp.GetContexts), http => http.Annotations.GetContexts(http.Request(), Org, Project, DefaultView), MachineScope.AnnotationRead),
        new(nameof(AnnotationsHttp.GetExecutions), http => http.Annotations.GetExecutions(http.Request(), Org, Project, DefaultView), MachineScope.AnnotationRead),
        new(nameof(AnnotationsHttp.GetUnits), http => http.Annotations.GetUnits(http.Request(), Org, Project, DefaultView), MachineScope.AnnotationRead),
        new(nameof(AnnotationsHttp.GetUnitsOfExecution), http => http.Annotations.GetUnitsOfExecution(http.Request(), Org, Project, DefaultView), MachineScope.AnnotationRead),
        new(nameof(AnnotationsHttp.GetDeletedAnnotations), http => http.Annotations.GetDeletedAnnotations(http.Request(), Org, Project, DefaultView), MachineScope.AnnotationRead),
        new(nameof(AnnotationsHttp.SetAnnotationsSimple), http => http.Annotations.SetAnnotationsSimple(http.Request("POST", FunctionTestDoubles.ToJsonBody<List<string>>([Restriction])), Org, Project, DefaultView), MachineScope.AnnotationWrite, MachineScope.AnnotationManage),

        new(nameof(ConfigurationHttp.GetConfigurations), http => http.Configuration.GetConfigurations(http.Request(), Org, Project, DefaultView), MachineScope.ConfigurationRead),
        new(nameof(ConfigurationHttp.GetConfigurationComposition), http => http.Configuration.GetConfigurationComposition(http.Request(), Org, Project, DefaultView, Restriction), MachineScope.ConfigurationRead),

        new(nameof(ConfigurationSchemaHttp.GetConfigurationSchemas), http => http.Schemas.GetConfigurationSchemas(http.Request(), Org, Project, DefaultView), MachineScope.ConfigurationRead),
        new(nameof(ConfigurationSchemaHttp.GetConfigurationSchema), http => http.Schemas.GetConfigurationSchema(http.Request(), Org, Project, DefaultView, RestrictionType), MachineScope.ConfigurationRead),
        new(nameof(ConfigurationSchemaHttp.SetConfigurationSchema), http => http.Schemas.SetConfigurationSchema(http.Request("PUT", "{}"), Org, Project, DefaultView, RestrictionType), MachineScope.ConfigurationWrite, MachineScope.SchemaWrite),
        new(nameof(ConfigurationSchemaHttp.DeleteConfigurationSchema), http => http.Schemas.DeleteConfigurationSchema(http.Request("DELETE"), Org, Project, DefaultView, RestrictionType), MachineScope.ConfigurationWrite, MachineScope.SchemaWrite),
        new(nameof(ConfigurationSchemaHttp.GetSchemaVersions), http => http.Schemas.GetSchemaVersions(http.Request(), Org, Project, DefaultView, RestrictionType), MachineScope.ConfigurationRead),
        new(nameof(ConfigurationSchemaHttp.GetSchemaVersion), http => http.Schemas.GetSchemaVersion(http.Request(), Org, Project, DefaultView, RestrictionType, Version), MachineScope.ConfigurationRead),
        new(nameof(ConfigurationSchemaHttp.GetSchemaVersionDiff), http => http.Schemas.GetSchemaVersionDiff(http.Request(), Org, Project, DefaultView, RestrictionType, Version), MachineScope.ConfigurationRead),
        new(nameof(ConfigurationSchemaHttp.GetSchemaVersionDiffCustom), http => http.Schemas.GetSchemaVersionDiffCustom(http.Request(), Org, Project, DefaultView, RestrictionType, Version, VersionFrom), MachineScope.ConfigurationRead),

        new(nameof(TemplateHttp.GetTemplates), http => http.Templates.GetTemplates(http.Request(), Org, Project, DefaultView), MachineScope.ConfigurationRead),
        new(nameof(TemplateHttp.GetTemplate), http => http.Templates.GetTemplate(http.Request(), Org, Project, DefaultView, RestrictionType), MachineScope.ConfigurationRead),
        new(nameof(TemplateHttp.SetTemplate), http => http.Templates.SetTemplate(http.Request("PUT", "{}"), Org, Project, DefaultView, RestrictionType), MachineScope.ConfigurationWrite, MachineScope.TemplateWrite),
        new(nameof(TemplateHttp.PatchTemplate), http => http.Templates.PatchTemplate(http.Request("PATCH", "[]"), Org, Project, DefaultView, RestrictionType), MachineScope.ConfigurationWrite, MachineScope.TemplateWrite),
        new(nameof(TemplateHttp.DeleteTemplate), http => http.Templates.DeleteTemplate(http.Request("DELETE"), Org, Project, DefaultView, RestrictionType), MachineScope.ConfigurationWrite, MachineScope.TemplateWrite),
        new(nameof(TemplateHttp.GetTemplateVersions), http => http.Templates.GetTemplateVersions(http.Request(), Org, Project, DefaultView, RestrictionType), MachineScope.ConfigurationRead),
        new(nameof(TemplateHttp.GetTemplateVersion), http => http.Templates.GetTemplateVersion(http.Request(), Org, Project, DefaultView, RestrictionType, Version), MachineScope.ConfigurationRead),
        new(nameof(TemplateHttp.GetTemplateVersionDiff), http => http.Templates.GetTemplateVersionDiff(http.Request(), Org, Project, DefaultView, RestrictionType, Version), MachineScope.ConfigurationRead),
        new(nameof(TemplateHttp.GetTemplateVersionDiffCustom), http => http.Templates.GetTemplateVersionDiffCustom(http.Request(), Org, Project, DefaultView, RestrictionType, Version, VersionFrom), MachineScope.ConfigurationRead),
    ];

    // The handlers whose keys come in the body (MachineRestrictionHttpTests.cs has their restriction cases), and the
    // binding catalog, which a restricted machine reads too. GetViews takes either read scope; ViewsHttpTests has it.
    private static readonly ScopedHandler[] OtherHandlers =
    [
        new(nameof(AnnotationsHttp.SetAnnotations), http => http.Annotations.SetAnnotations(http.Request("POST", FunctionTestDoubles.ToJsonBody<List<Annotation>>([StoredAnnotation(Restriction)])), Org, Project, DefaultView), MachineScope.AnnotationWrite, MachineScope.AnnotationManage),
        new(nameof(AnnotationsHttp.FindDeletedAnnotations), http => http.Annotations.FindDeletedAnnotations(http.Request("POST", FunctionTestDoubles.ToJsonBody(new AnnotationKeysRequest { Keys = [Restriction] })), Org, Project, DefaultView), MachineScope.AnnotationRead),
        new(nameof(AnnotationsHttp.RestoreAnnotations), http => http.Annotations.RestoreAnnotations(http.Request("POST", FunctionTestDoubles.ToJsonBody(new AnnotationRestoreRequest { Keys = [Restriction] })), Org, Project, DefaultView), MachineScope.AnnotationWrite, MachineScope.AnnotationManage),
        new(nameof(BindingsHttp.GetBindingCatalog), http => http.Bindings.GetBindingCatalog(http.Request(), Org, Project), MachineScope.ConfigurationRead),
    ];

    // Debug builds (DEV_AUTH) skip the role check of users.
    private static bool DevAuth => HttpRequestDataExtensions.IsDevelopmentAuthentication;

    // Every row with its scopes: those of one annotation called with the key of the restriction.
    private static IEnumerable<ScopedHandler> ScopedHandlers =>
    [
        .. KeyedHandlers.Select(handler => new ScopedHandler(handler.Name, http => handler.Invoke(http, Restriction), handler.Scope, handler.ManagementScope)),
        .. ProjectWideHandlers.Select(handler => new ScopedHandler(handler.Name, handler.Invoke, handler.Scope, handler.ManagementScope)),
        .. OtherHandlers,
    ];

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

    // The handlers of one annotation that need a management scope from a machine, with the keys of the restriction.
    public static TheoryData<string, string> KeyedManagementHandlersInsideTheRestriction()
    {
        var cases = new TheoryData<string, string>();

        foreach (var handler in KeyedHandlers.Where(handler => handler.ManagementScope is not null))
        {
            cases.Add(handler.Name, Restriction);

            if (handler.WithDescendant)
            {
                cases.Add(handler.Name, Descendant);
            }
        }

        return cases;
    }

    public static TheoryData<string, string> KeyedManagementHandlersOutsideTheRestriction()
    {
        var cases = new TheoryData<string, string>();

        foreach (var handler in KeyedHandlers.Where(handler => handler.ManagementScope is not null))
        {
            cases.Add(handler.Name, Ancestor);
            cases.Add(handler.Name, Sibling);
        }

        return cases;
    }

    public static TheoryData<string> ProjectWideManagementHandlerNames()
    {
        var cases = new TheoryData<string>();

        foreach (var handler in ProjectWideHandlers.Where(handler => handler.ManagementScope is not null))
        {
            cases.Add(handler.Name);
        }

        return cases;
    }

    public static TheoryData<string> ScopedHandlerNames()
    {
        return new TheoryData<string>(ScopedHandlers.Select(handler => handler.Name));
    }

    public static TheoryData<string> ScopedManagementHandlerNames()
    {
        return new TheoryData<string>(ScopedHandlers.Where(handler => handler.ManagementScope is not null).Select(handler => handler.Name));
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

        await ShouldBeRestrictedAsync(() => ProjectWide(handler).Invoke(http));
    }

    // The restriction comes before the scopes: outside of it, a restricted machine created without the scopes of the
    // handler learns that the annotation is out of its reach, not which scope it lacks.
    [Theory]
    [MemberData(nameof(KeyedHandlersOutsideTheRestriction))]
    public async Task KeyedHandler_OutsideTheRestrictionWithoutItsScopes_IsRestrictedFirst(string handler, string key)
    {
        var keyed = Keyed(handler);
        var http = UntouchedHandlers(RestrictedWith(Without(MachineScopes.All, keyed.Scope, keyed.ManagementScope)));

        await ShouldBeRestrictedAsync(() => keyed.Invoke(http, key));
    }

    [Theory]
    [MemberData(nameof(ProjectWideHandlerNames))]
    public async Task ProjectWideHandler_ByTheRestrictedMachineWithoutItsScopes_IsRestrictedFirst(string handler)
    {
        var projectWide = ProjectWide(handler);
        var http = UntouchedHandlers(RestrictedWith(Without(MachineScopes.All, projectWide.Scope, projectWide.ManagementScope)));

        await ShouldBeRestrictedAsync(() => projectWide.Invoke(http));
    }

    // The management scope comes after the restriction (424-57): the restricted machine without it reaches its
    // annotation and the descendants no further than the 403 AccessDenied of the scope, before any service call.
    [Theory]
    [MemberData(nameof(KeyedManagementHandlersInsideTheRestriction))]
    public async Task KeyedManagementHandler_InsideTheRestrictionWithoutTheManagementScope_IsAccessDeniedBeforeAnyService(string handler, string key)
    {
        var keyed = Keyed(handler);
        var http = UntouchedHandlers(RestrictedWithoutManagementScopes());

        await ShouldNeedTheScopeAsync(() => keyed.Invoke(http, key), keyed.ManagementScope!.Value);
    }

    // And the restriction comes first: outside of it, the machine learns that the annotation is out of its reach.
    [Theory]
    [MemberData(nameof(KeyedManagementHandlersOutsideTheRestriction))]
    public async Task KeyedManagementHandler_OutsideTheRestrictionWithoutTheManagementScope_IsRestrictedFirst(string handler, string key)
    {
        var http = UntouchedHandlers(RestrictedWithoutManagementScopes());

        await ShouldBeRestrictedAsync(() => Keyed(handler).Invoke(http, key));
    }

    [Theory]
    [MemberData(nameof(ProjectWideManagementHandlerNames))]
    public async Task ProjectWideManagementHandler_ByTheRestrictedMachineWithoutTheManagementScope_IsRestrictedFirst(string handler)
    {
        var http = UntouchedHandlers(RestrictedWithoutManagementScopes());

        await ShouldBeRestrictedAsync(() => ProjectWide(handler).Invoke(http));
    }

    // Exactly the scopes of the row, stored and carried, and nothing else: a write needs no read scope, and nothing
    // expands at request time.
    [Theory]
    [MemberData(nameof(ScopedHandlerNames))]
    public async Task Handler_ByAMachineWithExactlyItsScopes_ReachesTheService(string handler)
    {
        var scoped = Scoped(handler);
        var (http, recorders) = RecordingHandlers(Unrestricted(scoped.Scopes), scoped.Scopes);

        var result = await scoped.Invoke(http);

        result.ShouldNotBeOfType<BadRequestObjectResult>();
        recorders.SelectMany(recorder => recorder.Calls).ShouldNotBeEmpty();
    }

    // Every other scope, the write of a read included, does not stand in for the runtime scope of the row.
    [Theory]
    [MemberData(nameof(ScopedHandlerNames))]
    public async Task Handler_ByAMachineCreatedWithoutItsRuntimeScope_IsAccessDeniedBeforeAnyService(string handler)
    {
        var scoped = Scoped(handler);
        var http = UntouchedHandlers(Unrestricted(Without(MachineScopes.All, scoped.Scope)));

        await ShouldNeedTheScopeAsync(() => scoped.Invoke(http), scoped.Scope);
    }

    // A federated token that asked for a subset, or a provider token without the permission or the role.
    [Theory]
    [MemberData(nameof(ScopedHandlerNames))]
    public async Task Handler_ByAMachineWhoseCredentialsDoNotCarryItsRuntimeScope_IsAccessDeniedBeforeAnyService(string handler)
    {
        var scoped = Scoped(handler);
        var http = UntouchedHandlers(Unrestricted(MachineScopes.All), Without(MachineScopes.All, scoped.Scope));

        await ShouldNeedTheScopeAsync(() => scoped.Invoke(http), scoped.Scope, NotCarried);
    }

    [Theory]
    [MemberData(nameof(ScopedManagementHandlerNames))]
    public async Task ManagementHandler_ByAMachineCreatedWithoutItsManagementScope_IsAccessDeniedBeforeAnyService(string handler)
    {
        var scoped = Scoped(handler);
        var http = UntouchedHandlers(Unrestricted(Without(MachineScopes.All, scoped.ManagementScope)));

        await ShouldNeedTheScopeAsync(() => scoped.Invoke(http), scoped.ManagementScope!.Value);
    }

    [Theory]
    [MemberData(nameof(ScopedManagementHandlerNames))]
    public async Task ManagementHandler_ByAMachineWhoseCredentialsDoNotCarryItsManagementScope_IsAccessDeniedBeforeAnyService(string handler)
    {
        var scoped = Scoped(handler);
        var http = UntouchedHandlers(Unrestricted(MachineScopes.All), Without(MachineScopes.All, scoped.ManagementScope));

        await ShouldNeedTheScopeAsync(() => scoped.Invoke(http), scoped.ManagementScope!.Value, NotCarried);
    }

    // A management scope grants nothing without the runtime scope of its operation, which is checked first.
    [Theory]
    [MemberData(nameof(ScopedManagementHandlerNames))]
    public async Task ManagementHandler_ByAMachineWithItsManagementScopeAlone_NeedsTheRuntimeScopeFirst(string handler)
    {
        var scoped = Scoped(handler);
        MachineScope[] alone = [scoped.ManagementScope!.Value];
        var http = UntouchedHandlers(Unrestricted(alone), alone);

        await ShouldNeedTheScopeAsync(() => scoped.Invoke(http), scoped.Scope);
    }

    // Without both scopes the refusal names the runtime scope, which is checked first.
    [Theory]
    [MemberData(nameof(ScopedManagementHandlerNames))]
    public async Task ManagementHandler_ByAMachineCreatedWithoutItsRuntimeAndManagementScopes_NeedsTheRuntimeScopeFirst(string handler)
    {
        var scoped = Scoped(handler);
        var http = UntouchedHandlers(Unrestricted(Without(MachineScopes.All, scoped.Scope, scoped.ManagementScope)));

        await ShouldNeedTheScopeAsync(() => scoped.Invoke(http), scoped.Scope);
    }

    [Theory]
    [MemberData(nameof(ScopedManagementHandlerNames))]
    public async Task ManagementHandler_ByAMachineWhoseCredentialsCarryNeitherOfItsScopes_NeedsTheRuntimeScopeFirst(string handler)
    {
        var scoped = Scoped(handler);
        var http = UntouchedHandlers(Unrestricted(MachineScopes.All), Without(MachineScopes.All, scoped.Scope, scoped.ManagementScope));

        await ShouldNeedTheScopeAsync(() => scoped.Invoke(http), scoped.Scope, NotCarried);
    }

    // Users carry no data scope: User.Impersonation and the role decide (the role check runs in Release builds only).
    [Theory]
    [MemberData(nameof(ScopedHandlerNames))]
    public async Task Handler_ByAContributorWithoutDataScopes_ReachesTheService(string handler)
    {
        var scoped = Scoped(handler);
        var (http, recorders) = RecordingHandlers(new RoleAccess(AccountRole.Contributor), asUser: true);

        var result = await scoped.Invoke(http);

        result.ShouldNotBeOfType<BadRequestObjectResult>();
        recorders.SelectMany(recorder => recorder.Calls).ShouldNotBeEmpty();
    }

    // The delete route always asks the service to clear, which finishes an earlier clear left pending: nothing to
    // clear answers 404.
    [Theory]
    [InlineData(true, typeof(OkResult))]
    [InlineData(false, typeof(NotFoundResult))]
    public async Task DeleteConfiguration_AnswersWhatTheClearDid(bool cleared, Type expected)
    {
        var (http, recorders) = RecordingHandlers(new RoleAccess(AccountRole.Contributor), asUser: true);
        recorders[1].Returns(nameof(IConfigurationService.ClearConfigurationAsync), cleared);

        var result = await http.Configuration.DeleteConfiguration(http.Request("DELETE"), Org, Project, DefaultView, Restriction);

        result.ShouldBeOfType(expected);
        recorders[1].Calls.ShouldContain(call => call.Method == nameof(IConfigurationService.ClearConfigurationAsync));
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

    private static ProjectWideHandler ProjectWide(string name)
    {
        return ProjectWideHandlers.Single(handler => handler.Name == name);
    }

    private static ScopedHandler Scoped(string name)
    {
        return ScopedHandlers.Single(handler => handler.Name == name);
    }

    // The scopes but the excluded ones; a null excludes nothing (a row without a management scope).
    private static MachineScope[] Without(IEnumerable<MachineScope> scopes, params MachineScope?[] excluded)
    {
        return [.. scopes.Where(scope => !excluded.Contains(scope))];
    }

    // A machine of the whole project, created with exactly the given scopes.
    private static RoleAccess Unrestricted(IReadOnlyList<MachineScope> scopes)
    {
        return new RoleAccess(AccountRole.None, scopes: scopes);
    }

    private static RoleAccess RestrictedWith(IReadOnlyList<MachineScope> scopes)
    {
        return new RoleAccess(AccountRole.Reader, Restriction, scopes: scopes);
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

    private static HttpHandlers UntouchedHandlers(IAccessService? access = null, IReadOnlyList<MachineScope>? carried = null)
    {
        return new HttpHandlers(
            Untouched<IAnnotationService>(),
            Untouched<IConfigurationService>(),
            Untouched<IConfigurationSchemaService>(),
            Untouched<IConfigurationTemplateService>(),
            Untouched<IBindingCatalog>(),
            access ?? Restricted(),
            carried);
    }

    // The services answer the reads that the handlers do not check for null: an empty page of annotations, a cleared
    // configuration, and no version yet.
    private static (HttpHandlers Http, RecordingProxy[] Recorders) RecordingHandlers(
        IAccessService? access = null,
        IReadOnlyList<MachineScope>? carried = null,
        bool asUser = false)
    {
        var (annotations, annotationCalls) = Recording<IAnnotationService>();
        var (configuration, configurationCalls) = Recording<IConfigurationService>();
        var (schemas, schemaCalls) = Recording<IConfigurationSchemaService>();
        var (templates, templateCalls) = Recording<IConfigurationTemplateService>();
        var (bindings, bindingCalls) = Recording<IBindingCatalog>();
        annotationCalls.Returns(nameof(IAnnotationService.GetAnnotationsAsync), new AnnotationsResponse());
        configurationCalls.Returns(nameof(IConfigurationService.ClearConfigurationAsync), true);
        configurationCalls.Returns(nameof(IConfigurationService.GetConfigurationVersionsAsync), Array.Empty<ConfigurationVersion>());

        var http = new HttpHandlers(annotations, configuration, schemas, templates, bindings, access ?? Restricted(), carried, asUser);
        return (http, [annotationCalls, configurationCalls, schemaCalls, templateCalls, bindingCalls]);
    }

    // A handler of one annotation: how it is called with the key of its route, the service method that gets the key,
    // and the scopes a machine needs for it: the runtime scope, and the management scope of a management operation.
    private sealed record KeyedHandler(
        string Name,
        string Method,
        Func<RecordedCall, string> KeyOf,
        Func<HttpHandlers, string, Task<IActionResult>> Invoke,
        MachineScope Scope,
        MachineScope? ManagementScope = null,
        bool WithDescendant = true);

    private sealed record ProjectWideHandler(string Name, Func<HttpHandlers, Task<IActionResult>> Invoke, MachineScope Scope, MachineScope? ManagementScope = null);

    private sealed record ScopedHandler(string Name, Func<HttpHandlers, Task<IActionResult>> Invoke, MachineScope Scope, MachineScope? ManagementScope = null)
    {
        public MachineScope[] Scopes => ManagementScope is { } management ? [Scope, management] : [Scope];
    }

    // The handlers over the same services, for the machine of the access double (the restricted one in the tables), and
    // its requests: their credentials carry the given scopes, every scope by default. Or a user's requests, which carry
    // only User.Impersonation.
    private sealed class HttpHandlers(
        IAnnotationService annotations,
        IConfigurationService configuration,
        IConfigurationSchemaService schemas,
        IConfigurationTemplateService templates,
        IBindingCatalog bindings,
        IAccessService access,
        IReadOnlyList<MachineScope>? carried = null,
        bool asUser = false)
    {
        public AnnotationsHttp Annotations { get; } = new(annotations, access, TimeProvider.System, FunctionTestDoubles.ApiJsonOptions, NullLogger<AnnotationsHttp>.Instance);

        public ConfigurationHttp Configuration { get; } = new(configuration, annotations, access, new FixedKeyValuePolicies(requireMtls: false), NullLogger<ConfigurationHttp>.Instance);

        public ConfigurationSchemaHttp Schemas { get; } = new(schemas, access, NullLogger<ConfigurationSchemaHttp>.Instance);

        public TemplateHttp Templates { get; } = new(templates, access, NullLogger<TemplateHttp>.Instance);

        public BindingsHttp Bindings { get; } = new(access, bindings);

        public TestHttpRequestData Request(string method = "GET", string? body = null)
        {
            return asUser ? TestRequests.User(method, body) : TestRequests.Machine(carried, method, body);
        }
    }
}

using System.Reflection;
using System.Security.Claims;

using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Api.Security;
using _42.Platform.Storyteller.Api.V1;
using _42.Platform.Storyteller.Binding;
using _42.Platform.Storyteller.Configuring;
using _42.Platform.Storyteller.KeyValues;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// A machine restricted to one execution, at the handlers: it reaches the execution and its descendants, and gets
// 403 AnnotationRestricted before any service is called for an ancestor, a sibling or an operation that spans the
// project. The machine branch of the access checks runs in every build, so these tests hold in Debug and Release.
// MachineRestrictionHttpTests.Handlers.cs runs every handler of one annotation, and more project-wide ones, from tables.
public partial class MachineRestrictionHttpTests
{
    private const string Org = "acme";
    private const string Project = "billing";
    private const string DefaultView = "default";

    // The key of the machine, one of its descendants, and keys outside of it.
    private const string Restriction = "exe.northwind.invoicing.prod";
    private const string Descendant = "uxe.northwind.invoicing.prod.export";
    private const string Parent = "usg.northwind.invoicing";
    private const string Ancestor = "sbt.northwind";
    private const string Sibling = "exe.northwind.invoicing.dev";

    private static readonly AuditStamp Stamp = AuditStamp.Of(AuditActor.System, DateTimeOffset.UnixEpoch);

    [Theory]
    [InlineData(Ancestor)]
    [InlineData(Parent)]
    [InlineData(Sibling)]
    [InlineData("nope")]
    public async Task GetConfiguration_OutsideTheRestriction_IsRefusedBeforeTheService(string key)
    {
        var http = CreateConfigurationHttp(Untouched<IConfigurationService>(), Restricted());

        await ShouldBeRestrictedAsync(() => http.GetConfiguration(MachineRequest(), Org, Project, DefaultView, key));
    }

    [Theory]
    [InlineData(Ancestor)]
    [InlineData(Sibling)]
    public async Task GetConfigurationResolved_OutsideTheRestriction_IsRefusedBeforeTheService(string key)
    {
        var http = CreateConfigurationHttp(Untouched<IConfigurationService>(), Restricted());

        await ShouldBeRestrictedAsync(() => http.GetConfigurationResolved(MachineRequest(), Org, Project, DefaultView, key));
    }

    [Theory]
    [InlineData(Restriction)]
    [InlineData(Descendant)]
    public async Task GetConfiguration_TheRestrictionOrADescendant_ReachesTheService(string key)
    {
        var (configuration, recorder) = Recording<IConfigurationService>();
        recorder.Returns(nameof(IConfigurationService.GetRawConfigurationAsync), StoredConfiguration(key));

        var result = await CreateConfigurationHttp(configuration, Restricted()).GetConfiguration(MachineRequest(), Org, Project, DefaultView, key);

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<Configuration>().AnnotationKey.ShouldBe(key);
        var call = recorder.Calls.ShouldHaveSingleItem();
        call.Method.ShouldBe(nameof(IConfigurationService.GetRawConfigurationAsync));
        call.Arg<FullKey>(0).Annotation.ToString().ShouldBe(key);
    }

    [Fact]
    public async Task GetConfigurationResolved_TheRestriction_ReachesTheService()
    {
        var (configuration, recorder) = Recording<IConfigurationService>();
        recorder.Returns(nameof(IConfigurationService.GetResolvedConfigurationAsync), StoredConfiguration(Restriction));

        var result = await CreateConfigurationHttp(configuration, Restricted()).GetConfigurationResolved(MachineRequest(), Org, Project, DefaultView, Restriction);

        result.ShouldBeOfType<OkObjectResult>();
        var call = recorder.Calls.ShouldHaveSingleItem();
        call.Method.ShouldBe(nameof(IConfigurationService.GetResolvedConfigurationAsync));
        call.Arg<FullKey>(0).Annotation.ToString().ShouldBe(Restriction);
    }

    [Fact]
    public async Task GetConfigurations_ByTheRestrictedMachine_IsRefusedBeforeTheService()
    {
        var http = CreateConfigurationHttp(Untouched<IConfigurationService>(), Restricted());

        await ShouldBeRestrictedAsync(() => http.GetConfigurations(MachineRequest(), Org, Project, DefaultView));
    }

    // A machine without a key keeps the whole project, whatever blank value was stored.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetConfigurations_ByAnUnrestrictedMachine_ReachesTheService(string? storedKey)
    {
        var (configuration, recorder) = Recording<IConfigurationService>();
        recorder.Returns(nameof(IConfigurationService.ListConfigurationsAsync), new ConfigurationsResponse { Configurations = [] });

        var result = await CreateConfigurationHttp(configuration, new RoleAccess(AccountRole.Reader, storedKey)).GetConfigurations(MachineRequest(), Org, Project, DefaultView);

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<ConfigurationsResponse>();
        recorder.Calls.ShouldHaveSingleItem().Method.ShouldBe(nameof(IConfigurationService.ListConfigurationsAsync));
    }

    // A stored key that does not parse restricts the machine to nothing.
    [Fact]
    public async Task GetConfiguration_ByAMachineWithAnInvalidStoredKey_IsRefusedBeforeTheService()
    {
        var http = CreateConfigurationHttp(Untouched<IConfigurationService>(), new RoleAccess(AccountRole.Reader, "not a key"));

        await ShouldBeRestrictedAsync(() => http.GetConfiguration(MachineRequest(), Org, Project, DefaultView, Restriction));
    }

    [Fact]
    public async Task GetAnnotations_ByTheRestrictedMachine_IsRefusedBeforeTheService()
    {
        var http = CreateAnnotationsHttp(Untouched<IAnnotationService>());

        await ShouldBeRestrictedAsync(() => http.GetAnnotations(MachineRequest(), Org, Project, DefaultView));
    }

    [Theory]
    [InlineData(Ancestor)]
    [InlineData(Sibling)]
    public async Task GetAnnotation_OutsideTheRestriction_IsRefusedBeforeTheService(string key)
    {
        var http = CreateAnnotationsHttp(Untouched<IAnnotationService>());

        await ShouldBeRestrictedAsync(() => http.GetAnnotation(MachineRequest(), Org, Project, DefaultView, key));
    }

    [Fact]
    public async Task GetAnnotation_TheRestriction_ReachesTheService()
    {
        var (annotations, recorder) = Recording<IAnnotationService>();
        recorder.Returns(nameof(IAnnotationService.GetAnnotationAsync), StoredAnnotation(Restriction));

        var result = await CreateAnnotationsHttp(annotations).GetAnnotation(MachineRequest(), Org, Project, DefaultView, Restriction);

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<Annotation>().AnnotationKey.ShouldBe(Restriction);
        var call = recorder.Calls.ShouldHaveSingleItem();
        call.Method.ShouldBe(nameof(IAnnotationService.GetAnnotationAsync));
        call.Arg<FullKey>(0).Annotation.ToString().ShouldBe(Restriction);
    }

    [Fact]
    public async Task SetAnnotations_WithOneKeyOutsideTheRestriction_IsRefusedBeforeTheService()
    {
        var http = CreateAnnotationsHttp(Untouched<IAnnotationService>());
        List<Annotation> body = [StoredAnnotation(Restriction), StoredAnnotation(Descendant), StoredAnnotation("cnt.northwind.prod")];

        await ShouldBeRestrictedAsync(() => http.SetAnnotations(MachineRequest("POST"), Org, Project, DefaultView, body));
    }

    [Fact]
    public async Task SetAnnotations_EveryKeyInTheRestriction_ReachesTheService()
    {
        var (annotations, recorder) = Recording<IAnnotationService>();
        List<Annotation> body = [StoredAnnotation(Restriction), StoredAnnotation(Descendant)];
        recorder.Returns(nameof(IAnnotationService.CreateAnnotationsAsync), body.AsEnumerable());

        var result = await CreateAnnotationsHttp(annotations).SetAnnotations(MachineRequest("POST"), Org, Project, DefaultView, body);

        result.ShouldBeOfType<OkObjectResult>();
        var call = recorder.Calls.ShouldHaveSingleItem();
        call.Method.ShouldBe(nameof(IAnnotationService.CreateAnnotationsAsync));
        call.Arg<IEnumerable<Annotation>>(1).Select(annotation => annotation.AnnotationKey).ShouldBe([Restriction, Descendant]);
    }

    // The service creates the missing ancestors too; the restricted machine gets back only what it may address.
    [Fact]
    public async Task SetAnnotations_TheRestriction_LeavesTheCreatedAncestorsOutOfTheAnswer()
    {
        var (annotations, recorder) = Recording<IAnnotationService>();
        recorder.Returns(nameof(IAnnotationService.CreateAnnotationsAsync), CreatedWithTheAncestors());
        List<Annotation> body = [StoredAnnotation(Descendant)];

        var result = await CreateAnnotationsHttp(annotations).SetAnnotations(MachineRequest("POST"), Org, Project, DefaultView, body);

        var created = result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeAssignableTo<IEnumerable<Annotation>>()!;
        created.Select(annotation => annotation.AnnotationKey).ShouldBe([Restriction, Descendant]);
        recorder.Calls.ShouldHaveSingleItem().Method.ShouldBe(nameof(IAnnotationService.CreateAnnotationsAsync));
    }

    [Fact]
    public async Task SetAnnotations_ByAnUnrestrictedMachine_AnswersEveryCreatedAnnotation()
    {
        var (annotations, recorder) = Recording<IAnnotationService>();
        recorder.Returns(nameof(IAnnotationService.CreateAnnotationsAsync), CreatedWithTheAncestors());
        var http = new AnnotationsHttp(annotations, new RoleAccess(AccountRole.Contributor), TimeProvider.System, NullLogger<AnnotationsHttp>.Instance);
        List<Annotation> body = [StoredAnnotation(Descendant)];

        var result = await http.SetAnnotations(MachineRequest("POST"), Org, Project, DefaultView, body);

        var created = result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeAssignableTo<IEnumerable<Annotation>>()!;
        created.Select(annotation => annotation.AnnotationKey).ShouldBe([Ancestor, Parent, Restriction, Descendant]);
    }

    [Fact]
    public async Task FindDeletedAnnotations_WithAKeyOutsideTheRestriction_IsRefusedBeforeTheService()
    {
        var http = CreateAnnotationsHttp(Untouched<IAnnotationService>());
        var model = new AnnotationKeysRequest { Keys = [Descendant, Sibling] };

        await ShouldBeRestrictedAsync(() => http.FindDeletedAnnotations(MachineRequest("POST"), model, Org, Project, DefaultView));
    }

    [Fact]
    public async Task FindDeletedAnnotations_TheRestrictionAndADescendant_ReachTheService()
    {
        var (annotations, recorder) = Recording<IAnnotationService>();
        recorder.Returns(nameof(IAnnotationService.FindDeletedAnnotationsAsync), (IReadOnlyList<Annotation>)[StoredAnnotation(Descendant)]);
        var model = new AnnotationKeysRequest { Keys = [Restriction, Descendant] };

        var result = await CreateAnnotationsHttp(annotations).FindDeletedAnnotations(MachineRequest("POST"), model, Org, Project, DefaultView);

        var deleted = result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeAssignableTo<IReadOnlyList<Annotation>>()!;
        deleted.ShouldHaveSingleItem().AnnotationKey.ShouldBe(Descendant);
        var call = recorder.Calls.ShouldHaveSingleItem();
        call.Method.ShouldBe(nameof(IAnnotationService.FindDeletedAnnotationsAsync));
        call.Args[..3].ShouldBe([Org, Project, DefaultView]);
        call.Arg<IReadOnlyCollection<AnnotationKey>>(3).Select(key => key.ToString()).ShouldBe([Restriction, Descendant]);
    }

    [Fact]
    public async Task RestoreAnnotations_WithAKeyOutsideTheRestriction_IsRefusedBeforeTheService()
    {
        var http = CreateAnnotationsHttp(Untouched<IAnnotationService>());
        var model = new AnnotationRestoreRequest { Keys = [Restriction, Ancestor] };

        await ShouldBeRestrictedAsync(() => http.RestoreAnnotations(MachineRequest("POST"), model, Org, Project, DefaultView));
    }

    // The service restores the deleted ancestors too; the restricted machine gets back only what it may address.
    [Fact]
    public async Task RestoreAnnotations_TheRestriction_LeavesTheRestoredAncestorsOutOfTheAnswer()
    {
        var (annotations, recorder) = Recording<IAnnotationService>();
        recorder.Returns(nameof(IAnnotationService.RestoreAnnotationsAsync), new AnnotationRestoreResult
        {
            Annotations = [StoredAnnotation(Ancestor), StoredAnnotation(Parent), StoredAnnotation(Restriction), StoredAnnotation(Descendant)],
            CreatedKeys = ["cnt.northwind.prod", Descendant],
        });
        var model = new AnnotationRestoreRequest { Keys = [Restriction] };

        var result = await CreateAnnotationsHttp(annotations).RestoreAnnotations(MachineRequest("POST"), model, Org, Project, DefaultView);

        var restored = result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<AnnotationRestoreResult>();
        restored.Annotations.Select(annotation => annotation.AnnotationKey).ShouldBe([Restriction, Descendant]);
        restored.CreatedKeys.ShouldBe([Descendant]);
    }

    [Fact]
    public async Task RestoreAnnotations_ByAnUnrestrictedMachine_AnswersEveryRestoredAnnotation()
    {
        var (annotations, recorder) = Recording<IAnnotationService>();
        recorder.Returns(nameof(IAnnotationService.RestoreAnnotationsAsync), new AnnotationRestoreResult
        {
            Annotations = [StoredAnnotation(Ancestor), StoredAnnotation(Restriction)],
            CreatedKeys = [Ancestor],
        });
        var http = new AnnotationsHttp(annotations, new RoleAccess(AccountRole.Contributor), TimeProvider.System, NullLogger<AnnotationsHttp>.Instance);
        var model = new AnnotationRestoreRequest { Keys = [Restriction] };

        var result = await http.RestoreAnnotations(MachineRequest("POST"), model, Org, Project, DefaultView);

        var restored = result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<AnnotationRestoreResult>();
        restored.Annotations.Select(annotation => annotation.AnnotationKey).ShouldBe([Ancestor, Restriction]);
        restored.CreatedKeys.ShouldBe([Ancestor]);
    }

    [Fact]
    public async Task GetConfigurationSchema_OfAType_IsRefusedBeforeTheService()
    {
        var http = CreateSchemaHttp(Untouched<IConfigurationSchemaService>());

        await ShouldBeRestrictedAsync(() => http.GetConfigurationSchema(MachineRequest(), Org, Project, DefaultView, "exe"));
    }

    [Fact]
    public async Task GetAnnotationSchema_OfAnAncestor_IsRefusedBeforeTheService()
    {
        var http = CreateSchemaHttp(Untouched<IConfigurationSchemaService>());

        await ShouldBeRestrictedAsync(() => http.GetAnnotationSchema(MachineRequest(), Org, Project, DefaultView, Ancestor));
    }

    [Fact]
    public async Task GetAnnotationSchema_TheRestriction_ReachesTheSchemaService()
    {
        var (schemas, recorder) = Recording<IConfigurationSchemaService>();
        recorder.Returns(nameof(IConfigurationSchemaService.GetAnnotationSchemaAsync), new ConfigurationSchema
        {
            View = DefaultView,
            AnnotationKey = Restriction,
            Version = 1,
            Content = new JObject(),
            Modified = Stamp,
        });

        var result = await CreateSchemaHttp(schemas).GetAnnotationSchema(MachineRequest(), Org, Project, DefaultView, Restriction);

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<ConfigurationSchema>().AnnotationKey.ShouldBe(Restriction);
        var call = recorder.Calls.ShouldHaveSingleItem();
        call.Method.ShouldBe(nameof(IConfigurationSchemaService.GetAnnotationSchemaAsync));
        call.Args.ShouldBe([Org, Project, DefaultView, Restriction]);
    }

    [Fact]
    public async Task GetKeyValues_ByTheRestrictedMachine_IsRefusedBeforeTheService()
    {
        var http = new KeyValuesHttp(Untouched<IKeyValueService>(), Restricted(), new FixedKeyValuePolicies(), NullLogger<KeyValuesHttp>.Instance);

        await ShouldBeRestrictedAsync(() => http.GetKeyValues(MachineRequest(), Org, Project, DefaultView));
    }

    // The views and the binding catalog hold no annotation data.
    [Fact]
    public async Task GetViews_WithoutDiscovery_ReachesTheViewService()
    {
        var (views, recorder) = Recording<IViewService>();
        recorder.Returns(nameof(IViewService.GetViewsAsync), (IReadOnlyList<View>)[new View { Name = DefaultView, IsDefault = true }]);

        var result = await new ViewsHttp(views, Restricted()).GetViews(MachineRequest(), Org, Project, discover: false);

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeAssignableTo<IReadOnlyList<View>>()!.ShouldHaveSingleItem().Name.ShouldBe(DefaultView);
        var call = recorder.Calls.ShouldHaveSingleItem();
        call.Method.ShouldBe(nameof(IViewService.GetViewsAsync));
        call.Args.ShouldBe([Org, Project, false]);
    }

    [Fact]
    public async Task GetBindingCatalog_ReachesTheCatalog()
    {
        var (catalog, recorder) = Recording<IBindingCatalog>();
        recorder.Returns(nameof(IBindingCatalog.GetCatalog), new BindingCatalog());

        var result = await new BindingsHttp(Restricted(), catalog).GetBindingCatalog(MachineRequest(), Org, Project);

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<BindingCatalog>();
        recorder.Calls.ShouldHaveSingleItem().Method.ShouldBe(nameof(IBindingCatalog.GetCatalog));
    }

    private static async Task ShouldBeRestrictedAsync(Func<Task> act)
    {
        var exception = await Should.ThrowAsync<AccessDeniedException>(act);
        exception.ErrorCode.ShouldBe(ErrorCodes.AnnotationRestricted);
    }

    // The scopes pass the scope checks of a Release build; the restriction comes from the machine document.
    private static TestHttpRequestData MachineRequest(string method = "GET", string? body = null)
    {
        var (context, request) = FunctionTestDoubles.CreateRequest(new ServiceCollection().BuildServiceProvider(), method: method, body: body);
        context.Items[FunctionContextItemKeys.CachedClaims] = new List<Claim>
        {
            new("sub", "machine-1"),
            new("roles", Scopes.Default.Read),
            new("roles", Scopes.Default.Write),
        };
        context.Items[FunctionContextItemKeys.MachineIdentity] = "machine-1";
        return request;
    }

    private static RoleAccess Restricted()
    {
        return new RoleAccess(AccountRole.Reader, Restriction);
    }

    private static ConfigurationHttp CreateConfigurationHttp(IConfigurationService configuration, IAccessService access)
    {
        return new ConfigurationHttp(
            configuration,
            Untouched<IAnnotationService>(),
            access,
            new FixedKeyValuePolicies(requireMtls: false),
            NullLogger<ConfigurationHttp>.Instance);
    }

    private static AnnotationsHttp CreateAnnotationsHttp(IAnnotationService annotations)
    {
        return new AnnotationsHttp(annotations, Restricted(), TimeProvider.System, NullLogger<AnnotationsHttp>.Instance);
    }

    private static ConfigurationSchemaHttp CreateSchemaHttp(IConfigurationSchemaService schemas)
    {
        return new ConfigurationSchemaHttp(schemas, Restricted(), NullLogger<ConfigurationSchemaHttp>.Instance);
    }

    private static Configuration StoredConfiguration(string key)
    {
        return new Configuration { AnnotationKey = key, Version = 1, Content = new JObject(), Modified = Stamp };
    }

    private static Annotation StoredAnnotation(string key)
    {
        var annotationKey = AnnotationKey.Parse(key);
        return new Annotation
        {
            ProjectName = Project,
            ViewName = DefaultView,
            AnnotationKey = key,
            Name = annotationKey.Name,
            AnnotationType = annotationKey.Type,
            Created = Stamp,
            Modified = Stamp,
        };
    }

    // What a create below the restriction answers when the ancestors were missing: they come first.
    private static List<Annotation> CreatedWithTheAncestors()
    {
        return [StoredAnnotation(Ancestor), StoredAnnotation(Parent), StoredAnnotation(Restriction), StoredAnnotation(Descendant)];
    }

    // The machine must be refused before the service is reached.
    private static T Untouched<T>()
        where T : class
    {
        return DispatchProxy.Create<T, UntouchedProxy>();
    }

    private static (T Service, RecordingProxy Recorder) Recording<T>()
        where T : class
    {
        var service = DispatchProxy.Create<T, RecordingProxy>();
        return (service, (RecordingProxy)(object)service);
    }

    public sealed record RecordedCall(string Method, object?[] Args)
    {
        public T Arg<T>(int index) => (T)Args[index]!;
    }

    public class UntouchedProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            throw new InvalidOperationException($"{targetMethod?.Name} must not be called.");
        }
    }

    // Records every call and answers with the result given for the method, or with null.
    public class RecordingProxy : DispatchProxy
    {
        private readonly Dictionary<string, object?> _results = [];

        public List<RecordedCall> Calls { get; } = [];

        public void Returns(string method, object? result)
        {
            _results[method] = result;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var method = targetMethod ?? throw new ArgumentNullException(nameof(targetMethod));
            Calls.Add(new RecordedCall(method.Name, args ?? []));
            _results.TryGetValue(method.Name, out var result);
            return Completed(method.ReturnType, result);
        }

        private static object? Completed(Type returnType, object? result)
        {
            if (returnType == typeof(Task))
            {
                return Task.CompletedTask;
            }

            if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                return typeof(Task)
                    .GetMethod(nameof(Task.FromResult))!
                    .MakeGenericMethod(returnType.GetGenericArguments()[0])
                    .Invoke(null, [result]);
            }

            return result;
        }
    }
}

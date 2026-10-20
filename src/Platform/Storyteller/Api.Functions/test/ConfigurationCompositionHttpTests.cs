using System.Reflection;
using System.Text.Json;

using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Api.ConditionalReads;
using _42.Platform.Storyteller.Api.Models;
using _42.Platform.Storyteller.Api.Security;
using _42.Platform.Storyteller.Api.V1;
using _42.Platform.Storyteller.Configuring;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Net.Http.Headers;

using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// The composition read: a conditional read with its own tag, the 400 and 404 answers, and the gate of the whole project.
// MachineRestrictionHttpTests.Handlers.cs refuses the machine restricted to an annotation with the project-wide handlers.
public class ConfigurationCompositionHttpTests
{
    private const string Org = "acme";
    private const string Project = "billing";
    private const string View = "default";
    private const string Key = "exe.northwind.invoicing.prod";
    private const string Usage = "usg.northwind.invoicing";

    // The item key of the ASP.NET Core integration, where FunctionContext.GetHttpContext() looks.
    private const string HttpContextItem = "HttpRequestContext";

    private static readonly FullKey FullKeyOfTheRead = FullKey.Create(AnnotationKey.Parse(Key), Org, Project, View);

    private static readonly AuditStamp Stamp = AuditStamp.Of(AuditActor.System, DateTimeOffset.UnixEpoch);

    // Debug builds (DEV_AUTH) skip the role check of users.
    private static bool DevAuth => HttpRequestDataExtensions.IsDevelopmentAuthentication;

    [Fact]
    public async Task GetConfigurationComposition_AnswersTheCompositionWithAWeakTagAndAsksToRevalidate()
    {
        var composition = Composed();
        var (http, configurations, _) = Create(() => composition);
        var request = Read();

        var result = await http.GetConfigurationComposition(request, Org, Project, View, Key);

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeSameAs(composition);
        Headers(request).ETag.ToString().ShouldMatch("^W/\"c\\.[A-Za-z0-9_-]{22}\"$");
        Headers(request).CacheControl.ToString().ShouldBe("private, no-cache");
        configurations.Keys.ShouldHaveSingleItem().ToString().ShouldBe(FullKeyOfTheRead.ToString());
    }

    [Theory]
    [InlineData("{0}")]
    [InlineData("{1}")]
    [InlineData("\"other\", {0}")]
    [InlineData("*")]
    public async Task GetConfigurationComposition_IfNoneMatchListsTheTag_IsNotModified(string format)
    {
        var (http, _, _) = Create(() => Composed());
        var tag = await TagOfAsync(http);
        var request = Read(string.Format(format, tag, tag.Replace("W/", string.Empty, StringComparison.Ordinal)));

        var result = await http.GetConfigurationComposition(request, Org, Project, View, Key);

        result.ShouldBeOfType<StatusCodeResult>().StatusCode.ShouldBe(StatusCodes.Status304NotModified);
        Headers(request).ETag.ToString().ShouldBe(tag);
        Headers(request).CacheControl.ToString().ShouldBe("private, no-cache");
    }

    // A source of a new version that brings the same values: Content and Hash stay, the tag does not.
    [Fact]
    public async Task GetConfigurationComposition_ANewVersionOfASourceWithTheSameContent_GetsAnotherTag()
    {
        var composition = Composed();
        var (http, _, _) = Create(() => composition);
        var first = await TagOfAsync(http);

        composition = Composed(usageVersion: 3);
        var request = Read(first);
        var result = await http.GetConfigurationComposition(request, Org, Project, View, Key);

        var answered = result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<ConfigurationComposition>();
        answered.Content.ToString().ShouldBe(Composed().Content.ToString());
        answered.Hash.ShouldBe(Composed().Hash);
        Headers(request).ETag.ToString().ShouldNotBe(first);
    }

    [Fact]
    public void Tags_OfTheComposition_FollowTheSourcesTheOriginsAndTheKey()
    {
        var tag = ConfigurationEntityTags.ForComposition(FullKeyOfTheRead, Composed()).ToString();

        tag.ShouldStartWith("W/\"c.");
        ConfigurationEntityTags.ForComposition(FullKeyOfTheRead, Composed()).ToString().ShouldBe(tag);
        ConfigurationEntityTags.ForComposition(FullKeyOfTheRead, Composed(usageVersion: 3)).ToString().ShouldNotBe(tag);
        ConfigurationEntityTags.ForComposition(FullKeyOfTheRead, Composed(modeStep: 1)).ToString().ShouldNotBe(tag);
        ConfigurationEntityTags.ForComposition(FullKeyOfTheRead, Composed(plan: "free")).ToString().ShouldNotBe(tag);
        ConfigurationEntityTags.ForComposition(FullKey.Create(AnnotationKey.Parse(Key), Org, Project, "staging"), Composed()).ToString().ShouldNotBe(tag);
        ConfigurationEntityTags.ForComposition(FullKey.Create(AnnotationKey.Parse("exe.northwind.invoicing.dev"), Org, Project, View), Composed()).ToString().ShouldNotBe(tag);
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("exe")]
    public async Task GetConfigurationComposition_AnInvalidKey_IsBadRequestWithoutTheService(string key)
    {
        var (http, configurations, _) = Create(() => Composed());
        var request = Read();

        var result = await http.GetConfigurationComposition(request, Org, Project, View, key);

        result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>().Message.ShouldBe($"Invalid annotation key: {key}");
        configurations.Keys.ShouldBeEmpty();
        Headers(request).ContainsKey(HeaderNames.ETag).ShouldBeFalse();
    }

    [Fact]
    public async Task GetConfigurationComposition_Missing_IsNotFoundWithoutATag()
    {
        var (http, _, annotations) = Create(() => null);
        var request = Read("*");

        var result = await http.GetConfigurationComposition(request, Org, Project, View, Key);

        result.ShouldBeOfType<NotFoundResult>();
        annotations.Keys.ShouldHaveSingleItem().ToString().ShouldBe(FullKeyOfTheRead.ToString());
        Headers(request).ContainsKey(HeaderNames.ETag).ShouldBeFalse();
        Headers(request).ContainsKey(HeaderNames.CacheControl).ShouldBeFalse();
    }

    [Fact]
    public async Task GetConfigurationComposition_ADeletedAnnotation_IsNotFoundWithAnnotationDeleted()
    {
        var (http, _, annotations) = Create(() => null);
        annotations.Deleted = new Annotation
        {
            ProjectName = Project,
            ViewName = View,
            AnnotationKey = Key,
            Name = "prod",
            AnnotationType = AnnotationType.Execution,
        };
        var request = Read();

        var result = await http.GetConfigurationComposition(request, Org, Project, View, Key);

        var error = result.ShouldBeOfType<NotFoundObjectResult>().Value.ShouldBeOfType<ErrorResponse>();
        error.ErrorCode.ShouldBe(ErrorCodes.AnnotationDeleted);
        error.Message.ShouldContain(Key);
        Headers(request).ContainsKey(HeaderNames.ETag).ShouldBeFalse();
    }

    // The gate is the whole project: a machine without an annotation key reads the composition of any annotation.
    [Fact]
    public async Task GetConfigurationComposition_ByAnUnrestrictedMachine_ReachesTheService()
    {
        var (http, configurations, _) = Create(() => Composed());
        var request = WithHttpContext(KeyValueRequests.Machine(mtls: false), ifNoneMatch: null);

        var result = await http.GetConfigurationComposition(request, Org, Project, View, Key);

        result.ShouldBeOfType<OkObjectResult>();
        configurations.Keys.ShouldHaveSingleItem();
    }

    // Users are not restricted to annotations; the Reader role of the project lets them in, as for the effective read.
    [Fact]
    public async Task GetConfigurationComposition_ByAUserWithoutARole_IsDeniedBeforeTheService()
    {
        if (DevAuth)
        {
            return;
        }

        var (http, configurations, _) = Create(() => Composed(), AccountRole.None);

        var exception = await Should.ThrowAsync<AccessDeniedException>(() => http.GetConfigurationComposition(Read(), Org, Project, View, Key));

        exception.ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
        configurations.Keys.ShouldBeEmpty();
    }

    // The API writes with System.Text.Json: the enums by name, the documents as objects, and no null members.
    [Fact]
    public void Composition_AsTheApiJson_HasTheEnumNamesAndTheDocuments()
    {
        using var document = JsonDocument.Parse(FunctionTestDoubles.ToJsonBody(Composed()));
        var root = document.RootElement;

        root.GetProperty("Content").GetProperty("plan").GetString().ShouldBe("enterprise");
        root.GetProperty("Sources")[0].GetProperty("Content").GetProperty("plan").GetString().ShouldBe("enterprise");
        root.GetProperty("Sources")[1].GetProperty("Kind").GetString().ShouldBe("Template");
        root.GetProperty("Sources")[1].TryGetProperty("AnnotationKey", out _).ShouldBeFalse();
        root.GetProperty("Sources")[2].GetProperty("AnnotationType").GetString().ShouldBe("Execution");
        root.GetProperty("Sources")[2].GetProperty("Presence").GetString().ShouldBe("Missing");
        root.GetProperty("Sources")[2].TryGetProperty("Content", out _).ShouldBeFalse();
        root.GetProperty("Sources")[2].TryGetProperty("Modified", out _).ShouldBeFalse();
        root.GetProperty("Nodes")[0].GetProperty("MergedInto").GetInt32().ShouldBe(1);
        root.GetProperty("Nodes")[1].TryGetProperty("MergedInto", out _).ShouldBeFalse();
        root.GetProperty("Steps")[1].GetProperty("SourceId").GetString().ShouldBe("template:exe");
        root.GetProperty("Origins")[1].GetProperty("Path").GetString().ShouldBe("/mode");
        root.GetProperty("Origins")[1].GetProperty("Step").GetInt32().ShouldBe(2);
    }

    private static (ConfigurationHttp Http, CompositionProxy Configurations, DeletedAnnotationProxy Annotations) Create(
        Func<ConfigurationComposition?> composed,
        AccountRole role = AccountRole.Reader)
    {
        var configurations = DispatchProxy.Create<IConfigurationService, CompositionProxy>();
        var annotations = DispatchProxy.Create<IAnnotationService, DeletedAnnotationProxy>();
        var proxy = (CompositionProxy)(object)configurations;
        proxy.Composed = composed;
        var http = new ConfigurationHttp(
            configurations,
            annotations,
            new RoleAccess(role),
            new FixedKeyValuePolicies(requireMtls: true),
            NullLogger<ConfigurationHttp>.Instance);
        return (http, proxy, (DeletedAnnotationProxy)(object)annotations);
    }

    private static async Task<string> TagOfAsync(ConfigurationHttp http)
    {
        var request = Read();
        await http.GetConfigurationComposition(request, Org, Project, View, Key);
        return Headers(request).ETag.ToString();
    }

    // A user with the Default scopes, which pass the scope check of a Release build.
    private static TestHttpRequestData Read(string? ifNoneMatch = null)
    {
        return WithHttpContext(KeyValueRequests.User(), ifNoneMatch);
    }

    private static TestHttpRequestData WithHttpContext(TestHttpRequestData request, string? ifNoneMatch)
    {
        request.FunctionContext.Items[HttpContextItem] = new DefaultHttpContext();

        if (ifNoneMatch is not null)
        {
            request.Headers.TryAddWithoutValidation(HeaderNames.IfNoneMatch, ifNoneMatch);
        }

        return request;
    }

    private static IHeaderDictionary Headers(TestHttpRequestData request)
    {
        return ((HttpContext)request.FunctionContext.Items[HttpContextItem]).Response.Headers;
    }

    // The usage's configuration (node 0), then the execution's template and its own configuration, which is missing
    // (node 1, the target). The usage sets /plan in step 1, the template /mode in step 2.
    private static ConfigurationComposition Composed(ulong usageVersion = 2, int modeStep = 2, string plan = "enterprise")
    {
        return new ConfigurationComposition
        {
            AnnotationKey = Key,
            Content = new JObject { ["plan"] = plan, ["mode"] = "batch" },
            Hash = "0badc0de",
            Sources =
            [
                new ConfigurationCompositionSource
                {
                    Id = $"configuration:{Usage}",
                    Kind = ConfigurationCompositionSourceKind.Configuration,
                    AnnotationType = AnnotationType.Usage,
                    AnnotationKey = Usage,
                    Presence = ConfigurationCompositionSourcePresence.Present,
                    Version = usageVersion,
                    Modified = Stamp,
                    Content = new JObject { ["plan"] = plan },
                },
                new ConfigurationCompositionSource
                {
                    Id = "template:exe",
                    Kind = ConfigurationCompositionSourceKind.Template,
                    AnnotationType = AnnotationType.Execution,
                    Presence = ConfigurationCompositionSourcePresence.Present,
                    Version = 1,
                    Modified = Stamp,
                    Content = new JObject { ["mode"] = "batch" },
                },
                new ConfigurationCompositionSource
                {
                    Id = $"configuration:{Key}",
                    Kind = ConfigurationCompositionSourceKind.Configuration,
                    AnnotationType = AnnotationType.Execution,
                    AnnotationKey = Key,
                    Presence = ConfigurationCompositionSourcePresence.Missing,
                    Version = 0,
                },
            ],
            Nodes =
            [
                new ConfigurationCompositionNode { Index = 0, AnnotationKey = Usage, MergedInto = 1, Depth = 1 },
                new ConfigurationCompositionNode { Index = 1, AnnotationKey = Key, Depth = 0 },
            ],
            Steps =
            [
                new ConfigurationCompositionStep { Order = 1, SourceId = $"configuration:{Usage}", Node = 0 },
                new ConfigurationCompositionStep { Order = 2, SourceId = "template:exe", Node = 1 },
                new ConfigurationCompositionStep { Order = 3, SourceId = $"configuration:{Key}", Node = 1 },
            ],
            Origins =
            [
                new ConfigurationCompositionOrigin { Path = "/plan", Step = 1 },
                new ConfigurationCompositionOrigin { Path = "/mode", Step = modeStep },
            ],
        };
    }

    // The composition read; every other member fails the test.
    public class CompositionProxy : DispatchProxy
    {
        public List<FullKey> Keys { get; } = [];

        public Func<ConfigurationComposition?> Composed { get; set; } = () => null;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name != nameof(IConfigurationService.GetConfigurationCompositionAsync))
            {
                throw new InvalidOperationException($"{targetMethod?.Name} must not be called.");
            }

            Keys.Add((FullKey)args![0]!);
            return Task.FromResult(Composed());
        }
    }

    // The deleted annotation that a 404 looks for, none by default; every other member fails the test.
    public class DeletedAnnotationProxy : DispatchProxy
    {
        public List<FullKey> Keys { get; } = [];

        public Annotation? Deleted { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name != nameof(IAnnotationService.GetDeletedAnnotationAsync))
            {
                throw new InvalidOperationException($"{targetMethod?.Name} must not be called.");
            }

            Keys.Add((FullKey)args![0]!);
            return Task.FromResult(Deleted);
        }
    }
}

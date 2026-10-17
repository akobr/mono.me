using System.Reflection;
using System.Text.Json;

using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Api.ConditionalReads;
using _42.Platform.Storyteller.Api.Security;
using _42.Platform.Storyteller.Api.V1;
using _42.Platform.Storyteller.Binding;
using _42.Platform.Storyteller.Configuring;
using _42.Platform.Storyteller.KeyValues;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// ETag and If-None-Match on the effective and resolved configuration reads.
public class ConfigurationConditionalReadTests
{
    private const string Org = "acme";
    private const string Project = "billing";
    private const string View = "default";
    private const string Key = "unt.invoicing.sender";

    // The item key of the ASP.NET Core integration, where FunctionContext.GetHttpContext() looks.
    private const string HttpContextItem = "HttpRequestContext";

    private static readonly FullKey FullKeyOfTheRead = FullKey.Create(AnnotationKey.Parse(Key), Org, Project, View);

    [Fact]
    public async Task GetConfiguration_CarriesAWeakTagAndAsksToRevalidate()
    {
        var (http, _) = Create(() => Stored());
        var request = Read();

        var result = await http.GetConfiguration(request, Org, Project, View, Key);

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<Configuration>().Version.ShouldBe(3UL);
        var headers = Headers(request);
        headers.ETag.ToString().ShouldMatch("^W/\"e\\.[A-Za-z0-9_-]{22}\"$");
        headers.CacheControl.ToString().ShouldBe("private, no-cache");
    }

    [Theory]
    [InlineData("{0}")]
    [InlineData("{1}")]
    [InlineData("\"other\", {0}")]
    [InlineData("W/\"other\" , {1}")]
    [InlineData("*")]
    public async Task GetConfiguration_IfNoneMatchListsTheTag_IsNotModified(string format)
    {
        var (http, _) = Create(() => Stored());
        var tag = await TagOfAsync(http);
        var request = Read(string.Format(format, tag, tag.Replace("W/", string.Empty, StringComparison.Ordinal)));

        var result = await http.GetConfiguration(request, Org, Project, View, Key);

        result.ShouldBeOfType<StatusCodeResult>().StatusCode.ShouldBe(StatusCodes.Status304NotModified);
        Headers(request).ETag.ToString().ShouldBe(tag);
        Headers(request).CacheControl.ToString().ShouldBe("private, no-cache");
    }

    [Fact]
    public async Task GetConfiguration_TheTagOnAnotherHeaderLine_IsNotModified()
    {
        var (http, _) = Create(() => Stored());
        var tag = await TagOfAsync(http);
        var request = Read();
        request.Headers.TryAddWithoutValidation(HeaderNames.IfNoneMatch, ["W/\"e.other\"", tag]);

        var result = await http.GetConfiguration(request, Org, Project, View, Key);

        result.ShouldBeOfType<StatusCodeResult>().StatusCode.ShouldBe(StatusCodes.Status304NotModified);
    }

    [Theory]
    [InlineData("W/\"e.other\"")]
    [InlineData("junk {0}")]
    [InlineData("{0} junk")]
    [InlineData("unquoted")]
    [InlineData("")]
    public async Task GetConfiguration_AnotherOrAnInvalidIfNoneMatch_IsOk(string format)
    {
        var (http, _) = Create(() => Stored());
        var tag = await TagOfAsync(http);
        var request = Read();
        request.Headers.TryAddWithoutValidation(HeaderNames.IfNoneMatch, string.Format(format, tag));

        var result = await http.GetConfiguration(request, Org, Project, View, Key);

        result.ShouldBeOfType<OkObjectResult>();
        Headers(request).ETag.ToString().ShouldBe(tag);
    }

    [Fact]
    public async Task GetConfiguration_AChangedBody_GetsAnotherTag()
    {
        var stored = Stored();
        var (http, _) = Create(() => stored);
        var first = await TagOfAsync(http);

        stored = Stored(version: 4);
        var request = Read(first);
        var result = await http.GetConfiguration(request, Org, Project, View, Key);

        result.ShouldBeOfType<OkObjectResult>();
        Headers(request).ETag.ToString().ShouldNotBe(first);
    }

    [Fact]
    public async Task GetConfiguration_Missing_IsNotFoundWithoutATag()
    {
        var (http, _) = Create(() => null);
        var request = Read("*");

        var result = await http.GetConfiguration(request, Org, Project, View, Key);

        result.ShouldBeOfType<NotFoundResult>();
        Headers(request).ContainsKey(HeaderNames.ETag).ShouldBeFalse();
        Headers(request).ContainsKey(HeaderNames.CacheControl).ShouldBeFalse();
    }

    [Fact]
    public async Task GetConfigurationResolved_WithoutBindings_IsAnsweredWithoutTheResolver()
    {
        var (http, configurations) = Create(() => Stored());
        var effectiveTag = await TagOfAsync(http);
        configurations.Calls.Clear();
        var request = Machine(mtls: true);

        var result = await http.GetConfigurationResolved(request, Org, Project, View, Key);

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<Configuration>().Content["sender"]!.Value<string>().ShouldBe("billing@example.com");
        configurations.Calls.ShouldBe([nameof(IConfigurationService.GetRawConfigurationAsync), nameof(IConfigurationService.HasBindings)]);
        var tag = Headers(request).ETag.ToString();
        tag.ShouldStartWith("W/\"r.");
        tag.ShouldNotBe(effectiveTag);
    }

    [Fact]
    public async Task GetConfigurationResolved_WithoutBindings_TheSameTag_IsNotModifiedWithTheSameHeaders()
    {
        var (http, configurations) = Create(() => Stored());
        var first = Machine(mtls: true);
        await http.GetConfigurationResolved(first, Org, Project, View, Key);
        var tag = Headers(first).ETag.ToString();
        var second = Machine(mtls: true, ifNoneMatch: tag);

        var result = await http.GetConfigurationResolved(second, Org, Project, View, Key);

        result.ShouldBeOfType<StatusCodeResult>().StatusCode.ShouldBe(StatusCodes.Status304NotModified);
        Headers(second).ETag.ToString().ShouldBe(tag);
        Headers(second).CacheControl.ToString().ShouldBe("no-store");
        configurations.Calls.ShouldNotContain(nameof(IConfigurationService.ResolveConfigurationAsync));
    }

    [Fact]
    public async Task GetConfigurationResolved_WithheldSecrets_RepeatTheWithheldHeaderOnTheNotModified()
    {
        var (http, _) = Create(() => Stored());
        var first = Machine(mtls: false);
        await http.GetConfigurationResolved(first, Org, Project, View, Key);
        var tag = Headers(first).ETag.ToString();
        var second = Machine(mtls: false, ifNoneMatch: tag);

        var result = await http.GetConfigurationResolved(second, Org, Project, View, Key);

        result.ShouldBeOfType<StatusCodeResult>().StatusCode.ShouldBe(StatusCodes.Status304NotModified);
        Headers(second)[SecretRights.WithheldHeader].ToString().ShouldBe(SecretRights.WithheldByMtls);
        Headers(second).CacheControl.ToString().ShouldBe("private, no-cache");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetConfigurationResolved_SecretsReadAndWithheld_NeverShareATag(bool bound)
    {
        var (http, _) = Create(() => Stored(), bound ? Resolve("db-1") : null);
        var reading = Machine(mtls: true);
        var withheld = Machine(mtls: false);

        await http.GetConfigurationResolved(reading, Org, Project, View, Key);
        var result = await http.GetConfigurationResolved(Machine(mtls: false, ifNoneMatch: Headers(reading).ETag.ToString()), Org, Project, View, Key);
        await http.GetConfigurationResolved(withheld, Org, Project, View, Key);

        result.ShouldBeOfType<OkObjectResult>();
        Headers(withheld).ETag.ToString().ShouldNotBe(Headers(reading).ETag.ToString());
    }

    [Fact]
    public async Task GetConfigurationResolved_WithBindings_ResolvesWithTheSecretRightsOfTheCaller()
    {
        var (http, configurations) = Create(() => Stored(), Resolve("db-1"));

        await http.GetConfigurationResolved(Machine(mtls: true), Org, Project, View, Key);
        await http.GetConfigurationResolved(Machine(mtls: false), Org, Project, View, Key);

        configurations.IncludeSecrets.ShouldBe([true, false]);
    }

    [Fact]
    public async Task GetConfigurationResolved_NoSecretRights_IsPrivateWithoutTheWithheldHeader()
    {
        // a DEV_AUTH build gives every caller the secrets scope, so only Release has a caller without secret rights
        if (HttpRequestDataExtensions.IsDevelopmentAuthentication)
        {
            return;
        }

        var (http, _) = Create(() => Stored(), Resolve("db-1"));
        var reading = Machine(mtls: true);
        var neither = WithHttpContext(KeyValueRequests.Machine(mtls: false, roles: [Scopes.Configuration.Read]), ifNoneMatch: null);

        await http.GetConfigurationResolved(reading, Org, Project, View, Key);
        var result = await http.GetConfigurationResolved(neither, Org, Project, View, Key);

        result.ShouldBeOfType<OkObjectResult>();
        Headers(neither).CacheControl.ToString().ShouldBe("private, no-cache");
        Headers(neither).ContainsKey(SecretRights.WithheldHeader).ShouldBeFalse();
        Headers(neither).ETag.ToString().ShouldNotBe(Headers(reading).ETag.ToString());
    }

    [Fact]
    public async Task GetConfigurationResolved_WithBindings_TagsTheResolvedBody()
    {
        var value = "db-1";
        var (http, configurations) = Create(() => Stored(), (configuration, _) =>
        {
            configuration.Content["host"] = value;
            return configuration;
        });
        var first = Machine(mtls: true);
        await http.GetConfigurationResolved(first, Org, Project, View, Key);
        var tag = Headers(first).ETag.ToString();

        var unchanged = Machine(mtls: true, ifNoneMatch: tag);
        var unchangedResult = await http.GetConfigurationResolved(unchanged, Org, Project, View, Key);
        value = "db-2";
        var changed = Machine(mtls: true, ifNoneMatch: tag);
        var changedResult = await http.GetConfigurationResolved(changed, Org, Project, View, Key);

        tag.ShouldStartWith("W/\"b.");
        unchangedResult.ShouldBeOfType<StatusCodeResult>().StatusCode.ShouldBe(StatusCodes.Status304NotModified);
        changedResult.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<Configuration>().Content["host"]!.Value<string>().ShouldBe("db-2");
        Headers(changed).ETag.ToString().ShouldNotBe(tag);
        configurations.Calls.Count(call => call == nameof(IConfigurationService.ResolveConfigurationAsync)).ShouldBe(3, "a document with bindings is resolved for every read");
        configurations.Calls.ShouldNotContain(nameof(IConfigurationService.GetResolvedConfigurationAsync));
    }

    [Fact]
    public async Task GetConfigurationResolved_TagsOverBindings_FollowTheConfiguredKey()
    {
        var (http, _) = Create(() => Stored(), Resolve("db-1"));
        var key = Convert.ToBase64String(Enumerable.Range(1, 32).Select(index => (byte)index).ToArray());
        var otherKey = Convert.ToBase64String(Enumerable.Range(2, 32).Select(index => (byte)index).ToArray());

        var instance = await ResolvedTagAsync(http, key);
        var otherInstance = await ResolvedTagAsync(http, key);
        var otherDeployment = await ResolvedTagAsync(http, otherKey);
        var process = await ResolvedTagAsync(http, entityTagKey: null);

        otherInstance.ShouldBe(instance);
        otherDeployment.ShouldNotBe(instance);
        process.ShouldNotBe(instance);
        (await ResolvedTagAsync(http, entityTagKey: null)).ShouldBe(process, "without a key the process keeps one");
    }

    [Fact]
    public async Task GetConfigurationResolved_ABindingThatFails_SendsNoTag()
    {
        var (http, _) = Create(() => Stored(), (_, _) => throw new BindingEvaluationException("Failed to evaluate '@db.host'."));
        var request = Machine(mtls: true, ifNoneMatch: "*");

        await Should.ThrowAsync<BindingEvaluationException>(() => http.GetConfigurationResolved(request, Org, Project, View, Key));

        Headers(request).ContainsKey(HeaderNames.ETag).ShouldBeFalse();
        Headers(request).ContainsKey(HeaderNames.CacheControl).ShouldBeFalse();
    }

    [Fact]
    public void Tags_FollowEveryFieldOfTheBodyButModified()
    {
        var rights = Rights(canReadSecrets: true, withheld: false);
        var tag = ConfigurationEntityTags.ForEffective(FullKeyOfTheRead, Stored()).ToString();

        ConfigurationEntityTags.ForEffective(FullKeyOfTheRead, Stored()).ToString().ShouldBe(tag);
        ConfigurationEntityTags.ForEffective(FullKeyOfTheRead, Stored(modified: DateTimeOffset.UnixEpoch.AddDays(1))).ToString().ShouldBe(tag);
        ConfigurationEntityTags.ForEffective(FullKeyOfTheRead, Stored(version: 4)).ToString().ShouldNotBe(tag);
        ConfigurationEntityTags.ForEffective(FullKeyOfTheRead, Stored(hash: "feedf00d")).ToString().ShouldNotBe(tag);
        ConfigurationEntityTags.ForEffective(FullKeyOfTheRead, Stored(hash: null)).ToString().ShouldNotBe(tag);
        ConfigurationEntityTags.ForEffective(FullKeyOfTheRead, Stored(sender: "ops@example.com")).ToString().ShouldNotBe(tag);
        ConfigurationEntityTags.ForEffective(FullKey.Create(AnnotationKey.Parse(Key), Org, Project, "staging"), Stored()).ToString().ShouldNotBe(tag);
        ConfigurationEntityTags.ForEffective(FullKey.Create(AnnotationKey.Parse("unt.invoicing.receiver"), Org, Project, View), Stored()).ToString().ShouldNotBe(tag);
        ConfigurationEntityTags.ForResolved(FullKeyOfTheRead, Stored(), rights).ToString().ShouldNotBe(tag);
    }

    [Fact]
    public void Tags_SplitTheThreeSecretRights()
    {
        var secret = new byte[32];
        var reading = Rights(canReadSecrets: true, withheld: false);
        var withheld = Rights(canReadSecrets: false, withheld: true);
        var neither = Rights(canReadSecrets: false, withheld: false);

        string[] resolved =
        [
            ConfigurationEntityTags.ForResolved(FullKeyOfTheRead, Stored(), reading).ToString(),
            ConfigurationEntityTags.ForResolved(FullKeyOfTheRead, Stored(), withheld).ToString(),
            ConfigurationEntityTags.ForResolved(FullKeyOfTheRead, Stored(), neither).ToString(),
        ];
        string[] bound =
        [
            ConfigurationEntityTags.ForResolvedBindings(FullKeyOfTheRead, Stored(), reading, secret).ToString(),
            ConfigurationEntityTags.ForResolvedBindings(FullKeyOfTheRead, Stored(), withheld, secret).ToString(),
            ConfigurationEntityTags.ForResolvedBindings(FullKeyOfTheRead, Stored(), neither, secret).ToString(),
        ];

        resolved.Concat(bound).Distinct().Count().ShouldBe(6);
        bound.ShouldAllBe(tag => tag.StartsWith("W/\"b.", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=", true)]
    [InlineData("AQIDBAUGBwgJCgsMDQ4PEA==", false)]
    [InlineData("not base64!", false)]
    public void AddConditionalReads_ValidatesTheKey(string? key, bool valid)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConditionalReads:EntityTagKey"] = key })
            .Build();
        using var services = new ServiceCollection().AddConditionalReads(configuration).BuildServiceProvider();

        var read = () => services.GetRequiredService<IOptions<ConditionalReadOptions>>().Value;

        if (valid)
        {
            read.ShouldNotThrow();
        }
        else
        {
            var exception = Should.Throw<OptionsValidationException>(read);
            exception.Message.ShouldContain("ConditionalReads:EntityTagKey");
            exception.Message.ShouldNotContain(key!);
        }
    }

    [Fact]
    public async Task AddConditionalReads_AnInvalidKey_StopsTheStart()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConditionalReads:EntityTagKey"] = "AQIDBAUGBwgJCgsMDQ4PEA==" })
            .Build();
        using var host = new HostBuilder().ConfigureServices(services => services.AddConditionalReads(configuration)).Build();

        var exception = await Should.ThrowAsync<OptionsValidationException>(() => host.StartAsync());

        exception.Message.ShouldContain("ConditionalReads:EntityTagKey");
    }

    [Theory]
    [InlineData(Definitions.RouteIds.Configuration.GetConfiguration)]
    [InlineData(Definitions.RouteIds.Configuration.GetResolvedConfiguration)]
    public async Task Document_ConfigurationReads_DeclareTheNotModifiedAndTheTag(string operationId)
    {
        var json = await OpenApiExportTests.RenderAsync();
        using var document = JsonDocument.Parse(json);

        var operation = document.RootElement.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject())
            .Select(entry => entry.Value)
            .Single(entry => entry.GetProperty("operationId").GetString() == operationId);

        var responses = operation.GetProperty("responses");
        responses.GetProperty("200").GetProperty("headers").TryGetProperty(HeaderNames.ETag, out _).ShouldBeTrue();
        responses.GetProperty("304").GetProperty("headers").TryGetProperty(HeaderNames.ETag, out _).ShouldBeTrue();
        responses.GetProperty("304").TryGetProperty("content", out _).ShouldBeFalse();
        operation.GetProperty("description").GetString()!.ShouldContain("If-None-Match");
    }

    private static (ConfigurationHttp Http, ConfigurationProxy Configurations) Create(
        Func<Configuration?> stored,
        Func<Configuration, bool, Configuration>? resolve = null)
    {
        var configurations = DispatchProxy.Create<IConfigurationService, ConfigurationProxy>();
        var proxy = (ConfigurationProxy)(object)configurations;
        proxy.Stored = stored;
        proxy.Resolve = resolve;
        var http = new ConfigurationHttp(
            configurations,
            DispatchProxy.Create<IAnnotationService, NoDeletedAnnotations>(),
            new RoleAccess(AccountRole.Reader),
            new FixedKeyValuePolicies(requireMtls: true),
            NullLogger<ConfigurationHttp>.Instance);
        return (http, proxy);
    }

    private static Func<Configuration, bool, Configuration> Resolve(string host)
    {
        return (configuration, _) =>
        {
            configuration.Content["host"] = host;
            return configuration;
        };
    }

    private static async Task<string> TagOfAsync(ConfigurationHttp http)
    {
        var request = Read();
        await http.GetConfiguration(request, Org, Project, View, Key);
        return Headers(request).ETag.ToString();
    }

    private static async Task<string> ResolvedTagAsync(ConfigurationHttp http, string? entityTagKey)
    {
        var request = Machine(mtls: true);
        var services = new ServiceCollection();

        if (entityTagKey is not null)
        {
            services.AddSingleton(Options.Create(new ConditionalReadOptions { EntityTagKey = entityTagKey }));
        }

        await using var provider = services.BuildServiceProvider();
        request.FunctionContext.InstanceServices = provider;
        await http.GetConfigurationResolved(request, Org, Project, View, Key);
        return Headers(request).ETag.ToString();
    }

    // A user: the effective read needs no secret rights.
    private static TestHttpRequestData Read(string? ifNoneMatch = null)
    {
        return WithHttpContext(KeyValueRequests.User(), ifNoneMatch);
    }

    // Secret rights come from machines, because a DEV_AUTH build gives every user secret rights:
    // with a certificate the machine reads secrets, without one they are withheld (the project requires mTLS).
    private static TestHttpRequestData Machine(bool mtls, string? ifNoneMatch = null)
    {
        return WithHttpContext(KeyValueRequests.Machine(mtls), ifNoneMatch);
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

    private static SecretRights Rights(bool canReadSecrets, bool withheld)
    {
        return new SecretRights(new KeyValueCaller(AuditActor.System, canReadSecrets, canReadSecrets), withheld);
    }

    private static Configuration Stored(ulong version = 3, string? hash = "0badc0de", string sender = "billing@example.com", DateTimeOffset? modified = null)
    {
        return new Configuration
        {
            AnnotationKey = Key,
            Version = version,
            Content = new JObject { ["sender"] = sender, ["host"] = "@db.host" },
            Modified = AuditStamp.Of(AuditActor.System, modified ?? DateTimeOffset.UnixEpoch),
            Hash = hash,
        };
    }

    // The reads of the conditional handlers; every other member fails the test. A proxy follows interface changes by itself.
    public class ConfigurationProxy : DispatchProxy
    {
        public List<string> Calls { get; } = [];

        // includeSecrets of every resolve
        public List<bool> IncludeSecrets { get; } = [];

        public Func<Configuration?> Stored { get; set; } = () => null;

        // null: the document has no bindings
        public Func<Configuration, bool, Configuration>? Resolve { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var name = targetMethod?.Name ?? string.Empty;
            Calls.Add(name);

            return name switch
            {
                nameof(IConfigurationService.GetRawConfigurationAsync) => Task.FromResult(Stored()),
                nameof(IConfigurationService.HasBindings) => Resolve is not null,
                nameof(IConfigurationService.ResolveConfigurationAsync) => ResolveAsync((Configuration)args![1]!, (bool)args[2]!),
                _ => throw new InvalidOperationException($"{name} must not be called."),
            };
        }

        private Task<Configuration> ResolveAsync(Configuration effective, bool includeSecrets)
        {
            IncludeSecrets.Add(includeSecrets);

            try
            {
                return Task.FromResult(Resolve!(effective, includeSecrets));
            }
            catch (Exception exception)
            {
                return Task.FromException<Configuration>(exception);
            }
        }
    }

    // No annotation is deleted, so a missing configuration is a plain 404.
    public class NoDeletedAnnotations : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return targetMethod?.Name == nameof(IAnnotationService.GetDeletedAnnotationAsync)
                ? Task.FromResult<Annotation?>(null)
                : throw new InvalidOperationException($"{targetMethod?.Name} must not be called.");
        }
    }
}

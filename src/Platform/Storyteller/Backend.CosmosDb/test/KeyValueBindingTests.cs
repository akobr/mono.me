#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Binding;
using _42.Platform.Storyteller.Binding.Language;
using _42.Platform.Storyteller.Configuring;
using _42.Platform.Storyteller.KeyValues;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// The native key/value store as the default, public and secret binding sources of resolved configurations.
public class KeyValueBindingTests(Startup startup)
    : BaseTestsClass(startup)
{
    private const string Org = TestConstants.Organization;
    private const string View = Constants.DefaultViewName;

    private static readonly KeyValueCaller Admin = KeyValueCaller.WithSecrets("account: admin");

    private IKeyValueService KeyValues => Context.Services.GetRequiredService<IKeyValueService>();

    private CountingKeyValueService Counting => Context.Services.GetRequiredService<CountingKeyValueService>();

    private IConfigurationService Configs => Context.Services.GetRequiredService<IConfigurationService>();

    private IAnnotationService Annotations => Context.Services.GetRequiredService<IAnnotationService>();

    private FakeKeyEncryptionProvider KeyProvider => Context.Services.GetRequiredService<FakeKeyEncryptionProvider>();

    [Fact]
    public async Task ResolvedConfiguration_BindsPairsOfItsView()
    {
        var project = $"kvb-{Guid.NewGuid():N}"[..30];
        await KeyValues.SetAsync(Org, project, View, "db.host", new KeyValueWrite { Protection = KeyValueProtection.Public, Value = "sql.internal" }, Admin);
        await KeyValues.SetAsync(Org, project, View, "db.password", new KeyValueWrite { Protection = KeyValueProtection.Standard, Value = "hunter2" }, Admin);
        await KeyValues.SetAsync(Org, project, View, "api.host", new KeyValueWrite { Protection = KeyValueProtection.Public, Value = "api.internal" }, Admin);
        await KeyValues.SetAsync(Org, project, "rollout", "only.rollout", new KeyValueWrite { Protection = KeyValueProtection.Public, Value = "r" }, Admin);

        var key = await CreateConfigurationAsync(project, "bindings", """
            {
                "host": "@db.host",
                "hostAgain": "@DB.HOST",
                "password": "@db.password",
                "onlyPublic": "@(db.host, public)",
                "onlyPublicCall": "@public(db.host)",
                "publicOfSecret": "@public(db.password)",
                "onlySecret": "@secret(db.password)",
                "secretOfPublic": "@(db.host, secret)",
                "url": "@[https://@api.host/v1]",
                "missing": "@missing.key",
                "otherView": "@only.rollout"
            }
            """);

        var hidden = (await Configs.GetResolvedConfigurationAsync(key, includeSecrets: false))!.Content;
        hidden["host"]!.Value<string>().Should().Be("sql.internal");
        hidden["hostAgain"]!.Value<string>().Should().Be("sql.internal", "keys are case-insensitive");
        hidden["password"]!.Value<string>().Should().Be("@db.password", "a secret stays unbound without secrets");
        hidden["onlyPublic"]!.Value<string>().Should().Be("sql.internal");
        hidden["onlyPublicCall"]!.Value<string>().Should().Be("sql.internal");
        hidden["publicOfSecret"]!.Value<string>().Should().Be("@public(db.password)");
        hidden["onlySecret"]!.Value<string>().Should().Be("@secret(db.password)");
        hidden["secretOfPublic"]!.Value<string>().Should().Be("@(db.host, secret)", "the secret source declines public pairs");
        hidden["url"]!.Value<string>().Should().Be("https://api.internal/v1");
        hidden["missing"]!.Value<string>().Should().Be("@missing.key", "a missing key stays as stored");
        hidden["otherView"]!.Value<string>().Should().Be("@only.rollout", "a configuration sees the pairs of its own view only");

        var revealed = (await Configs.GetResolvedConfigurationAsync(key, includeSecrets: true))!.Content;
        revealed["password"]!.Value<string>().Should().Be("hunter2");
        revealed["onlySecret"]!.Value<string>().Should().Be("hunter2");
        revealed["publicOfSecret"]!.Value<string>().Should().Be("@public(db.password)", "the public source never reveals a secret");
    }

    [Fact]
    public async Task ResolvedConfiguration_MissingKeyInsideInterpolation_Throws()
    {
        var project = $"kvb-{Guid.NewGuid():N}"[..30];
        var key = await CreateConfigurationAsync(project, "interpolation", """{ "url": "@[https://@nope.host/v1]" }""");

        var act = () => Configs.GetResolvedConfigurationAsync(key, includeSecrets: false);
        var failure = await act.Should().ThrowAsync<BindingException>();
        failure.Which.Message.Should().Contain("could not be resolved");
    }

    [Fact]
    public async Task ResolvedConfiguration_LooksAKeyUpOncePerRead()
    {
        var project = $"kvb-{Guid.NewGuid():N}"[..30];
        await KeyValues.SetAsync(Org, project, View, "shared.value", new KeyValueWrite { Protection = KeyValueProtection.Public, Value = "x" }, Admin);
        var key = await CreateConfigurationAsync(project, "memo", """
            {
                "a": "@shared.value",
                "b": "@Shared.Value",
                "c": "@[@shared.value and @shared.value]"
            }
            """);

        Counting.ResetLookups();
        var resolved = (await Configs.GetResolvedConfigurationAsync(key, includeSecrets: false))!.Content;

        resolved["c"]!.Value<string>().Should().Be("x and x");
        Counting.GetLookupCount("shared.value").Should().Be(1, "the lookups are counted case-insensitively, and four bindings of one key make one lookup");

        // a changed pair takes effect on the next read, the memo lives with the read
        await KeyValues.SetAsync(Org, project, View, "shared.value", new KeyValueWrite { Value = "y" }, Admin);
        (await Configs.GetResolvedConfigurationAsync(key, includeSecrets: false))!.Content["a"]!.Value<string>().Should().Be("y");
    }

    [Fact]
    public async Task ResolvedConfiguration_FailsWhenTheKeyProviderIsUnavailable()
    {
        var project = $"kvb-{Guid.NewGuid():N}"[..30];
        await KeyValues.SetAsync(Org, project, View, "flaky.secret", new KeyValueWrite { Protection = KeyValueProtection.Standard, Value = "s" }, Admin);
        var key = await CreateConfigurationAsync(project, "unavailable", """{ "s": "@flaky.secret" }""");

        KeyProvider.IsUnavailable = true;

        try
        {
            var act = () => Configs.GetResolvedConfigurationAsync(key, includeSecrets: true);
            var failure = await act.Should().ThrowAsync<ServiceUnavailableException>();
            failure.Which.ErrorCode.Should().Be(ErrorCodes.KeyEncryptionUnavailable);
            failure.Which.RetryAfter.Should().Be(TimeSpan.FromSeconds(5));

            // without secrets the vault is not needed
            var hidden = await Configs.GetResolvedConfigurationAsync(key, includeSecrets: false);
            hidden!.Content["s"]!.Value<string>().Should().Be("@flaky.secret");
        }
        finally
        {
            KeyProvider.IsUnavailable = false;
        }
    }

    [Fact]
    public void Registration_SourceAndFunctionWithTheSameName_FailsTheStartup()
    {
        var services = new ServiceCollection();
        services.AddConfigurationBindings(options => options
            .AddFunction("kv", _ => new DelegateFunction())
            .AddSource(_ => new DelegateSource(), "kv"));

        using var provider = services.BuildServiceProvider();
        var act = () => provider.GetRequiredService<IBindingExecutor>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*'kv'*both a source and a function*");
    }

    [Fact]
    public void Registration_AVaultNamedDefaultIsNotABindingSource()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AzureKeyVaults:default"] = "https://default.vault.azure.net/",
            ["AzureKeyVaults:public"] = "https://public.vault.azure.net/",
            ["AzureKeyVaults:shared"] = "https://shared.vault.azure.net/",
        }).Build();

        var services = new ServiceCollection();
        services.AddConfigurationBindings();
        services.AddAzureKeyVaultBindings(configuration);
        using var withoutStore = services.BuildServiceProvider();
        var vaultOnly = withoutStore.GetRequiredService<IOptions<BindingsOptions>>().Value;
        vaultOnly.SourceKeys.Should().BeEquivalentTo(["shared"]);

        services.AddKeyValueBindings();
        using var withStore = services.BuildServiceProvider();
        var all = withStore.GetRequiredService<IOptions<BindingsOptions>>().Value;
        all.SourceKeys.Should().BeEquivalentTo(["shared", "default", "public", "secret"]);
    }

    private async Task<FullKey> CreateConfigurationAsync(string project, string name, string content)
    {
        var annotationKey = AnnotationKey.CreateResponsibility(name);
        await Annotations.CreateAnnotationAsync(Org, new Responsibility
        {
            AnnotationKey = annotationKey,
            AnnotationType = AnnotationType.Responsibility,
            Name = name,
            ProjectName = project,
            ViewName = View,
        });

        var key = FullKey.Create(annotationKey, Org, project, View);
        await Configs.CreateOrUpdateConfigurationAsync(key, JObject.Parse(content), "system");
        return key;
    }

    private sealed class DelegateFunction : IBindingFunction
    {
        public ValueTask<BindingValue?> InvokeAsync(BindingFunctionRequest request) => new((BindingValue?)null);
    }

    private sealed class DelegateSource : IBindingSource
    {
        public ValueTask<BindingValue?> ResolveAsync(BindingRequest request) => new((BindingValue?)null);
    }
}

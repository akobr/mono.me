using System.IO.Abstractions.TestingHelpers;
using System.Net.Http.Headers;
using _42.Platform.Storyteller.Sdk.Configuring.Settings;
using _42.Platform.Storyteller.Sdk.Credentials;
using _42.Platform.Storyteller.Sdk.Resilience;
using Microsoft.Extensions.Time.Testing;

namespace _42.Platform.Storyteller.Sdk.Configuring.UnitTests.Settings;

public class SettingsReaderTests
{
    private const string ApiKey = "2s.YWNtZTpiaWxsaW5nOm1hY2hpbmUtMQ.k3y-s3cret";

    private readonly Dictionary<string, string?> _environment = new()
    {
        ["STORYTELLER_ENDPOINT"] = TestDocuments.Endpoint,
        ["STORYTELLER_KEY"] = TestDocuments.Key,
    };

    private readonly MockFileSystem _fileSystem = new();

    [Fact]
    public void Defaults()
    {
        var settings = Read();

        settings.Endpoint.ShouldBe(new Uri(TestDocuments.Endpoint));
        settings.Key.ShouldNotBeNull().ToString().ShouldBe(TestDocuments.Key);
        settings.Mode.ShouldBe(StorytellerReadMode.Resolved);
        settings.RefreshInterval.ShouldBe(TimeSpan.FromSeconds(30));
        settings.Optional.ShouldBeFalse();
        settings.CacheDirectory.ShouldBeNull();
        settings.Credential.ShouldBeNull();
        settings.ReferenceResolver.ShouldBeNull();
        settings.SecretRefreshInterval.ShouldBe(TimeSpan.FromHours(1));
        settings.ReadTimeout.ShouldBe(TimeSpan.FromSeconds(30));
        settings.Resilience.AttemptTimeout.ShouldBe(TimeSpan.FromSeconds(5));
        settings.Placement.ShouldBe(SourcePlacement.BeforeEnvironmentVariables);
        settings.CacheProtector.ShouldBeNull();
        settings.LoggerFactory.ShouldBeNull();
        settings.TimeProvider.ShouldBe(TimeProvider.System);
        settings.FileSystem.ShouldBe(_fileSystem);
        settings.NextRandom().ShouldBeInRange(0, 1);
    }

    [Fact]
    public void TheEnvironment_FillsWhatTheCodeLeftUnset()
    {
        _environment["STORYTELLER_MODE"] = "Effective";
        _environment["STORYTELLER_REFRESH_INTERVAL"] = "5m";
        _environment["STORYTELLER_OPTIONAL"] = "1";
        _environment["STORYTELLER_CACHE_DIR"] = "/var/cache/storyteller";

        var settings = Read();

        settings.Mode.ShouldBe(StorytellerReadMode.Effective);
        settings.RefreshInterval.ShouldBe(TimeSpan.FromMinutes(5));
        settings.Optional.ShouldBeTrue();
        settings.CacheDirectory.ShouldBe(_fileSystem.Path.GetFullPath("/var/cache/storyteller"));
    }

    [Fact]
    public void Code_WinsOverTheEnvironment_EvenOverInvalidValues()
    {
        _environment["STORYTELLER_ENDPOINT"] = "http://not-local/api";
        _environment["STORYTELLER_API_URL"] = "https://other.example.com/api";
        _environment["STORYTELLER_KEY"] = "not a key";
        _environment["STORYTELLER_MODE"] = "raw";
        _environment["STORYTELLER_REFRESH_INTERVAL"] = "soon";
        _environment["STORYTELLER_OPTIONAL"] = "maybe";
        _environment["STORYTELLER_CACHE_DIR"] = "/var/cache/environment";
        _environment["STORYTELLER_API_KEY"] = "ApiKey";

        var credential = TestCredential.Bearer("token");
        var settings = Read(options =>
        {
            options.Endpoint = "https://code.example.com/api";
            options.Key = "@acme.billing.prod.rst.payments";
            options.Mode = StorytellerReadMode.Effective;
            options.RefreshInterval = TimeSpan.Zero;
            options.Optional = true;
            options.CacheDirectory = "/var/cache/code";
            options.Credential = credential;
        });

        settings.Endpoint.ShouldBe(new Uri("https://code.example.com/api"));
        settings.Key!.ToString().ShouldBe("@acme.billing.prod.rst.payments");
        settings.Mode.ShouldBe(StorytellerReadMode.Effective);
        settings.RefreshInterval.ShouldBe(TimeSpan.Zero);
        settings.Optional.ShouldBeTrue();
        settings.CacheDirectory.ShouldBe(_fileSystem.Path.GetFullPath("/var/cache/code"));
        settings.Credential.ShouldBeSameAs(credential);
    }

    [Fact]
    public void ABlankValueInCode_CountsAsUnset()
    {
        var settings = Read(options =>
        {
            options.Endpoint = "  ";
            options.Key = string.Empty;
        });

        settings.Endpoint.ShouldBe(new Uri(TestDocuments.Endpoint));
        settings.Key!.ToString().ShouldBe(TestDocuments.Key);
    }

    [Fact]
    public void TheSformEndpointName_IsAFallback()
    {
        _environment.Remove("STORYTELLER_ENDPOINT");
        _environment["STORYTELLER_API_URL"] = "http://localhost:7071/api";

        Read().Endpoint.ShouldBe(new Uri("http://localhost:7071/api"));
    }

    [Fact]
    public void TheSameValueUnderBothNames_IsFine()
    {
        _environment["STORYTELLER_API_URL"] = TestDocuments.Endpoint;

        Read().Endpoint.ShouldBe(new Uri(TestDocuments.Endpoint));
    }

    [Fact]
    public void ANameAndItsFallback_WithDifferentValues_FailAndNameBoth()
    {
        _environment["STORYTELLER_API_URL"] = "https://other.example.com/api";

        var exception = ShouldFail();

        exception.Message.ShouldContain("STORYTELLER_ENDPOINT");
        exception.Message.ShouldContain("STORYTELLER_API_URL");
    }

    [Fact]
    public void CredentialFallbacks_ThatDisagree_FailAndNameBoth()
    {
        _environment["STORYTELLER_CLIENT_CERTIFICATE"] = "/run/a.pfx";
        _environment["STORYTELLER_CERTIFICATE_FILE"] = "/run/b.pfx";
        _environment["STORYTELLER_CERTIFICATE_PASSWORD"] = "pfx-s3cret";

        var exception = ShouldFail();

        exception.Message.ShouldContain("STORYTELLER_CLIENT_CERTIFICATE");
        exception.Message.ShouldContain("STORYTELLER_CERTIFICATE_FILE");
        exception.Message.ShouldNotContain("pfx-s3cret");
        exception.InnerException.ShouldBeOfType<StorytellerAuthenticationException>().Reason.ShouldBe(StorytellerAuthenticationFailureReason.Configuration);
        exception.Key.ShouldBe(TestDocuments.Key);
    }

    [Theory]
    [InlineData("https://storyteller.example.com/api")]
    [InlineData("https://storyteller.example.com:8443/api/")]
    [InlineData("http://localhost:7071/api")]
    [InlineData("http://127.0.0.1:7071/api")]
    [InlineData("http://[::1]:7071/api")]
    public void TheLocalUrlRule_Allows(string endpoint)
    {
        _environment["STORYTELLER_ENDPOINT"] = endpoint;

        Read().Endpoint.ShouldBe(new Uri(endpoint));
    }

    [Theory]
    [InlineData("http://loopback/api")]
    [InlineData("http://storyteller.example.com/api")]
    [InlineData("http://10.0.0.5/api")]
    [InlineData("https://user:p4ssw0rd@storyteller.example.com/api")]
    [InlineData("ftp://storyteller.example.com/api")]
    [InlineData("/api")]
    [InlineData("https://storyteller.example.com/api?code=p4ssw0rd")]
    [InlineData("https://storyteller.example.com/api#p4ssw0rd")]
    public void TheLocalUrlRule_Refuses_AndNamesTheVariableButNotTheValue(string endpoint)
    {
        _environment["STORYTELLER_ENDPOINT"] = endpoint;

        var exception = ShouldFail();

        exception.Message.ShouldContain("STORYTELLER_ENDPOINT");
        exception.Message.ShouldNotContain("p4ssw0rd");
        exception.IsTransient.ShouldBeFalse();
    }

    // LocalUrl copies the SDK's internal rule: an endpoint the provider accepts is one its credentials accept.
    [Theory]
    [InlineData("https://storyteller.example.com/api")]
    [InlineData("https://storyteller.example.com:8443/api/")]
    [InlineData("http://localhost:7071/api")]
    [InlineData("http://LOCALHOST/api")]
    [InlineData("http://127.0.0.1:7071/api")]
    [InlineData("http://[::1]:7071/api")]
    [InlineData("http://loopback/api")]
    [InlineData("http://storyteller.example.com/api")]
    [InlineData("http://10.0.0.5/api")]
    [InlineData("https://user:p4ssw0rd@storyteller.example.com/api")]
    [InlineData("http://user@localhost/api")]
    [InlineData("ftp://storyteller.example.com/api")]
    [InlineData("https://storyteller.example.com/api?code=p4ssw0rd")]
    [InlineData("https://storyteller.example.com/api#p4ssw0rd")]
    public void TheLocalUrlRule_IsTheOneOfTheSdkCredentials(string endpoint)
    {
        _environment["STORYTELLER_ENDPOINT"] = endpoint;
        var uri = new Uri(endpoint, UriKind.Absolute);

        var providerAccepts = TryRun(() => Read());
        var credentialAccepts = TryRun(() => StorytellerCredential.FromFederatedTokenFile(
            new FederatedTokenOptions { Endpoint = uri, ClientId = "machine", SubjectTokenPath = "/run/token" }));

        credentialAccepts.ShouldBe(providerAccepts);

        static bool TryRun(Action action)
        {
            try
            {
                action();
                return true;
            }
            catch (Exception exception) when (exception is StorytellerConfigurationException or StorytellerAuthenticationException)
            {
                return false;
            }
        }
    }

    [Fact]
    public void AnEndpointSetInCode_IsNamedAsAProperty()
    {
        ShouldFail(options => options.Endpoint = "http://storyteller.example.com/api")
            .Message.ShouldContain("StorytellerConfigurationOptions.Endpoint");
    }

    [Fact]
    public void AMissingEndpoint_NamesTheVariables()
    {
        _environment.Remove("STORYTELLER_ENDPOINT");

        var exception = ShouldFail();

        exception.Message.ShouldContain("STORYTELLER_ENDPOINT");
        exception.Message.ShouldContain("StorytellerConfigurationOptions.Endpoint");
    }

    [Fact]
    public void AMissingKey_FailsTheProvider_ButNotTheDocumentService()
    {
        _environment.Remove("STORYTELLER_KEY");

        ShouldFail().Message.ShouldContain("STORYTELLER_KEY");
        Read(requireKey: false).Key.ShouldBeNull();
        SettingsReader.ReadForDocuments(Options()).Key.ShouldBeNull();
    }

    [Fact]
    public void TheDocumentService_IgnoresTheOptionsOfTheProviderAlone()
    {
        _environment["STORYTELLER_REFRESH_INTERVAL"] = "often";
        _environment["STORYTELLER_OPTIONAL"] = "maybe";
        _environment["STORYTELLER_CACHE_DIR"] = "/var/cache/storyteller";
        _environment["STORYTELLER_MODE"] = "effective";

        var settings = SettingsReader.ReadForDocuments(Options(options =>
        {
            options.RefreshInterval = TimeSpan.FromMilliseconds(1);
            options.Optional = true;
            options.CacheDirectory = "/var/cache/other";
            options.CacheProtector = new TestCacheProtector();
            options.Placement = (SourcePlacement)7;
            options.SecretRefreshInterval = TimeSpan.FromMinutes(5);
        }));

        settings.Key.ShouldNotBeNull().ToString().ShouldBe(TestDocuments.Key);
        settings.Mode.ShouldBe(StorytellerReadMode.Effective);
        settings.SecretRefreshInterval.ShouldBe(TimeSpan.FromMinutes(5));
        settings.RefreshInterval.ShouldBe(TimeSpan.Zero);
        settings.Optional.ShouldBeFalse();
        settings.CacheDirectory.ShouldBeNull();
        settings.CacheProtector.ShouldBeNull();
        settings.Placement.ShouldBe(SourcePlacement.BeforeEnvironmentVariables);
    }

    [Fact]
    public void TheDocumentService_ValidatesWhatItHonours()
    {
        _environment["STORYTELLER_KEY"] = "@acme.billing";
        Should.Throw<StorytellerConfigurationException>(() => SettingsReader.ReadForDocuments(Options())).Message.ShouldContain("STORYTELLER_KEY");

        _environment["STORYTELLER_KEY"] = TestDocuments.Key;
        _environment["STORYTELLER_MODE"] = "both";
        Should.Throw<StorytellerConfigurationException>(() => SettingsReader.ReadForDocuments(Options())).Message.ShouldContain("STORYTELLER_MODE");

        _environment.Remove("STORYTELLER_MODE");
        Should.Throw<StorytellerConfigurationException>(() => SettingsReader.ReadForDocuments(Options(options => options.ReadTimeout = TimeSpan.Zero)))
            .Message.ShouldContain("ReadTimeout");
    }

    [Fact]
    public void TheDocumentService_BindsTheCredentialToTheProjectOfItsKey_WhenItHasOne()
    {
        var scoped = TestCredential.ApiKey(ApiKey);
        SettingsReader.ReadForDocuments(Options(options => options.Credential = scoped));
        _environment.Remove("STORYTELLER_KEY");
        var unscoped = TestCredential.ApiKey(ApiKey);
        SettingsReader.ReadForDocuments(Options(options => options.Credential = unscoped));

        var binding = scoped.Bindings.ShouldHaveSingleItem();
        binding.Endpoint.ShouldBe(new Uri(TestDocuments.Endpoint));
        binding.Organization.ShouldBe("acme");
        binding.Project.ShouldBe("billing");
        var without = unscoped.Bindings.ShouldHaveSingleItem();
        without.Organization.ShouldBeNull();
        without.Project.ShouldBeNull();
    }

    [Theory]
    [InlineData("acme.billing.default.sbt.northwind")]
    [InlineData("@acme.billing.default.xyz.northwind")]
    [InlineData("@acme.billing.default.exe.a.b.c.d")]
    [InlineData("@.billing.default.sbt.northwind")]
    public void AnInvalidKey_NamesItsSourceAndTheReason(string key)
    {
        _environment["STORYTELLER_KEY"] = key;

        var exception = ShouldFail();

        exception.Message.ShouldContain("STORYTELLER_KEY");
        exception.Message.ShouldContain("full key");
        exception.InnerException.ShouldBeOfType<FormatException>();
        exception.Key.ShouldBe(key);

        ShouldFail(options => options.Key = key).Message.ShouldContain("StorytellerConfigurationOptions.Key");
    }

    [Fact]
    public void AnEightSegmentKey_OfAUnitOfExecution_Parses()
    {
        _environment["STORYTELLER_KEY"] = "@acme.billing.default.uxe.northwind.payments.eu.worker";

        Read().Key!.Annotation.Type.ShouldBe(AnnotationType.UnitOfExecution);
    }

    [Theory]
    [InlineData("STORYTELLER_MODE", "raw")]
    [InlineData("STORYTELLER_OPTIONAL", "yes")]
    [InlineData("STORYTELLER_REFRESH_INTERVAL", "soon")]
    [InlineData("STORYTELLER_REFRESH_INTERVAL", "500ms")]
    [InlineData("STORYTELLER_REFRESH_INTERVAL", "-30s")]
    [InlineData("STORYTELLER_REFRESH_INTERVAL", "1081h")]
    public void AnInvalidValue_NamesItsVariable(string name, string value)
    {
        _environment[name] = value;

        var exception = ShouldFail();

        exception.Message.ShouldContain(name);
        exception.Key.ShouldBe(TestDocuments.Key);
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("1s", 1_000)]
    [InlineData("00:00:01", 1_000)]
    [InlineData("1080h", 3_888_000_000)]
    public void TheRefreshInterval_IsZeroOrAtLeastASecond_AndAtMost45Days(string text, long milliseconds)
    {
        _environment["STORYTELLER_REFRESH_INTERVAL"] = text;

        Read().RefreshInterval.ShouldBe(TimeSpan.FromMilliseconds(milliseconds));
    }

    [Theory]
    [InlineData(500)]
    [InlineData(46L * 24 * 60 * 60 * 1000)]
    [InlineData(922_337_203_685_477)]
    public void ARefreshIntervalInCode_OutOfRange_NamesTheProperty(long milliseconds)
    {
        ShouldFail(options => options.RefreshInterval = TimeSpan.FromMilliseconds(milliseconds))
            .Message.ShouldContain("StorytellerConfigurationOptions.RefreshInterval");
    }

    [Fact]
    public void Durations_OfTheOptions_MustBePositive()
    {
        ShouldFail(options => options.SecretRefreshInterval = TimeSpan.Zero).Message.ShouldContain("SecretRefreshInterval");
        ShouldFail(options => options.ReadTimeout = TimeSpan.FromSeconds(-1)).Message.ShouldContain("ReadTimeout");

        Read(options => options.ReadTimeout = Timeout.InfiniteTimeSpan).ReadTimeout.ShouldBe(Timeout.InfiniteTimeSpan);
    }

    [Fact]
    public void UndefinedEnumValues_Fail()
    {
        ShouldFail(options => options.Mode = (StorytellerReadMode)7).Message.ShouldContain("Mode");
        ShouldFail(options => options.Placement = (SourcePlacement)7).Message.ShouldContain("Placement");
    }

    [Fact]
    public void Resilience_IsCopiedAndValidated()
    {
        var options = Options();
        options.Resilience.MaxRetryAttempts = 1;

        var settings = SettingsReader.Read(options, true);
        options.Resilience.MaxRetryAttempts = 7;

        settings.Resilience.MaxRetryAttempts.ShouldBe(1);
        settings.Resilience.ShouldNotBeSameAs(options.Resilience);

        ShouldFail(invalid => invalid.Resilience.AttemptTimeout = TimeSpan.FromMilliseconds(1)).Message.ShouldContain("Resilience");
        ShouldFail(invalid => invalid.Resilience = null!).Message.ShouldContain("Resilience");

        // A typo that would turn retries or the attempt timeout off fails instead.
        ShouldFail(invalid => invalid.Resilience.MaxRetryAttempts = -1).Message.ShouldContain("MaxRetryAttempts");
        ShouldFail(invalid => invalid.Resilience.AttemptTimeout = TimeSpan.FromSeconds(-5)).Message.ShouldContain("AttemptTimeout");
    }

    [Fact]
    public void ARelativeCacheDirectory_IsResolvedNow()
    {
        _environment["STORYTELLER_CACHE_DIR"] = "cache";

        var settings = Read();

        settings.CacheDirectory.ShouldBe(_fileSystem.Path.Combine(_fileSystem.Directory.GetCurrentDirectory(), "cache"));
    }

    [Fact]
    public async Task ACredential_FromTheEnvironment()
    {
        _environment["STORYTELLER_API_KEY"] = ApiKey;

        var settings = Read();

        var authorization = await settings.Credential.ShouldNotBeNull().GetAuthorizationAsync(null, CancellationToken.None);
        authorization.ShouldBe(new AuthenticationHeaderValue("ApiKey", ApiKey));
    }

    [Fact]
    public void ACredential_IsBoundToTheValidatedOptions()
    {
        var credential = TestCredential.Bearer("token");
        var time = new FakeTimeProvider();
        Action<SocketsHttpHandler> configure = _ => { };

        var settings = Read(options =>
        {
            options.Credential = credential;
            options.Key = "@acme.billing.prod.rst.payments";
            options.ConfigurePrimaryHandler = configure;
            options.TimeProvider = time;
        });

        var binding = credential.Bindings.ShouldHaveSingleItem();
        binding.Endpoint.ShouldBe(new Uri(TestDocuments.Endpoint));
        binding.Organization.ShouldBe("acme");
        binding.Project.ShouldBe("billing");
        binding.ConfigureHandler.ShouldBeSameAs(configure);
        binding.TimeProvider.ShouldBeSameAs(time);
        binding.Resilience.ShouldBeSameAs(settings.Resilience);
    }

    [Fact]
    public async Task AFederatedCredential_FromTheEnvironment_BindsToAKeySetInCode()
    {
        _fileSystem.AddFile("/run/platform-token", new MockFileData("platform-token"));
        _environment["STORYTELLER_CLIENT_ID"] = "machine-1";
        _environment["STORYTELLER_TOKEN_FILE"] = "/run/platform-token";
        _environment["STORYTELLER_KEY"] = "@other.elsewhere.default.sbt.northwind";

        var settings = Read(options =>
        {
            options.Endpoint = "http://127.0.0.1:9/api";
            options.Key = "@acme.billing.default.sbt.northwind";
            options.Resilience.MaxRetryAttempts = 0;

            // No network: the exchange fails, and its message names the URL it was sent to.
            options.ConfigurePrimaryHandler = handler => handler.ConnectCallback = (_, _) => ValueTask.FromException<Stream>(new IOException("Offline."));
        });

        var exception = await Should.ThrowAsync<StorytellerAuthenticationException>(
            () => settings.Credential.ShouldNotBeNull().GetAuthorizationAsync(null, CancellationToken.None).AsTask());

        exception.Reason.ShouldBe(StorytellerAuthenticationFailureReason.Unavailable);
        exception.Message.ShouldContain("http://127.0.0.1:9/api/v1/acme/billing/access/token");
    }

    [Fact]
    public void NoCredentialVariables_MeanAnonymousRequests()
    {
        Read().Credential.ShouldBeNull();
    }

    private StorytellerConfigurationOptions Options(Action<StorytellerConfigurationOptions>? configure = null)
    {
        var options = TestOptions.Create(_environment, fileSystem: _fileSystem);
        options.Resilience = new StorytellerResilienceOptions();
        configure?.Invoke(options);
        return options;
    }

    private StorytellerSettings Read(Action<StorytellerConfigurationOptions>? configure = null, bool requireKey = true)
    {
        return SettingsReader.Read(Options(configure), requireKey);
    }

    private StorytellerConfigurationException ShouldFail(Action<StorytellerConfigurationOptions>? configure = null)
    {
        return Should.Throw<StorytellerConfigurationException>(() => Read(configure));
    }
}

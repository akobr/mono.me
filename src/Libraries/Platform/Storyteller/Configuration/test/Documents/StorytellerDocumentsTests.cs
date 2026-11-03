using System.Net;
using _42.Platform.Storyteller.Sdk.Configuring.Documents;
using _42.Platform.Storyteller.Sdk.Configuring.Reading;
using _42.Platform.Storyteller.Sdk.Configuring.UnitTests.Engine;
using _42.Platform.Storyteller.Sdk.Configuring.UnitTests.References;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Sdk.Configuring.UnitTests.Documents;

public sealed class StorytellerDocumentsTests : IDisposable
{
    private const string Secret = "S3cr3t-Value-0f-The-Vault";
    private const string Reference = "kv://my-vault/db-password";
    private const string Content = """{ "Db": { "Host": "db.local", "Port": 5432, "Password": "kv://my-vault/db-password", "Hosts": [ "a", "b" ] } }""";
    private const string ResolvedPath = "/api/v1/acme/billing/default/configuration/sbt.northwind/resolved";
    private const string EffectivePath = "/api/v1/acme/billing/default/configuration/sbt.northwind";
    private const string LogCategory = "_42.Platform.Storyteller.Sdk.Configuring.StorytellerDocuments";

    private readonly EngineFixture _fixture = new();
    private readonly FakeResolver _resolver = new() { Secrets = { [Reference] = Secret } };

    [Fact]
    public async Task AFullKey_IsReadInTheModeOfTheOptions_IntoADocument()
    {
        Respond(Content, EngineFixture.Tag(1));
        var documents = Create(options => options.ReferenceResolver = _resolver);

        var document = await documents.GetAsync(TestDocuments.Key);

        var request = _fixture.Http.Requests.ShouldHaveSingleItem();
        request.Uri.AbsolutePath.ShouldBe(ResolvedPath);
        request.Headers.ShouldNotContainKey("If-None-Match");
        document.Key.ShouldBe(TestDocuments.Key);
        document.Mode.ShouldBe(StorytellerReadMode.Resolved);
        document.ETag.ShouldBe(EngineFixture.Tag(1));
        document.Version.ShouldBe(3);
        document.Hash.ShouldBe("a1b2c3d4");
        document.Modified.ShouldNotBeNull().At.ShouldBe(DateTimeOffset.Parse("2026-10-19T08:30:00+02:00"));
        document.Modified.By.Id.ShouldBe("user_1");
        document.Labels.ShouldBe(["billing", "eu"]);
        document.Values["tier"].ShouldBe("gold");
        document.Values["limits"].ShouldBeOfType<JObject>()["max"]!.Value<int>().ShouldBe(5);
        document.Content["Db"]!["Password"]!.Value<string>().ShouldBe(Secret);
        document.Content["Db"]!["Port"]!.Value<int>().ShouldBe(5432);
        document.Configuration["Db:Password"].ShouldBe(Secret);
        document.Configuration["db:hosts:1"].ShouldBe("b");
        document.Configuration["Db:Port"].ShouldBe("5432");
    }

    [Fact]
    public async Task EveryCall_Revalidates_AndA304ReturnsTheCachedDocument()
    {
        Respond(Content, EngineFixture.Tag(1));
        var documents = Create(options => options.ReferenceResolver = _resolver);
        var first = await documents.GetAsync(TestDocuments.Key);
        _fixture.RespondNotModified(EngineFixture.Tag(1), "no-store");

        var second = await documents.GetAsync(TestDocuments.Key);

        _fixture.Http.Requests.Count.ShouldBe(2);
        _fixture.Http.Requests[1].Headers["If-None-Match"].ShouldBe(EngineFixture.Tag(1));
        second.ETag.ShouldBe(EngineFixture.Tag(1));
        second.Version.ShouldBe(first.Version);
        second.Content.ToString().ShouldBe(first.Content.ToString());
        second.Configuration["Db:Password"].ShouldBe(Secret);
        _resolver.Resolved.ShouldBe([Reference]);

        // A new tag on a 304 is the one the next call sends.
        _fixture.RespondNotModified(EngineFixture.Tag(2));
        (await documents.GetAsync(TestDocuments.Key)).ETag.ShouldBe(EngineFixture.Tag(2));
        _fixture.RespondNotModified(EngineFixture.Tag(2));
        await documents.GetAsync(TestDocuments.Key);
        _fixture.Http.Requests[^1].Headers["If-None-Match"].ShouldBe(EngineFixture.Tag(2));
    }

    [Fact]
    public async Task A304_ResolvesExpiredReferencesAgain_AndReusesFreshOnes()
    {
        Respond(Content, EngineFixture.Tag(1));
        var documents = Create(options =>
        {
            options.ReferenceResolver = _resolver;
            options.SecretRefreshInterval = TimeSpan.FromMinutes(10);
        });
        await documents.GetAsync(TestDocuments.Key);
        _resolver.Secrets[Reference] = "rotated";

        _fixture.Time.Advance(TimeSpan.FromMinutes(5));
        _fixture.RespondNotModified(EngineFixture.Tag(1));
        (await documents.GetAsync(TestDocuments.Key)).Configuration["Db:Password"].ShouldBe(Secret);

        _fixture.Time.Advance(TimeSpan.FromMinutes(6));
        _fixture.RespondNotModified(EngineFixture.Tag(1));
        var rotated = await documents.GetAsync(TestDocuments.Key);

        rotated.Configuration["Db:Password"].ShouldBe("rotated");
        rotated.Content["Db"]!["Password"]!.Value<string>().ShouldBe("rotated");
        _resolver.Resolved.ShouldBe([Reference, Reference]);
        _fixture.Http.Requests[^1].Headers["If-None-Match"].ShouldBe(EngineFixture.Tag(1));
    }

    [Theory]
    [InlineData("@acme.billing.default.sbt.contoso", StorytellerReadMode.Resolved)]
    [InlineData("@acme.billing.staging.sbt.northwind", StorytellerReadMode.Resolved)]
    [InlineData(TestDocuments.Key, StorytellerReadMode.Effective)]
    public async Task ATagIsSentForItsOwnIdentityOnly(string otherKey, StorytellerReadMode otherMode)
    {
        _fixture.Http.Otherwise = (_, _) => Task.FromResult(Document(Content, EngineFixture.Tag(9)));
        Respond(Content, EngineFixture.Tag(1));
        var documents = Create();
        await documents.GetAsync(TestDocuments.Key);

        var other = await documents.GetAsync(otherKey, otherMode);

        other.Key.ShouldBe(otherKey);
        other.Mode.ShouldBe(otherMode);
        _fixture.Http.Requests[1].Headers.ShouldNotContainKey("If-None-Match");
        _fixture.RespondNotModified(EngineFixture.Tag(1));
        (await documents.GetAsync(TestDocuments.Key, StorytellerReadMode.Resolved)).ETag.ShouldBe(EngineFixture.Tag(1));
        _fixture.Http.Requests[2].Headers["If-None-Match"].ShouldBe(EngineFixture.Tag(1));
        _fixture.RespondNotModified(EngineFixture.Tag(9));
        await documents.GetAsync(otherKey, otherMode);
        _fixture.Http.Requests[3].Headers["If-None-Match"].ShouldBe(EngineFixture.Tag(9));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentCalls_ShareOneRequest_AlsoBeyondTheSizeLimitOfTheCache(bool fullCache)
    {
        var documents = Create();
        var cache = ((StorytellerDocuments)documents).Cache;

        if (fullCache)
        {
            for (var index = 0; index < StorytellerDocuments.CacheSizeLimit; index++)
            {
                cache.Set($"filler-{index}", index, new MemoryCacheEntryOptions { Size = 1 });
            }

            cache.Count.ShouldBe(StorytellerDocuments.CacheSizeLimit);
        }

        var gate = new GatedResponse(() => Document(Content, EngineFixture.Tag(1)));
        _fixture.Http.Respond(gate.RespondAsync);

        var calls = Enumerable.Range(0, 10).Select(_ => documents.GetAsync(TestDocuments.Key)).ToArray();
        await gate.Entered;
        gate.Release();
        var results = await Task.WhenAll(calls).WaitAsync(EngineFixture.Patience);

        _fixture.Http.Requests.ShouldHaveSingleItem();
        results.ShouldAllBe(document => document.ETag == EngineFixture.Tag(1) && document.Configuration["Db:Host"] == "db.local");
        results.Distinct().Count().ShouldBe(results.Length);
        cache.TryGetValue(Identity(), out _).ShouldBe(!fullCache);

        // Done: the next call sends a request of its own, with the tag only if the cache took the document.
        _fixture.Http.Otherwise = (_, _) => Task.FromResult(fullCache ? Document(Content, EngineFixture.Tag(1)) : EngineFixture.NotModified(EngineFixture.Tag(1)));
        await documents.GetAsync(TestDocuments.Key);
        _fixture.Http.Requests.Count.ShouldBe(2);
        _fixture.Http.Requests[1].Headers.ContainsKey("If-None-Match").ShouldBe(!fullCache);
    }

    [Fact]
    public async Task ACallerThatCancels_EndsItsOwnWaitOnly()
    {
        var documents = Create();
        var gate = new GatedResponse(() => Document(Content, EngineFixture.Tag(1)));
        _fixture.Http.Respond(gate.RespondAsync);
        using var cancellation = new CancellationTokenSource();

        var cancelled = documents.GetAsync(TestDocuments.Key, cancellation.Token);
        var other = documents.GetAsync(TestDocuments.Key);
        await gate.Entered;
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => cancelled);
        gate.Release();
        (await other.WaitAsync(EngineFixture.Patience)).ETag.ShouldBe(EngineFixture.Tag(1));
        _fixture.Http.Requests.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task ResolvedValues_LiveInTheEntries_SoAnEvictedConfigurationTakesItsSecretsWithIt()
    {
        Respond(Content, EngineFixture.Tag(1));
        var documents = Create(options =>
        {
            options.ReferenceResolver = _resolver;
            options.SecretRefreshInterval = Timeout.InfiniteTimeSpan;
        });
        await documents.GetAsync(TestDocuments.Key);

        // A changed document reuses the values of its previous entry; another configuration resolves its own.
        Respond("""{ "Db": { "Name": "two", "Password": "kv://my-vault/db-password" } }""", EngineFixture.Tag(2));
        (await documents.GetAsync(TestDocuments.Key)).Configuration["Db:Password"].ShouldBe(Secret);
        _resolver.Resolved.ShouldBe([Reference]);
        Respond(Content, EngineFixture.Tag(3));
        await documents.GetAsync("@acme.billing.default.sbt.contoso");
        _resolver.Resolved.ShouldBe([Reference, Reference]);

        // Nothing of an evicted configuration stays in memory to be reused.
        _fixture.Http.Respond(HttpStatusCode.NotFound);
        await Should.ThrowAsync<StorytellerConfigurationException>(() => documents.GetAsync(TestDocuments.Key));
        _resolver.Secrets[Reference] = "rotated";
        Respond(Content, EngineFixture.Tag(4));

        (await documents.GetAsync(TestDocuments.Key)).Configuration["Db:Password"].ShouldBe("rotated");
        _resolver.Resolved.ShouldBe([Reference, Reference, Reference]);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, null, false)]
    [InlineData(HttpStatusCode.Forbidden, TestDocuments.ErrorJson, false)]
    [InlineData(HttpStatusCode.NotFound, null, false)]
    [InlineData(HttpStatusCode.ServiceUnavailable, null, true)]
    public async Task AFailureThrows_And401_403Or404EvictTheIdentity(HttpStatusCode status, string? body, bool isTransient)
    {
        Respond(Content, EngineFixture.Tag(1));
        var documents = Create();
        await documents.GetAsync(TestDocuments.Key);
        _fixture.Http.Respond(status, body);

        var exception = await Should.ThrowAsync<StorytellerConfigurationException>(() => documents.GetAsync(TestDocuments.Key));

        exception.Key.ShouldBe(TestDocuments.Key);
        exception.StatusCode.ShouldBe((int)status);
        exception.IsTransient.ShouldBe(isTransient);
        _fixture.Http.Requests[1].Headers["If-None-Match"].ShouldBe(EngineFixture.Tag(1));

        // A transient failure keeps the document for the next 304.
        Respond(Content, EngineFixture.Tag(2));
        await documents.GetAsync(TestDocuments.Key);
        _fixture.Http.Requests[2].Headers.ContainsKey("If-None-Match").ShouldBe(isTransient);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.NotModified)]
    public async Task WithheldSecrets_Fail_AndEvictTheIdentity(HttpStatusCode status)
    {
        Respond(Content, EngineFixture.Tag(1));
        var documents = Create();
        await documents.GetAsync(TestDocuments.Key);
        _fixture.Http.Respond(
            status,
            status == HttpStatusCode.OK ? Body(Content) : null,
            ("ETag", EngineFixture.Tag(1)),
            ("X-2S-Secrets-Withheld", "mtls-required"));

        var exception = await Should.ThrowAsync<StorytellerConfigurationException>(() => documents.GetAsync(TestDocuments.Key));

        exception.IsTransient.ShouldBeFalse();
        exception.Key.ShouldBe(TestDocuments.Key);
        exception.Message.ShouldContain("mtls-required");
        exception.Message.ShouldContain("STORYTELLER_CLIENT_CERTIFICATE");
        Respond(Content, EngineFixture.Tag(1));
        await documents.GetAsync(TestDocuments.Key);
        _fixture.Http.Requests[2].Headers.ShouldNotContainKey("If-None-Match");
    }

    [Fact]
    public async Task TheEffectiveMode_IgnoresTheWithheldHeader_AndResolvesNothing()
    {
        _fixture.Http.Respond(HttpStatusCode.OK, Body(Content), ("ETag", "W/\"e.AAECAwQFBgcICQoLDA0ODw\""), ("X-2S-Secrets-Withheld", "mtls-required"));
        var documents = Create(options =>
        {
            options.Mode = StorytellerReadMode.Effective;
            options.ReferenceResolver = _resolver;
        });

        var document = await documents.GetAsync(TestDocuments.Key);

        _fixture.Http.Requests.ShouldHaveSingleItem().Uri.AbsolutePath.ShouldBe(EffectivePath);
        document.Mode.ShouldBe(StorytellerReadMode.Effective);
        document.Configuration["Db:Password"].ShouldBe(Reference);
        _resolver.Asked.ShouldBeEmpty();
    }

    [Fact]
    public async Task Get_BindsTheDocumentOrASection()
    {
        Respond(Content, EngineFixture.Tag(1));
        var documents = Create(options => options.ReferenceResolver = _resolver);
        var document = await documents.GetAsync(TestDocuments.Key);

        var db = document.Get<DbOptions>("Db");
        var whole = document.Get<DocumentOptions>();

        db.Host.ShouldBe("db.local");
        db.Port.ShouldBe(5432);
        db.Password.ShouldBe(Secret);
        db.Hosts.ShouldBe(["a", "b"]);
        whole.Db.ShouldNotBeNull().Port.ShouldBe(5432);
        document.Get<int>("Db:Port").ShouldBe(5432);

        var missing = Should.Throw<StorytellerConfigurationException>(() => document.Get<DbOptions>("Cache"));
        missing.Message.ShouldContain("has no values at Cache");
        missing.Key.ShouldBe(TestDocuments.Key);
        Should.Throw<StorytellerConfigurationException>(() => document.Get<int>("Db:Timeout"));
    }

    [Fact]
    public async Task Get_NamesAValueThatDoesNotConvert_WithoutIt()
    {
        _resolver.Secrets["kv://my-vault/port"] = Secret;
        Respond("""{ "Db": { "Host": "db.local", "Port": "kv://my-vault/port" } }""", EngineFixture.Tag(1));
        var documents = Create(options => options.ReferenceResolver = _resolver);
        var document = await documents.GetAsync(TestDocuments.Key);

        var exception = Should.Throw<StorytellerConfigurationException>(() => document.Get<DbOptions>("Db"));

        exception.Message.ShouldContain(nameof(DbOptions));
        exception.Message.ShouldContain("at Db");
        exception.ToString().ShouldNotContain(Secret);
        exception.InnerException.ShouldBeNull();
    }

    [Fact]
    public async Task AnnotationKeys_AreReadInTheDefaultScope()
    {
        _fixture.Http.Otherwise = (_, _) => Task.FromResult(Document(Content, EngineFixture.Tag(1)));
        _fixture.Environment["STORYTELLER_KEY"] = "@acme.billing.staging.sbt.northwind";
        var documents = Create();

        var document = await documents.GetAsync(" SBT.contoso ");

        document.Key.ShouldBe("@acme.billing.staging.sbt.contoso");
        _fixture.Http.Requests.ShouldHaveSingleItem().Uri.AbsolutePath.ShouldBe("/api/v1/acme/billing/staging/configuration/sbt.contoso/resolved");
        Should.Throw<ArgumentException>(() => documents.GetAsync("not a key"));
        Should.Throw<ArgumentException>(() => documents.GetAsync("@acme.billing"));
        Should.Throw<ArgumentException>(() => documents.GetAsync(" "));
        Should.Throw<ArgumentOutOfRangeException>(() => documents.GetAsync(TestDocuments.Key, (StorytellerReadMode)7));
    }

    [Fact]
    public async Task WithoutAKey_FullKeysAreRead_AndAnnotationKeysAreRefused()
    {
        _fixture.Http.Otherwise = (_, _) => Task.FromResult(Document(Content, EngineFixture.Tag(1)));
        _fixture.Environment.Remove("STORYTELLER_KEY");
        var documents = Create();

        (await documents.GetAsync(TestDocuments.Key)).Key.ShouldBe(TestDocuments.Key);

        Should.Throw<ArgumentException>(() => documents.GetAsync("sbt.northwind")).Message.ShouldContain("STORYTELLER_KEY");
    }

    [Fact]
    public async Task AFederatedCredential_NeedsAKey()
    {
        _fixture.Environment.Remove("STORYTELLER_KEY");
        _fixture.Environment["STORYTELLER_CLIENT_ID"] = "machine-1";
        _fixture.Environment["STORYTELLER_TOKEN_FILE"] = "platform-token";
        var documents = Create();

        var exception = await Should.ThrowAsync<StorytellerConfigurationException>(() => documents.GetAsync(TestDocuments.Key));

        exception.IsTransient.ShouldBeFalse();
        exception.Message.ShouldContain("STORYTELLER_KEY");
        _fixture.Http.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task EachCall_GetsADocumentOfItsOwn()
    {
        Respond(Content, EngineFixture.Tag(1));
        var documents = Create();
        var first = await documents.GetAsync(TestDocuments.Key);
        first.Content["Db"]!["Host"] = "changed";
        first.Configuration["Db:Host"] = "changed";
        first.Modified!.At = DateTimeOffset.MinValue;
        first.Values["limits"].ShouldBeOfType<JObject>()["max"] = 7;
        _fixture.RespondNotModified(EngineFixture.Tag(1));

        var second = await documents.GetAsync(TestDocuments.Key);

        second.ShouldNotBeSameAs(first);
        second.Content["Db"]!["Host"]!.Value<string>().ShouldBe("db.local");
        second.Configuration["Db:Host"].ShouldBe("db.local");
        second.Modified.ShouldNotBeNull().At.ShouldBe(DateTimeOffset.Parse("2026-10-19T08:30:00+02:00"));
        second.Values["limits"].ShouldBeOfType<JObject>()["max"]!.Value<int>().ShouldBe(5);
        second.Content.ShouldBeSameAs(second.Content);
    }

    [Fact]
    public async Task ResolvedValues_StayInMemory_AndOutOfLogsExceptionsAndText()
    {
        Respond("""{ "Db": { "Password": "kv://my-vault/db-password", "Other": "@secret(api-key)", "Port": "kv://my-vault/port" } }""", EngineFixture.Tag(1));
        _resolver.Secrets["kv://my-vault/port"] = "Port-" + Secret;
        var documents = Create(options => options.ReferenceResolver = _resolver);

        var document = await documents.GetAsync(TestDocuments.Key);
        Should.Throw<StorytellerConfigurationException>(() => document.Get<DbOptions>("Db"));
        _resolver.Respond = (_, _) => throw new SecretResolutionException("The vault is busy.", true, "my-vault", "db-password");
        _fixture.Time.Advance(TimeSpan.FromMinutes(90));
        _fixture.RespondNotModified(EngineFixture.Tag(1));
        var failure = await Should.ThrowAsync<StorytellerConfigurationException>(() => documents.GetAsync(TestDocuments.Key));

        document.Configuration["Db:Password"].ShouldBe(Secret);
        failure.IsTransient.ShouldBeTrue();
        failure.Message.ShouldContain("$.Db.");
        failure.ToString().ShouldNotContain(Secret);
        document.ToString().ShouldNotContain(Secret);
        document.ToString().ShouldContain(TestDocuments.Key);
        _fixture.FileSystem.AllFiles.ShouldBeEmpty();
        var warning = _fixture.Logs.Entries.Where(entry => entry.Level >= LogLevel.Warning).ShouldHaveSingleItem();
        warning.Category.ShouldBe(LogCategory);
        warning.Message.ShouldContain("$.Db.Other");
        _fixture.Logs.Entries.ShouldAllBe(entry => !entry.Message.Contains(Secret, StringComparison.Ordinal));
    }

    [Fact]
    public async Task FailuresToApply_Throw_WithTheirClass()
    {
        var documents = Create(options => options.ReferenceResolver = _resolver);
        Respond("""{ "a:b": 1 }""", EngineFixture.Tag(1));
        (await Should.ThrowAsync<StorytellerConfigurationException>(() => documents.GetAsync(TestDocuments.Key))).IsTransient.ShouldBeFalse();

        _fixture.Http.Respond(HttpStatusCode.UnprocessableEntity, """{ "Message": "No.", "ErrorCode": "binding.evaluation" }""");
        var unprocessable = await Should.ThrowAsync<StorytellerConfigurationException>(() => documents.GetAsync(TestDocuments.Key));
        unprocessable.ErrorCode.ShouldBe("binding.evaluation");
        unprocessable.IsTransient.ShouldBeFalse();

        Respond("""{ "Db": { "Password": "kv://other-vault/x" } }""", EngineFixture.Tag(2));
        (await Should.ThrowAsync<StorytellerConfigurationException>(() => documents.GetAsync(TestDocuments.Key))).Message.ShouldContain("other-vault");
    }

    [Fact]
    public async Task AReadThatNeverAnswers_FailsWithinReadTimeout_AsTransient()
    {
        _fixture.Http.Respond(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return StubHttpHandler.Response(HttpStatusCode.OK);
        });
        var documents = Create(options =>
        {
            options.ReadTimeout = TimeSpan.FromMilliseconds(200);
            options.Resilience.AttemptTimeout = Timeout.InfiniteTimeSpan;
        });

        var exception = await Should.ThrowAsync<StorytellerConfigurationException>(() => documents.GetAsync(TestDocuments.Key).WaitAsync(EngineFixture.Patience));

        exception.IsTransient.ShouldBeTrue();
        exception.Message.ShouldContain("ReadTimeout");
    }

    [Fact]
    public async Task EntriesExpire_AfterTwiceTheSecretRefreshIntervalWithoutACall()
    {
        _fixture.Http.Otherwise = (_, _) => Task.FromResult(Document(Content, EngineFixture.Tag(1)));
        var documents = Create(options => options.SecretRefreshInterval = TimeSpan.FromMinutes(10));
        await documents.GetAsync(TestDocuments.Key);

        _fixture.Time.Advance(TimeSpan.FromMinutes(15));
        _fixture.RespondNotModified(EngineFixture.Tag(1));
        await documents.GetAsync(TestDocuments.Key);
        _fixture.Time.Advance(TimeSpan.FromMinutes(15));
        _fixture.RespondNotModified(EngineFixture.Tag(1));
        await documents.GetAsync(TestDocuments.Key);
        _fixture.Time.Advance(TimeSpan.FromMinutes(21));
        await documents.GetAsync(TestDocuments.Key);

        _fixture.Http.Requests.Select(request => request.Headers.ContainsKey("If-None-Match")).ShouldBe([false, true, true, false]);
    }

    [Fact]
    public async Task TheContainer_DisposesTheService_AndLogsGoToItsLoggerFactory()
    {
        var logs = new RecordingLoggerFactory();
        var services = new ServiceCollection()
            .AddSingleton<ILoggerFactory>(logs)
            .AddStorytellerDocuments(_fixture.Configure(withLogs: false))
            .AddStorytellerDocuments(_fixture.Configure(options => options.Mode = StorytellerReadMode.Effective));
        services.Count(service => service.ServiceType == typeof(IStorytellerDocuments)).ShouldBe(1);
        var container = services.BuildServiceProvider();
        var documents = container.GetRequiredService<IStorytellerDocuments>();
        container.GetRequiredService<IStorytellerDocuments>().ShouldBeSameAs(documents);
        Respond("""{ "Db": { "Password": "@secret(db)" } }""", EngineFixture.Tag(1));
        await documents.GetAsync(TestDocuments.Key);
        var gate = new GatedResponse(() => Document(Content, EngineFixture.Tag(2)));
        _fixture.Http.Respond(gate.RespondAsync);
        var pending = documents.GetAsync(TestDocuments.Key);
        await gate.Entered;

        await container.DisposeAsync();

        await Should.ThrowAsync<ObjectDisposedException>(() => pending.WaitAsync(EngineFixture.Patience));
        Should.Throw<ObjectDisposedException>(() => documents.GetAsync(TestDocuments.Key));
        logs.Entries.ShouldContain(entry => entry.Category == LogCategory && entry.Level == LogLevel.Warning && entry.Message.Contains("$.Db.Password"));
        _fixture.Logs.Entries.ShouldBeEmpty();
    }

    [Fact]
    public void Registration_ValidatesTheOptions()
    {
        _fixture.Environment.Remove("STORYTELLER_ENDPOINT");

        Should.Throw<StorytellerConfigurationException>(() => new ServiceCollection().AddStorytellerDocuments(_fixture.Configure()))
            .Message.ShouldContain("STORYTELLER_ENDPOINT");
    }

    public void Dispose()
    {
        _fixture.Dispose();
    }

    private static string Body(string content, long version = 3)
    {
        return $$"""
            {
              "AnnotationKey": "sbt.northwind",
              "Version": {{version}},
              "Content": {{content}},
              "Modified": { "At": "2026-10-19T08:30:00+02:00", "By": { "Kind": "Account", "Id": "user_1" } },
              "Hash": "a1b2c3d4",
              "Labels": [ "billing", "eu" ],
              "Values": { "tier": "gold", "limits": { "max": 5 } }
            }
            """;
    }

    private static HttpResponseMessage Document(string content, string etag)
    {
        return StubHttpHandler.Response(HttpStatusCode.OK, Body(content), ("ETag", etag), ("Cache-Control", "no-store"));
    }

    private static DocumentIdentity Identity()
    {
        return DocumentIdentity.Create(new Uri(TestDocuments.Endpoint), FullKey.Parse(TestDocuments.Key), StorytellerReadMode.Resolved);
    }

    private void Respond(string content, string etag)
    {
        _fixture.Http.Respond((_, _) => Task.FromResult(Document(content, etag)));
    }

    private IStorytellerDocuments Create(Action<StorytellerConfigurationOptions>? configure = null)
    {
        var container = _fixture.Track(new ServiceCollection().AddStorytellerDocuments(_fixture.Configure(configure)).BuildServiceProvider());
        return container.GetRequiredService<IStorytellerDocuments>();
    }

    public sealed class DbOptions
    {
        public string? Host { get; set; }

        public int Port { get; set; }

        public string? Password { get; set; }

        public List<string> Hosts { get; set; } = [];
    }

    public sealed class DocumentOptions
    {
        public DbOptions? Db { get; set; }
    }
}

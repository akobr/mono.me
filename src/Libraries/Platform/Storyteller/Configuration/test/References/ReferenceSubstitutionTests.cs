using _42.Platform.Storyteller.Sdk.Configuring.References;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Sdk.Configuring.UnitTests.References;

public class ReferenceSubstitutionTests
{
    private const string Content = """
        {
          "Db": {
            "Host": "db.local",
            "Password": " kv://my-vault/db-password ",
            "Port": 5432,
            "Replicas": [ "aws-sm://replica", "plain", null, { "Token": "aws-sm://token" } ]
          },
          "Other": "kv://my-vault/db-password"
        }
        """;

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 23, 8, 0, 0, TimeSpan.Zero));
    private readonly FakeResolver _resolver = new()
    {
        Secrets =
        {
            ["kv://my-vault/db-password"] = "p@ss",
            ["aws-sm://replica"] = "replica-secret",
            ["aws-sm://token"] = "token-secret",
        },
    };

    [Fact]
    public async Task AcceptedValues_AreReplaced_InACopy()
    {
        var content = JObject.Parse(Content);
        var original = content.ToString();

        var result = await Create().SubstituteAsync(content, CancellationToken.None);

        result.Content.ShouldNotBeSameAs(content);
        content.ToString().ShouldBe(original);
        result.Content.SelectToken("Db.Password")!.Value<string>().ShouldBe("p@ss");
        result.Content.SelectToken("Other")!.Value<string>().ShouldBe("p@ss");
        result.Content.SelectToken("Db.Replicas[0]")!.Value<string>().ShouldBe("replica-secret");
        result.Content.SelectToken("Db.Replicas[1]")!.Value<string>().ShouldBe("plain");
        result.Content.SelectToken("Db.Replicas[2]")!.Type.ShouldBe(JTokenType.Null);
        result.Content.SelectToken("Db.Replicas[3].Token")!.Value<string>().ShouldBe("token-secret");
        result.Content.SelectToken("Db.Port")!.Value<int>().ShouldBe(5432);
        result.OldestResolution.ShouldBe(_time.GetUtcNow());
        result.Unresolved.ShouldBeEmpty();

        // The trimmed value is what the resolver sees; the same text is resolved once.
        _resolver.Resolved.ShouldBe(["kv://my-vault/db-password", "aws-sm://replica", "aws-sm://token"]);
    }

    [Fact]
    public async Task ADocumentWithoutAcceptedValues_IsReturnedAsItIs()
    {
        var content = JObject.Parse("""{ "Db": { "Host": "db.local", "Port": 5432 } }""");

        var result = await Create().SubstituteAsync(content, CancellationToken.None);

        result.Content.ShouldBeSameAs(content);
        result.OldestResolution.ShouldBeNull();
    }

    [Fact]
    public async Task PropertyNames_AreNeverResolved()
    {
        _resolver.Accepts = value => value.StartsWith("aws-sm://", StringComparison.Ordinal);
        _resolver.Respond = (_, _) => Task.FromResult("secret");
        var content = JObject.Parse("""{ "aws-sm://name": "aws-sm://value", "aws-sm://group": { "aws-sm://inner": "plain" } }""");

        var result = await Create().SubstituteAsync(content, CancellationToken.None);

        result.Content["aws-sm://name"]!.Value<string>().ShouldBe("secret");
        result.Content.SelectToken("['aws-sm://group']['aws-sm://inner']")!.Value<string>().ShouldBe("plain");
        _resolver.Asked.ShouldBe(["aws-sm://value", "plain"]);
    }

    [Fact]
    public async Task TheEffectiveMode_ResolvesNothing()
    {
        var content = JObject.Parse(Content);

        var result = await Create(mode: StorytellerReadMode.Effective).SubstituteAsync(content, CancellationToken.None);

        result.Content.ShouldBeSameAs(content);
        result.Unresolved.ShouldBeEmpty();
        _resolver.Asked.ShouldBeEmpty();
    }

    [Fact]
    public async Task WithoutAResolver_ReferencesStay_AndTheirPathsAreReported()
    {
        var content = JObject.Parse("""
            {
              "Db": { "Password": "kv://my-vault/db-password", "Uri": "https://vault.vault.azure.net/secrets/name", "Plain": "aws-sm://replica" },
              "List": [ "{\"uri\":\"https://vault.vault.azure.net/secrets/name\"}" ]
            }
            """);
        var substitution = new ReferenceSubstitution(null, StorytellerReadMode.Resolved, TimeSpan.FromHours(1), _time);

        var result = await substitution.SubstituteAsync(content, CancellationToken.None);

        result.Content.ShouldBeSameAs(content);
        result.HasResolver.ShouldBeFalse();
        result.Unresolved.Select(reference => reference.Path).ShouldBe(["$.Db.Password", "$.Db.Uri", "$.List[0]"]);

        var logger = new RecordingLoggerFactory();
        substitution.WarnUnresolved(logger.CreateLogger("test"), TestDocuments.Key, result);

        var warning = logger.Entries.ShouldHaveSingleItem();
        warning.Level.ShouldBe(LogLevel.Warning);
        warning.Message.ShouldContain("no ReferenceResolver is set");
        warning.Message.ShouldContain("$.Db.Password, $.Db.Uri, $.List[0]");
    }

    [Fact]
    public async Task AnUnclaimedShorthand_FailsDefinitively()
    {
        _resolver.Accepts = value => value.StartsWith("aws-sm://", StringComparison.Ordinal);

        var exception = await Should.ThrowAsync<SecretResolutionException>(() => Create().SubstituteAsync(JObject.Parse(Content), CancellationToken.None).AsTask());

        exception.IsTransient.ShouldBeFalse();
        exception.VaultName.ShouldBe("my-vault");
        exception.SecretName.ShouldBe("db-password");
        exception.Message.ShouldContain("$.Db.Password");
    }

    [Fact]
    public async Task OtherUnclaimedReferences_StayAsWritten_WithTheirPathAndHost()
    {
        _resolver.Accepts = value => value.StartsWith("aws-sm://", StringComparison.Ordinal);
        _resolver.Respond = (_, _) => Task.FromResult("secret");
        var content = JObject.Parse("""{ "Uri": "https://other.vault.example/secrets/x", "App": "{\"uri\":\"https://kv.vault.azure.net/secrets/y\"}", "Aws": "aws-sm://x" }""");
        var substitution = Create();

        var result = await substitution.SubstituteAsync(content, CancellationToken.None);

        result.Content["Uri"]!.Value<string>().ShouldBe("https://other.vault.example/secrets/x");
        result.Content["Aws"]!.Value<string>().ShouldBe("secret");
        result.Unresolved.ShouldBe([new UnresolvedReference("$.Uri", "other.vault.example"), new UnresolvedReference("$.App", "kv.vault.azure.net")]);

        var logger = new RecordingLoggerFactory();
        substitution.WarnUnresolved(logger.CreateLogger("test"), TestDocuments.Key, result);

        var warning = logger.Entries.ShouldHaveSingleItem();
        warning.Message.ShouldContain("no resolver accepts");
        warning.Message.ShouldContain("$.Uri (other.vault.example), $.App (kv.vault.azure.net)");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AFailure_KeepsItsClass_AndNamesThePath(bool isTransient)
    {
        _resolver.Respond = (_, _) => throw new SecretResolutionException("The vault said no.", isTransient, "my-vault", "db-password");

        var exception = await Should.ThrowAsync<SecretResolutionException>(() => Create().SubstituteAsync(JObject.Parse(Content), CancellationToken.None).AsTask());

        exception.IsTransient.ShouldBe(isTransient);
        exception.VaultName.ShouldBe("my-vault");
        exception.SecretName.ShouldBe("db-password");
        exception.Message.ShouldBe("The secret reference at $.Db.Password could not be resolved: The vault said no.");
    }

    [Fact]
    public async Task AnyOtherException_OfTheResolver_IsDefinitive()
    {
        _resolver.Respond = (_, _) => throw new InvalidOperationException("Something inside the resolver.");

        var exception = await Should.ThrowAsync<SecretResolutionException>(() => Create().SubstituteAsync(JObject.Parse(Content), CancellationToken.None).AsTask());

        exception.IsTransient.ShouldBeFalse();
        exception.Message.ShouldBe("Resolving the secret reference at $.Db.Password failed with InvalidOperationException.");
        exception.InnerException.ShouldBeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task AThrowingCanResolve_IsADefinitiveFailure()
    {
        _resolver.Accepts = _ => throw new FormatException("No.");

        var exception = await Should.ThrowAsync<SecretResolutionException>(() => Create().SubstituteAsync(JObject.Parse(Content), CancellationToken.None).AsTask());

        exception.IsTransient.ShouldBeFalse();
        exception.Message.ShouldContain("$.Db.Host");
    }

    [Fact]
    public async Task ATimeoutOfTheResolversOwn_IsTransient()
    {
        _resolver.Respond = (_, _) => throw new TaskCanceledException("The resolver's own timeout.");

        var exception = await Should.ThrowAsync<SecretResolutionException>(() => Create().SubstituteAsync(JObject.Parse(Content), CancellationToken.None).AsTask());

        exception.IsTransient.ShouldBeTrue();
    }

    [Fact]
    public async Task ANullValue_IsADefinitiveFailure()
    {
        _resolver.Respond = (_, _) => Task.FromResult<string>(null!);

        var exception = await Should.ThrowAsync<SecretResolutionException>(() => Create().SubstituteAsync(JObject.Parse(Content), CancellationToken.None).AsTask());

        exception.IsTransient.ShouldBeFalse();
    }

    [Fact]
    public async Task TheCallersCancellation_ComesOut_AlsoFromAResolverThatIgnoresIt()
    {
        var resolving = new TaskCompletionSource<string>();
        _resolver.Respond = (_, _) => resolving.Task;
        using var cancellation = new CancellationTokenSource();

        var substituting = Create().SubstituteAsync(JObject.Parse(Content), cancellation.Token).AsTask();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => substituting);
        resolving.SetException(new InvalidOperationException("Later."));
    }

    [Fact]
    public async Task ResolvedValues_AreReused_UntilSecretRefreshInterval()
    {
        var substitution = Create();
        var first = await substitution.SubstituteAsync(JObject.Parse(Content), CancellationToken.None);
        _resolver.Secrets["kv://my-vault/db-password"] = "rotated";
        _time.Advance(TimeSpan.FromMinutes(59));

        // A changed document reuses the fresh value.
        var changed = await substitution.SubstituteAsync(JObject.Parse("""{ "Password": "kv://my-vault/db-password", "New": "aws-sm://token" }"""), CancellationToken.None);

        changed.Content["Password"]!.Value<string>().ShouldBe("p@ss");
        changed.OldestResolution.ShouldBe(first.OldestResolution);
        substitution.IsRefreshDue(changed).ShouldBeFalse();
        _resolver.Resolved.Count.ShouldBe(3);

        // After the interval, the expired values are resolved again.
        _time.Advance(TimeSpan.FromMinutes(1));
        substitution.IsRefreshDue(changed).ShouldBeTrue();

        var refreshed = await substitution.SubstituteAsync(JObject.Parse(Content), CancellationToken.None);

        refreshed.Content.SelectToken("Db.Password")!.Value<string>().ShouldBe("rotated");
        refreshed.OldestResolution.ShouldBe(_time.GetUtcNow());
        _resolver.Resolved.Count.ShouldBe(6);
    }

    [Fact]
    public async Task WithoutSharedValues_OnlyTheFreshValuesOfTheGivenResultAreReused()
    {
        var substitution = new ReferenceSubstitution(_resolver, StorytellerReadMode.Resolved, TimeSpan.FromHours(1), _time, sharesValues: false);
        var first = await substitution.SubstituteAsync(JObject.Parse(Content), CancellationToken.None);
        first.ResolvedValues.Keys.ShouldBe(["kv://my-vault/db-password", "aws-sm://replica", "aws-sm://token"], ignoreOrder: true);
        _resolver.Secrets["kv://my-vault/db-password"] = "rotated";
        _time.Advance(TimeSpan.FromMinutes(30));

        // Without the earlier result, every value is resolved again; with it, its fresh values are reused.
        (await substitution.SubstituteAsync(JObject.Parse(Content), CancellationToken.None)).Content["Other"]!.Value<string>().ShouldBe("rotated");
        _resolver.Resolved.Count.ShouldBe(6);
        var reused = await substitution.SubstituteAsync(JObject.Parse(Content), first, CancellationToken.None);
        reused.Content["Other"]!.Value<string>().ShouldBe("p@ss");
        reused.OldestResolution.ShouldBe(first.OldestResolution);
        _resolver.Resolved.Count.ShouldBe(6);

        // An expired value of the earlier result is resolved again.
        _time.Advance(TimeSpan.FromMinutes(30));
        (await substitution.SubstituteAsync(JObject.Parse(Content), first, CancellationToken.None)).Content["Other"]!.Value<string>().ShouldBe("rotated");
        _resolver.Resolved.Count.ShouldBe(9);
        first.ToString().ShouldNotContain("p@ss");
    }

    [Fact]
    public async Task AFailedResolution_IsNotCached()
    {
        var fail = true;
        _resolver.Respond = (value, _) => fail ? throw new SecretResolutionException("Busy.", true) : Task.FromResult("p@ss");
        var substitution = Create();
        var content = JObject.Parse("""{ "Password": "kv://my-vault/db-password" }""");

        await Should.ThrowAsync<SecretResolutionException>(() => substitution.SubstituteAsync(content, CancellationToken.None).AsTask());
        fail = false;

        (await substitution.SubstituteAsync(content, CancellationToken.None)).Content["Password"]!.Value<string>().ShouldBe("p@ss");
    }

    [Fact]
    public void TheResult_NeverPrintsItsContent()
    {
        var result = new SubstitutionResult(JObject.Parse("""{ "Password": "p@ss" }"""), _time.GetUtcNow());

        result.ToString().ShouldNotContain("p@ss");
    }

    private ReferenceSubstitution Create(StorytellerReadMode mode = StorytellerReadMode.Resolved)
    {
        return new ReferenceSubstitution(_resolver, mode, TimeSpan.FromHours(1), _time);
    }
}

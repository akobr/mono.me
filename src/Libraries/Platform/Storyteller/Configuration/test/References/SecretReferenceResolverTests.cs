using _42.Platform.Storyteller.Sdk.Configuring.References;
using Microsoft.Extensions.Time.Testing;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Sdk.Configuring.UnitTests.References;

public class SecretReferenceResolverTests
{
    [Fact]
    public async Task FirstOf_ResolvesWithTheFirstResolverThatAccepts()
    {
        var vault = new PrefixResolver("kv://", "from-vault");
        var other = new PrefixResolver("kv://", "from-other");
        var aws = new PrefixResolver("aws://", "from-aws");
        var resolver = SecretReferenceResolver.FirstOf(vault, other, aws);

        resolver.CanResolve("kv://vault/name").ShouldBeTrue();
        resolver.CanResolve("aws://secret").ShouldBeTrue();
        resolver.CanResolve("plain").ShouldBeFalse();

        (await resolver.ResolveAsync("kv://vault/name", CancellationToken.None)).ShouldBe("from-vault");
        (await resolver.ResolveAsync("aws://secret", CancellationToken.None)).ShouldBe("from-aws");
        other.Resolved.ShouldBe(0);
    }

    [Fact]
    public async Task FirstOf_RefusesAValueNoResolverAccepts()
    {
        var resolver = SecretReferenceResolver.FirstOf();

        var exception = await Should.ThrowAsync<SecretResolutionException>(() => resolver.ResolveAsync("kv://vault/name", CancellationToken.None).AsTask());

        exception.IsTransient.ShouldBeFalse();
        exception.Message.ShouldNotContain("kv://");
    }

    [Fact]
    public void FirstOf_RefusesNullResolvers()
    {
        Should.Throw<ArgumentNullException>(() => SecretReferenceResolver.FirstOf(null!));
        Should.Throw<ArgumentException>(() => SecretReferenceResolver.FirstOf(new PrefixResolver("a", "b"), null!));
    }

    [Fact]
    public void FirstOf_KeepsItsOwnCopy()
    {
        var resolvers = new ISecretReferenceResolver[] { new PrefixResolver("kv://", "x") };
        var resolver = SecretReferenceResolver.FirstOf(resolvers);

        resolvers[0] = new PrefixResolver("aws://", "y");

        resolver.CanResolve("kv://vault/name").ShouldBeTrue();
    }

    [Fact]
    public void ASecretResolutionException_NamesTheVaultAndTheSecret()
    {
        var exception = new SecretResolutionException("The secret db-password of myvault is forbidden.", false, "myvault", "db-password");

        exception.VaultName.ShouldBe("myvault");
        exception.SecretName.ShouldBe("db-password");
        exception.IsTransient.ShouldBeFalse();
    }

    [Theory]
    [InlineData(120, true)]
    [InlineData(30, false)]
    public void ResolvedReferences_AreDueAfterTheSecretRefreshInterval(int minutesAgo, bool isDue)
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 23, 8, 0, 0, TimeSpan.Zero));
        var substitution = new ReferenceSubstitution(new PrefixResolver("kv://", "secret"), StorytellerReadMode.Resolved, TimeSpan.FromHours(1), time);

        substitution.IsRefreshDue(Resolved(time, minutesAgo)).ShouldBe(isDue);
    }

    [Fact]
    public void AnInfiniteSecretRefreshInterval_NeverExpires()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 23, 8, 0, 0, TimeSpan.Zero));
        var substitution = new ReferenceSubstitution(new PrefixResolver("kv://", "secret"), StorytellerReadMode.Resolved, Timeout.InfiniteTimeSpan, time);

        substitution.IsRefreshDue(Resolved(time, 60 * 24 * 365)).ShouldBeFalse();
    }

    private static SubstitutionResult Resolved(TimeProvider time, int minutesAgo)
    {
        return new SubstitutionResult(new JObject(), time.GetUtcNow().AddMinutes(-minutesAgo));
    }

    private sealed class PrefixResolver : ISecretReferenceResolver
    {
        private readonly string _prefix;
        private readonly string _value;

        public PrefixResolver(string prefix, string value)
        {
            _prefix = prefix;
            _value = value;
        }

        public int Resolved { get; private set; }

        public bool CanResolve(string value)
        {
            return value.StartsWith(_prefix, StringComparison.Ordinal);
        }

        public ValueTask<string> ResolveAsync(string value, CancellationToken cancellationToken)
        {
            Resolved++;
            return ValueTask.FromResult(_value);
        }
    }
}

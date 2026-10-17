using _42.Platform.Storyteller.Accessing.Federation;

namespace _42.Platform.Storyteller.Access.Federation.UnitTests;

// The in-memory stores behave like the Cosmos DB ones, so tests and local runs see the same rules.
public class InMemoryStoresTests
{
    [Fact]
    public async Task IssuerStore_ListsByNameAndKeepsProjectsApart()
    {
        var store = new InMemoryFederatedIssuerStore();
        await store.SetAsync("org1", "proj1", Issuer("zeta"));
        await store.SetAsync("org1", "proj1", Issuer("alpha"));
        await store.SetAsync("org1", "proj2", Issuer("beta"));

        (await store.ListAsync("org1", "proj1")).Select(issuer => issuer.Name).ShouldBe(["alpha", "zeta"]);
        (await store.ListAsync("org2", "proj1")).ShouldBeEmpty();
        (await store.GetAsync("org1", "proj1", "alpha")).ShouldNotBeNull();
        (await store.GetAsync("org1", "proj1", "beta")).ShouldBeNull();
        (await store.GetAsync("org1", "proj1", "Not A Name")).ShouldBeNull();
    }

    [Fact]
    public async Task IssuerStore_ReplacesAnIssuerButNotItsIssuerString()
    {
        var store = new InMemoryFederatedIssuerStore();
        await store.SetAsync("org1", "proj1", Issuer("cluster"));

        await store.SetAsync("org1", "proj1", Issuer("cluster") with { Description = "Production" });
        (await store.GetAsync("org1", "proj1", "cluster"))!.Description.ShouldBe("Production");

        var exception = await Should.ThrowAsync<InvalidInputException>(
            () => store.SetAsync("org1", "proj1", Issuer("cluster") with { Issuer = "https://other.example" }));
        exception.Message.ShouldBe(FederationValidation.IssuerImmutableMessage);
        exception.ErrorCode.ShouldBe(ErrorCodes.InvalidFederation);
    }

    [Fact]
    public async Task IssuerStore_RefusesTheIssuerAfterTheLimit_AndAnInvalidName()
    {
        var store = new InMemoryFederatedIssuerStore();

        for (var index = 0; index < FederationValidation.MaxIssuersPerProject; index++)
        {
            await store.SetAsync("org1", "proj1", Issuer($"issuer-{index}"));
        }

        (await Should.ThrowAsync<InvalidInputException>(() => store.SetAsync("org1", "proj1", Issuer("one-too-many"))))
            .Message.ShouldBe(FederationValidation.TooManyIssuersMessage);
        await Should.ThrowAsync<InvalidInputException>(() => new InMemoryFederatedIssuerStore().SetAsync("org1", "proj1", Issuer("Bad_Name")));

        // Replacing an existing one still works at the limit.
        await store.SetAsync("org1", "proj1", Issuer("issuer-3") with { Description = "kept" });
    }

    [Fact]
    public async Task IssuerStore_DeletesOnce()
    {
        var store = new InMemoryFederatedIssuerStore();
        await store.SetAsync("org1", "proj1", Issuer("cluster"));

        (await store.DeleteAsync("org1", "proj1", "cluster")).ShouldBeTrue();
        (await store.DeleteAsync("org1", "proj1", "cluster")).ShouldBeFalse();
        (await store.DeleteAsync("org9", "proj1", "cluster")).ShouldBeFalse();
    }

    [Fact]
    public async Task SigningKeyStore_KeepsTheFirstKey()
    {
        var store = new InMemoryTokenSigningKeyStore();
        var first = new TokenSigningKey("kid-1", "RS256", [1, 2, 3], DateTimeOffset.UtcNow);

        (await store.GetAsync()).ShouldBeNull();
        (await store.TryCreateAsync(first)).ShouldBeTrue();
        (await store.TryCreateAsync(first with { Kid = "kid-2" })).ShouldBeFalse();
        (await store.GetAsync()).ShouldBe(first);
    }

    private static FederatedIssuer Issuer(string name)
    {
        return new FederatedIssuer { Name = name, Issuer = $"https://{name}.example", KeySource = FederatedIssuerKeySource.Discovery };
    }
}

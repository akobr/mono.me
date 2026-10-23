#nullable enable

using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Binding;
using _42.Platform.Storyteller.Configuring;
using Azure;
using Azure.Security.KeyVault.Secrets;
using FluentAssertions;
using Microsoft.Extensions.Azure;
using Moq;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// What the Key Vault binding source records into the manifest of a resolve; no vault needed.
public class KeyVaultBindingSourceTests
{
    private const string VaultName = "primaryVault";

    private readonly Mock<SecretClient> _client = new();

    [Fact]
    public async Task ResolveAsync_ASecret_RecordsItsVersionAndMakesItVolatile()
    {
        var secret = SecretModelFactory.KeyVaultSecret(SecretModelFactory.SecretProperties(name: "db--password", version: "v7"), "s3cret");
        _client.Setup(client => client.GetSecretAsync("db--password", It.IsAny<string>(), It.IsAny<SecretContentType?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(secret, Mock.Of<Response>()));
        var dependencies = Recording();

        var value = await Source().ResolveAsync(Request(includeSecrets: true, dependencies));

        value!.Token.ToString().Should().Be("s3cret");
        dependencies.IsVolatile.Should().BeTrue("a secret has no version check cheaper than reading it");
        dependencies.ToList().Should().ContainSingle().Which.Should().Be(
            new BindingDependency(BindingDependencyKinds.KeyVault, VaultName, "db--password", "v7"));
    }

    [Fact]
    public async Task ResolveAsync_AMissingSecret_RecordsAbsentAndMakesItVolatile()
    {
        _client.Setup(client => client.GetSecretAsync("db--password", It.IsAny<string>(), It.IsAny<SecretContentType?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(404, "Not found"));
        var dependencies = Recording();

        var value = await Source().ResolveAsync(Request(includeSecrets: true, dependencies));

        value.Should().BeNull();
        dependencies.IsVolatile.Should().BeTrue();
        dependencies.ToList().Should().ContainSingle().Which.Marker.Should().Be(BindingDependency.Absent);
    }

    [Fact]
    public async Task ResolveAsync_WithoutSecrets_ReadsAndRecordsNothing()
    {
        var dependencies = Recording();

        var value = await Source().ResolveAsync(Request(includeSecrets: false, dependencies));

        value.Should().BeNull();
        _client.VerifyNoOtherCalls();
        dependencies.IsVolatile.Should().BeFalse("a caller without secret rights gets the string as stored, which the tag covers");
        dependencies.ToList().Should().BeEmpty();
    }

    [Fact]
    public void Source_Records()
    {
        Source().Should().BeAssignableTo<IRecordingBinding>();
    }

    private static BindingDependencies Recording()
    {
        return new BindingScope { Dependencies = new BindingDependencies() }.Dependencies!;
    }

    private static BindingRequest Request(bool includeSecrets, BindingDependencies dependencies)
    {
        return new BindingRequest { Path = ["db", "password"], IncludeSecrets = includeSecrets, Dependencies = dependencies };
    }

    private KeyVaultBindingSource Source()
    {
        var factory = new Mock<IAzureClientFactory<SecretClient>>();
        factory.Setup(clients => clients.CreateClient(VaultName)).Returns(_client.Object);
        return new KeyVaultBindingSource(factory.Object, VaultName);
    }
}

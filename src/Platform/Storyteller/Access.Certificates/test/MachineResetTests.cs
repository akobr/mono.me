using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace _42.Platform.Storyteller.Access.Certificates.UnitTests;

// The API key reset replaces the stored hash by compare-and-set, and a Certificate machine has no key to reset.
// The hash store is strict, so any call a test does not expect fails it.
public class MachineResetTests
{
    private const string MachineId = "3b0f9d3e-8f43-4a8e-9a0c-2f7d3c1e5b6a";

    private static readonly MachineScope[] Granted = [MachineScope.AnnotationRead, MachineScope.ConfigurationRead];

    private readonly Mock<IApiKeyHashStore> _hashStore = new(MockBehavior.Strict);

    [Fact]
    public async Task ApiKeyReset_ReplacesTheStoredHash_WithTheHashOfTheNewKey()
    {
        var service = new ApiKeyMachineAccessService(_hashStore.Object);
        string? newHash = null;
        SetStoredHash("stored-hash");
        _hashStore.Setup(store => store.ReplaceAsync("org1", "proj1", MachineId, "stored-hash", It.IsAny<string>()))
            .Callback((string _, string _, string _, string _, string hash) => newHash = hash)
            .ReturnsAsync(true);

        var key = await service.ResetMachineAccessAsync(MachineId, "org1", "proj1");

        key.ShouldNotBeNull().ShouldStartWith("2s.");
        newHash.ShouldNotBeNull().ShouldNotBe("stored-hash");

        // The key is the one of the hash written, with the scopes as stored.
        SetStoredHash(newHash);
        (await service.ValidateAsync(key)).ShouldNotBeNull().Scopes.ShouldBe(Granted);
    }

    [Fact]
    public async Task ApiKeyReset_WhenTheHashChangedMeanwhile_IsAConflict()
    {
        SetStoredHash("stored-hash");
        _hashStore.Setup(store => store.ReplaceAsync("org1", "proj1", MachineId, "stored-hash", It.IsAny<string>()))
            .ReturnsAsync(false);

        var exception = await Should.ThrowAsync<ConflictException>(
            () => new ApiKeyMachineAccessService(_hashStore.Object).ResetMachineAccessAsync(MachineId, "org1", "proj1"));

        exception.Message.ShouldBe($"The API key of the machine access {MachineId} was reset concurrently; reload and retry.");
        exception.ErrorCode.ShouldBe(ErrorCodes.Conflict);
    }

    // The store's 404 (the machine is gone or being deleted) stays a 404; only a false from the store is a conflict.
    [Fact]
    public async Task ApiKeyReset_WhenTheMachineIsGoneAtTheReplace_PropagatesNotFound()
    {
        var notFound = new NotFoundException($"The machine access {MachineId} has not been found.");
        SetStoredHash("stored-hash");
        _hashStore.Setup(store => store.ReplaceAsync("org1", "proj1", MachineId, "stored-hash", It.IsAny<string>()))
            .ThrowsAsync(notFound);

        var exception = await Should.ThrowAsync<NotFoundException>(
            () => new ApiKeyMachineAccessService(_hashStore.Object).ResetMachineAccessAsync(MachineId, "org1", "proj1"));

        exception.ShouldBeSameAs(notFound);
    }

    [Fact]
    public async Task ApiKeyReset_WithoutAStoredHash_ReturnsNull_AndWritesNothing()
    {
        _hashStore.Setup(store => store.GetAsync("org1", "proj1", MachineId)).ReturnsAsync((ApiKeyHashEntry?)null);

        (await new ApiKeyMachineAccessService(_hashStore.Object).ResetMachineAccessAsync(MachineId, "org1", "proj1")).ShouldBeNull();

        _hashStore.Verify(store => store.GetAsync("org1", "proj1", MachineId), Times.Once);
        _hashStore.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RouterReset_OfACertificateMachine_IsNotSupported_AndNeverReachesTheApiKeyService()
    {
        var exception = await Should.ThrowAsync<MachineAccessNotSupportedException>(
            () => CreateRouter().ResetMachineAccessAsync(Access(MachineCredentialKind.Certificate), "org1", "proj1"));

        exception.Message.ShouldBe(PolicyAwareMachineAccessService.CertificateResetNotSupportedMessage);
        exception.ErrorCode.ShouldBe(ErrorCodes.MachineAccessNotSupported);
        _hashStore.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(MachineCredentialKind.ApiKey)]
    [InlineData(MachineCredentialKind.CertificateAndApiKey)]
    public async Task RouterReset_OfAMachineWithAnApiKey_ReplacesItsHash(MachineCredentialKind kind)
    {
        SetStoredHash("stored-hash");
        _hashStore.Setup(store => store.ReplaceAsync("org1", "proj1", MachineId, "stored-hash", It.IsAny<string>()))
            .ReturnsAsync(true);

        (await CreateRouter().ResetMachineAccessAsync(Access(kind), "org1", "proj1")).ShouldNotBeNull();

        _hashStore.Verify(store => store.ReplaceAsync("org1", "proj1", MachineId, "stored-hash", It.IsAny<string>()), Times.Once);
    }

    private static MachineAccess Access(MachineCredentialKind kind)
    {
        return new MachineAccess
        {
            Id = MachineId,
            ObjectId = MachineId,
            AccessKey = "2s.***",
            Scopes = Granted,
            CredentialKind = kind,
        };
    }

    private void SetStoredHash(string hash)
    {
        _hashStore.Setup(store => store.GetAsync("org1", "proj1", MachineId)).ReturnsAsync(new ApiKeyHashEntry(hash, Granted));
    }

    // The certificate service and the policy store are not needed by a reset; reaching them fails the test.
    private PolicyAwareMachineAccessService CreateRouter()
    {
        return new PolicyAwareMachineAccessService(
            new ApiKeyMachineAccessService(_hashStore.Object),
            null!,
            Mock.Of<IMachineAuthenticationPolicyStore>(MockBehavior.Strict),
            Options.Create(TestCertificateHelper.DefaultOptions),
            NullLogger<PolicyAwareMachineAccessService>.Instance);
    }
}

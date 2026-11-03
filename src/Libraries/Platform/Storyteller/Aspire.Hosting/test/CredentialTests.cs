using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace _42.Platform.Storyteller.AspireHosting.UnitTests;

public sealed class CredentialTests
{
    public enum Credential
    {
        ApiKey,
        ClientCredentials,
        ClientCertificate,
    }

    // The SDK reads one credential from the variables: one kind, or an API key with a client certificate.
    [Theory]
    [InlineData(Credential.ApiKey, Credential.ApiKey, "an API key")]
    [InlineData(Credential.ApiKey, Credential.ClientCredentials, "an API key")]
    [InlineData(Credential.ClientCredentials, Credential.ApiKey, "client credentials")]
    [InlineData(Credential.ClientCredentials, Credential.ClientCredentials, "client credentials")]
    [InlineData(Credential.ClientCredentials, Credential.ClientCertificate, "client credentials")]
    [InlineData(Credential.ClientCertificate, Credential.ClientCredentials, "a client certificate")]
    [InlineData(Credential.ClientCertificate, Credential.ClientCertificate, "a client certificate")]
    public void ASecondCredential_ThatTheSdkRefuses_FailsWhenItIsAdded(Credential first, Credential second, string existing)
    {
        var host = TestAppHost.Run();
        var storyteller = host.Builder.AddStoryteller("storyteller", "https://storyteller.example.com/api");
        Add(host, storyteller, first);

        var exception = Should.Throw<InvalidOperationException>(() => Add(host, storyteller, second));

        exception.Message.ShouldContain($"'storyteller' already has {existing}");
        exception.Message.ShouldContain("Add another Storyteller resource");
    }

    [Theory]
    [InlineData(Credential.ApiKey, Credential.ClientCertificate)]
    [InlineData(Credential.ClientCertificate, Credential.ApiKey)]
    public void AnApiKeyAndAClientCertificate_CombineInEitherOrder(Credential first, Credential second)
    {
        var host = TestAppHost.Run();
        var storyteller = host.Builder.AddStoryteller("storyteller", "https://storyteller.example.com/api");

        Add(host, storyteller, first);
        Add(host, storyteller, second);

        storyteller.Resource.ApiKey.ShouldNotBeNull();
        storyteller.Resource.CertificatePath.ShouldNotBeNull();
    }

    [Fact]
    public void AThirdCredential_AfterAnApiKeyAndACertificate_Fails()
    {
        var host = TestAppHost.Run();
        var storyteller = host.Builder.AddStoryteller("storyteller", "https://storyteller.example.com/api");
        Add(host, storyteller, Credential.ApiKey);
        Add(host, storyteller, Credential.ClientCertificate);

        Should.Throw<InvalidOperationException>(() => Add(host, storyteller, Credential.ClientCredentials))
            .Message.ShouldContain("already has an API key and a client certificate");
    }

    [Fact]
    public void ARelativeCertificatePath_IsMadeAbsolute_AgainstTheAppHostDirectory()
    {
        var host = TestAppHost.Run();

        var storyteller = host.Builder.AddStoryteller("storyteller", "https://storyteller.example.com/api")
            .WithClientCertificate(Path.Combine("..", "certificates", "machine.pfx"));

        host.Builder.AppHostDirectory.ShouldBe(TestAppHost.Directory);
        storyteller.Resource.CertificatePath.ShouldBe(Path.GetFullPath(Path.Combine(TestAppHost.Directory, "..", "certificates", "machine.pfx")));
    }

    [Fact]
    public void AnAbsoluteCertificatePath_IsKept()
    {
        var host = TestAppHost.Run();
        var path = Path.Combine(Path.GetTempPath(), "machines", "machine.pem");

        var storyteller = host.Builder.AddStoryteller("storyteller", "https://storyteller.example.com/api").WithClientCertificate(path);

        storyteller.Resource.CertificatePath.ShouldBe(path);
    }

    [Theory]
    [InlineData("", null, "tokenUrl")]
    [InlineData(null, " ", "tokenScope")]
    public void AnEmptyTokenUrlOrScope_Fails_AsNullLeavesItUnset(string? tokenUrl, string? tokenScope, string parameterName)
    {
        var host = TestAppHost.Run();
        var storyteller = host.Builder.AddStoryteller("storyteller", "https://storyteller.example.com/api");

        Should.Throw<ArgumentException>(() => storyteller.WithClientCredentials(
                host.Builder.AddParameter("storyteller-client-id"),
                host.Builder.AddParameter("storyteller-client-secret", secret: true),
                tokenUrl,
                tokenScope))
            .ParamName.ShouldBe(parameterName);
        storyteller.Resource.ClientId.ShouldBeNull();
    }

    [Fact]
    public void AnEmptyCertificatePath_Fails()
    {
        var host = TestAppHost.Run();
        var storyteller = host.Builder.AddStoryteller("storyteller", "https://storyteller.example.com/api");

        Should.Throw<ArgumentException>(() => storyteller.WithClientCertificate(" ")).ParamName.ShouldBe("path");
    }

    private static void Add(TestAppHost host, IResourceBuilder<StorytellerResource> storyteller, Credential credential)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        switch (credential)
        {
            case Credential.ApiKey:
                storyteller.WithApiKey(host.Builder.AddParameter($"api-key-{suffix}", secret: true));
                break;
            case Credential.ClientCredentials:
                storyteller.WithClientCredentials(host.Builder.AddParameter($"client-id-{suffix}"), host.Builder.AddParameter($"client-secret-{suffix}", secret: true));
                break;
            default:
                storyteller.WithClientCertificate($"machine-{suffix}.pem");
                break;
        }
    }
}

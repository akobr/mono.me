using System.IO.Abstractions.TestingHelpers;
using System.Net.Http.Headers;
using _42.Platform.Storyteller.Sdk.Credentials;

namespace _42.Platform.Storyteller.Sdk.UnitTests.Credentials;

public class SimpleCredentialTests
{
    private const string ApiKey = "2s.YWNtZTpiaWxsaW5nOm1hY2hpbmUtMQ.s3cr3t-value";

    private static readonly StorytellerCredentialBinding Binding = new(new Uri("https://api.example.com/api"));

    [Theory]
    [InlineData(ApiKey)]
    [InlineData("  " + ApiKey + "  ")]
    [InlineData("ApiKey " + ApiKey)]
    [InlineData("apikey   " + ApiKey)]
    public async Task ApiKey_IsSentWithTheApiKeyScheme_WithoutAPrefixOrWhitespace(string value)
    {
        var credential = StorytellerCredential.FromApiKey(value);

        var authorization = await credential.GetAuthorizationAsync(null, CancellationToken.None);

        authorization.ShouldNotBeNull();
        authorization.Scheme.ShouldBe("ApiKey");
        authorization.Parameter.ShouldBe(ApiKey);
        credential.CanRenew.ShouldBeFalse();
        credential.ClientCertificate.ShouldBeNull();
        credential.Bind(Binding).ShouldBeSameAs(credential);
        credential.ToString()!.ShouldNotContain("s3cr3t");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ApiKey ")]
    public void AnEmptyApiKey_IsRefused(string value)
    {
        Should.Throw<ArgumentException>(() => StorytellerCredential.FromApiKey(value));
    }

    [Fact]
    public async Task ACertificate_SendsNoAuthorization()
    {
        using var certificate = TestCertificates.WithPrivateKey();
        var credential = StorytellerCredential.FromCertificate(certificate);

        (await credential.GetAuthorizationAsync(null, CancellationToken.None)).ShouldBeNull();
        credential.ClientCertificate.ShouldBeSameAs(certificate);
        credential.CanRenew.ShouldBeFalse();
        credential.Bind(Binding).ShouldBeSameAs(credential);
    }

    [Fact]
    public async Task AnApiKey_CombinesWithACertificate()
    {
        using var certificate = TestCertificates.WithPrivateKey();

        var credential = StorytellerCredential.FromApiKey(ApiKey).WithClientCertificate(certificate);

        credential.ClientCertificate.ShouldBeSameAs(certificate);
        (await credential.GetAuthorizationAsync(null, CancellationToken.None))!.Parameter.ShouldBe(ApiKey);
        credential.ToString()!.ShouldNotContain("s3cr3t");
    }

    [Fact]
    public void OnlyAnApiKey_CombinesWithACertificate()
    {
        using var certificate = TestCertificates.WithPrivateKey();
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData> { ["/run/token"] = new("token") });

        Should.Throw<InvalidOperationException>(() => StorytellerCredential.FromTokenFile("/run/token", fileSystem).WithClientCertificate(certificate));
        Should.Throw<InvalidOperationException>(() => StorytellerCredential.FromCertificate(certificate).WithClientCertificate(certificate));
    }

    [Fact]
    public void ACertificateWithoutAPrivateKey_IsRefused()
    {
        using var certificate = TestCertificates.WithoutPrivateKey();

        Should.Throw<ArgumentException>(() => StorytellerCredential.FromCertificate(certificate));
        Should.Throw<ArgumentException>(() => StorytellerCredential.FromApiKey(ApiKey).WithClientCertificate(certificate));
    }

    [Fact]
    public async Task ATokenFile_IsReadAgainForEveryRequest()
    {
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData> { ["/run/token"] = new(" first-token\n") });
        var credential = StorytellerCredential.FromTokenFile("/run/token", fileSystem);

        var first = await credential.GetAuthorizationAsync(null, CancellationToken.None);
        fileSystem.File.WriteAllText("/run/token", "Bearer second-token");
        var second = await credential.GetAuthorizationAsync(null, CancellationToken.None);

        first.ShouldBe(new AuthenticationHeaderValue("Bearer", "first-token"));
        second.ShouldBe(new AuthenticationHeaderValue("Bearer", "second-token"));
        credential.CanRenew.ShouldBeFalse();
        credential.Bind(Binding).ShouldBeSameAs(credential);
    }

    [Theory]
    [InlineData(null, "does not exist")]
    [InlineData("", "is empty")]
    [InlineData("Bearer", "holds no token")]
    public async Task AMissingOrEmptyTokenFile_IsAConfigurationError(string? content, string message)
    {
        var fileSystem = new MockFileSystem();

        if (content is not null)
        {
            fileSystem.AddFile("/run/token", new MockFileData(content));
        }

        var exception = await Should.ThrowAsync<StorytellerAuthenticationException>(
            async () => await StorytellerCredential.FromTokenFile("/run/token", fileSystem).GetAuthorizationAsync(null, CancellationToken.None));

        exception.Reason.ShouldBe(StorytellerAuthenticationFailureReason.Configuration);
        exception.IsTransient.ShouldBeFalse();
        exception.Message.ShouldContain("/run/token");
        exception.Message.ShouldContain(message);
    }

    [Fact]
    public void ACertificateFile_Loads()
    {
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData> { ["/run/machine.pfx"] = new(TestCertificates.Pkcs12()) });

        using var certificate = StorytellerClientCertificate.Load("/run/machine.pfx", TestCertificates.Password, fileSystem);

        certificate.HasPrivateKey.ShouldBeTrue();
    }

    [Fact]
    public void ACertificateFileWithoutAPrivateKey_IsAConfigurationError()
    {
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData> { ["/run/machine.pfx"] = new(TestCertificates.Pkcs12WithoutPrivateKey()) });

        var exception = Should.Throw<StorytellerAuthenticationException>(() => StorytellerClientCertificate.Load("/run/machine.pfx", TestCertificates.Password, fileSystem));

        exception.Reason.ShouldBe(StorytellerAuthenticationFailureReason.Configuration);
        exception.Message.ShouldContain("no private key");
    }

    [Fact]
    public void AWrongPassword_IsAConfigurationError_WithoutThePassword()
    {
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData> { ["/run/machine.pfx"] = new(TestCertificates.Pkcs12()) });

        var exception = Should.Throw<StorytellerAuthenticationException>(() => StorytellerClientCertificate.Load("/run/machine.pfx", "wrong-p4ssword", fileSystem));

        exception.Reason.ShouldBe(StorytellerAuthenticationFailureReason.Configuration);
        exception.Message.ShouldNotContain("wrong-p4ssword");
    }

    [Fact]
    public void AMissingCertificateFile_IsAConfigurationError()
    {
        var exception = Should.Throw<StorytellerAuthenticationException>(() => StorytellerClientCertificate.Load("/run/missing.pfx", null, new MockFileSystem()));

        exception.Reason.ShouldBe(StorytellerAuthenticationFailureReason.Configuration);
        exception.Message.ShouldContain("does not exist");
    }
}

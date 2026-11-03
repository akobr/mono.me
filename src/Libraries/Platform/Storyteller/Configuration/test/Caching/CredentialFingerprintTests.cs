using System.IO.Abstractions.TestingHelpers;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using _42.Platform.Storyteller.Sdk.Configuring.Caching;
using _42.Platform.Storyteller.Sdk.Credentials;

namespace _42.Platform.Storyteller.Sdk.Configuring.UnitTests.Caching;

public class CredentialFingerprintTests
{
    // 2s.{base64url(organization:project:machine)}.{secret}
    private const string MachineOneKey = "2s.YWNtZTpiaWxsaW5nOm1hY2hpbmUtMQ.k3y-s3cret";
    private const string MachineOneRotatedKey = "2s.YWNtZTpiaWxsaW5nOm1hY2hpbmUtMQ.r0tated-s3cret";
    private const string MachineTwoKey = "2s.YWNtZTpiaWxsaW5nOm1hY2hpbmUtMg.k3y-s3cret";
    private const string FederatedMachineId = "0b9f6a52-5d1c-4c47-9d0c-0f1e2d3c4b5a";

    [Fact]
    public async Task NoCredential_IsAnonymous()
    {
        (await Compute(null)).ShouldBe(CredentialFingerprint.Anonymous);
    }

    [Fact]
    public async Task ApiKeys_AreSeparatedByTheirMachine_NotByTheirSecret()
    {
        var one = await Compute(StorytellerCredential.FromApiKey(MachineOneKey));

        one.ShouldStartWith("ApiKeyCredential:");
        one.ShouldBe(await Compute(StorytellerCredential.FromApiKey(MachineOneRotatedKey)));
        one.ShouldNotBe(await Compute(StorytellerCredential.FromApiKey(MachineTwoKey)));
        one.ShouldNotContain("s3cret");
    }

    [Fact]
    public async Task UnstructuredApiKeys_AreSeparatedToo()
    {
        (await Compute(StorytellerCredential.FromApiKey("legacy-one")))
            .ShouldNotBe(await Compute(StorytellerCredential.FromApiKey("legacy-two")));
    }

    [Fact]
    public async Task Certificates_AreSeparated()
    {
        using var first = CreateCertificate();
        using var second = CreateCertificate();

        var withFirst = await Compute(StorytellerCredential.FromCertificate(first));

        withFirst.ShouldStartWith("CertificateCredential:");
        withFirst.ShouldNotBe(await Compute(StorytellerCredential.FromCertificate(second)));
        withFirst.ShouldNotContain(first.Thumbprint, Case.Insensitive);

        var keyWithFirst = await Compute(StorytellerCredential.FromApiKey(MachineOneKey).WithClientCertificate(first));
        keyWithFirst.ShouldNotBe(await Compute(StorytellerCredential.FromApiKey(MachineOneKey)));
        keyWithFirst.ShouldNotBe(await Compute(StorytellerCredential.FromApiKey(MachineOneKey).WithClientCertificate(second)));
    }

    [Fact]
    public async Task ClientCredentials_AreSeparatedByTheirClientId_WithoutAskingForAToken()
    {
        var connected = false;

        StorytellerCredential Create(string clientId, string secret) => StorytellerCredential.FromClientCredentials(new ClientCredentialsOptions
        {
            TokenUrl = new Uri("https://idp.example.com/oauth2/token"),
            ClientId = clientId,
            ClientSecret = secret,
            ConfigureHandler = handler => handler.ConnectCallback = (_, _) =>
            {
                connected = true;
                return ValueTask.FromException<Stream>(new IOException("Offline."));
            },
        });

        var first = await Compute(Create("client-1", "secret-1"));

        first.ShouldBe(await Compute(Create("client-1", "secret-2")));
        first.ShouldNotBe(await Compute(Create("client-2", "secret-1")));
        connected.ShouldBeFalse();
    }

    [Fact]
    public async Task AKindWithoutItsFile_StillHasAFingerprint()
    {
        var fileSystem = new MockFileSystem();

        var fingerprint = await Compute(StorytellerCredential.FromTokenFile("/run/missing-token", fileSystem));

        fingerprint.ShouldStartWith("TokenFileCredential:");
    }

    [Fact]
    public async Task ACredentialOfTheApplication_IsFingerprintedByItsType_AndNotByAToken()
    {
        var credential = TestCredential.Bearer("a");

        (await Compute(credential)).ShouldStartWith("TestCredential:");
        (await Compute(TestCredential.Bearer("b"))).ShouldBe(await Compute(credential));
        (await Compute(TestCredential.Failing(StorytellerAuthenticationFailureReason.Unavailable))).ShouldBe(await Compute(credential));
    }

    [Fact]
    public async Task ApiKeysOfTheApplication_AreSeparatedByTheirMachine_WhenTheyAreAtHand()
    {
        var one = await Compute(TestCredential.ApiKey(MachineOneKey));

        one.ShouldStartWith("TestCredential:");
        one.ShouldBe(await Compute(TestCredential.ApiKey(MachineOneRotatedKey)));
        one.ShouldNotBe(await Compute(TestCredential.ApiKey(MachineTwoKey)));
        one.ShouldNotContain("s3cret");
    }

    [Fact]
    public async Task AnApiKeyTheApplicationMustFetch_IsNotWaitedFor_SoTheFingerprintDoesNotDependOnTheNetwork()
    {
        var fetching = new FetchingCredential();

        var computing = CredentialFingerprint.ComputeAsync(fetching, CancellationToken.None);

        computing.IsCompleted.ShouldBeTrue();
        var online = await computing;
        fetching.Fetch.SetResult(new AuthenticationHeaderValue("ApiKey", MachineOneKey));

        // The same principal while its vault is unreachable: the entry written online is found offline.
        var offline = new FetchingCredential();
        offline.Fetch.SetException(new HttpRequestException("Offline."));

        (await Compute(offline)).ShouldBe(online);

        // Without a principal in ToString(), principals of such a kind share it.
        var other = new FetchingCredential();
        (await Compute(other)).ShouldBe(online);
        other.Fetch.SetResult(new AuthenticationHeaderValue("ApiKey", MachineTwoKey));
    }

    [Fact]
    public async Task ACredentialOfTheApplication_IsSeparatedByItsToString()
    {
        (await Compute(new NamedCredential("machine-1")))
            .ShouldNotBe(await Compute(new NamedCredential("machine-2")));
    }

    [Fact]
    public async Task Kinds_AreSeparated()
    {
        var fileSystem = new MockFileSystem();

        (await Compute(StorytellerCredential.FromTokenFile("/run/token", fileSystem)))
            .ShouldNotBe(await Compute(StorytellerCredential.FromApiKey(MachineOneKey)));
    }

    // Pinned: the entries a federated machine wrote must still match after an upgrade of the SDK.
    [Fact]
    public async Task AFederatedCredential_FromATokenFile_KeepsItsFingerprint()
    {
        var credential = StorytellerCredential.FromEnvironment(Variables(new Dictionary<string, string?>
        {
            ["STORYTELLER_TOKEN_FILE"] = "/run/platform-token",
        }));

        credential.ShouldNotBeNull().ToString().ShouldBe($"StorytellerCredential {{ Kind = Federated, ClientId = {FederatedMachineId}, SubjectTokenPath = /run/platform-token }}");
        (await Compute(credential)).ShouldBe("FederatedTokenCredential:f90d14ab91f0f9bf9a1d25655aa18750");
    }

    // The client id keeps two machines of one job apart; the runner's URL and request token are not part of it.
    [Fact]
    public async Task AFederatedCredential_OfGitHubActions_HasAFingerprintOfItsOwn()
    {
        var credential = StorytellerCredential.FromEnvironment(Variables(new Dictionary<string, string?>
        {
            ["ACTIONS_ID_TOKEN_REQUEST_URL"] = "https://pipelines.actions.githubusercontent.com/abc/idtoken?api-version=2.0",
            ["ACTIONS_ID_TOKEN_REQUEST_TOKEN"] = "github-request-t0ken",
        }));

        credential.ShouldNotBeNull().ToString().ShouldBe($"StorytellerCredential {{ Kind = Federated, ClientId = {FederatedMachineId}, SubjectTokenSource = GitHubActions }}");
        var fingerprint = await Compute(credential);

        fingerprint.ShouldBe("FederatedTokenCredential:72ba6a4a99252cbd65c5e262d5a090f4");
        fingerprint.ShouldNotBe(await Compute(StorytellerCredential.FromEnvironment(Variables(new Dictionary<string, string?>
        {
            ["ACTIONS_ID_TOKEN_REQUEST_URL"] = "https://pipelines.actions.githubusercontent.com/other/idtoken?api-version=2.0",
            ["ACTIONS_ID_TOKEN_REQUEST_TOKEN"] = "github-request-t0ken",
            ["STORYTELLER_CLIENT_ID"] = "7c4d2b1a-0e9f-4a3b-8c7d-6e5f4a3b2c1d",
        }))));
    }

    private static Func<string, string?> Variables(Dictionary<string, string?> variables)
    {
        variables.TryAdd("STORYTELLER_CLIENT_ID", FederatedMachineId);
        return name => variables.GetValueOrDefault(name);
    }

    private static Task<string> Compute(StorytellerCredential? credential)
    {
        return CredentialFingerprint.ComputeAsync(credential, CancellationToken.None).AsTask();
    }

    private static X509Certificate2 CreateCertificate()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=storyteller-fingerprint-test", key, HashAlgorithmName.SHA256);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
    }

    // Fetches its API key, from a vault say, once the test completes Fetch.
    private sealed class FetchingCredential : StorytellerCredential
    {
        public TaskCompletionSource<AuthenticationHeaderValue?> Fetch { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ValueTask<AuthenticationHeaderValue?> GetAuthorizationAsync(AuthenticationHeaderValue? rejected, CancellationToken cancellationToken)
        {
            return new ValueTask<AuthenticationHeaderValue?>(Fetch.Task);
        }
    }

    // Names its principal, without secrets, as an application's credential should.
    private sealed class NamedCredential(string machine) : StorytellerCredential
    {
        public override ValueTask<AuthenticationHeaderValue?> GetAuthorizationAsync(AuthenticationHeaderValue? rejected, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult<AuthenticationHeaderValue?>(new AuthenticationHeaderValue("Bearer", "token"));
        }

        public override string ToString()
        {
            return $"{nameof(NamedCredential)} for {machine}";
        }
    }
}

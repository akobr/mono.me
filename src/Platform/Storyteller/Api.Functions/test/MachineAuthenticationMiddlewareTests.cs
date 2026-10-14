using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using _42.Platform.Storyteller.Api;
using _42.Platform.Storyteller.Api.Security;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// A machine presents what its own kind requires, and its project has to enable that kind.
public class MachineAuthenticationMiddlewareTests
{
    private const string CertificateHeader = "X-ARR-ClientCert";
    private const string MachineId = "3f2c6a8e-6c1d-4a59-9a47-0a2f4f4c1b2d";

    private static readonly string CertificateValue = CreateCertificateValue();

    private readonly RecordingMiddleware _middleware = new();
    private readonly FakePolicyStore _policyStore = new();
    private readonly FakeApiKeyValidator _apiKeyValidator = new();
    private readonly FakeCertificateValidator _certificateValidator = new();

    [Theory]
    [InlineData(MachineCredentialKind.ApiKey, true, false)]
    [InlineData(MachineCredentialKind.Certificate, false, true)]
    [InlineData(MachineCredentialKind.CertificateAndApiKey, true, true)]
    public async Task MachineOfAnEnabledKind_WithItsCredentials_IsAuthenticated(MachineCredentialKind kind, bool sendApiKey, bool sendCertificate)
    {
        _policyStore.Policy = Enable(MachineCredentialKind.ApiKey, MachineCredentialKind.Certificate, MachineCredentialKind.CertificateAndApiKey);
        GiveMachine(kind);

        var (called, context) = await InvokeAsync(sendApiKey, sendCertificate);

        called.ShouldBeTrue();
        _middleware.Response.ShouldBeNull();
        context.Items[FunctionContextItemKeys.MachineIdentity].ShouldBe(MachineId);
    }

    [Fact]
    public async Task MachinesOfDifferentKinds_WorkSideBySideInOneProject()
    {
        _policyStore.Policy = Enable(MachineCredentialKind.ApiKey, MachineCredentialKind.Certificate);

        GiveMachine(MachineCredentialKind.ApiKey);
        (await InvokeAsync(sendApiKey: true, sendCertificate: false)).Called.ShouldBeTrue();

        GiveMachine(MachineCredentialKind.Certificate);
        (await InvokeAsync(sendApiKey: false, sendCertificate: true)).Called.ShouldBeTrue();
    }

    [Fact]
    public async Task MachineOfAKindTheProjectNoLongerEnables_IsUnauthorized()
    {
        _policyStore.Policy = Enable(MachineCredentialKind.Certificate);
        GiveMachine(MachineCredentialKind.ApiKey);

        var (called, _) = await InvokeAsync(sendApiKey: true, sendCertificate: false);

        called.ShouldBeFalse();
        _middleware.Response!.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CertificateAndApiKeyMachine_WithOnlyItsApiKey_IsUnauthorized()
    {
        // ApiKey is enabled too, but this machine was created to send both.
        _policyStore.Policy = Enable(MachineCredentialKind.ApiKey, MachineCredentialKind.CertificateAndApiKey);
        GiveMachine(MachineCredentialKind.CertificateAndApiKey);

        var (called, _) = await InvokeAsync(sendApiKey: true, sendCertificate: false);

        called.ShouldBeFalse();
        _middleware.Response!.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ApiKeyOfAClientCredentialsProject_IsUnauthorized()
    {
        _policyStore.Policy = Enable(MachineCredentialKind.ClientCredentials);
        GiveMachine(MachineCredentialKind.ApiKey);

        (await InvokeAsync(sendApiKey: true, sendCertificate: false)).Called.ShouldBeFalse();
    }

    [Theory]
    [InlineData(MachineCredentialKind.ApiKey, true)]
    [InlineData(MachineCredentialKind.CertificateAndApiKey, false)]
    public async Task UnknownMachineKind_UsesTheKindOfThePresentedCredentials(MachineCredentialKind enabled, bool accepted)
    {
        _policyStore.Policy = Enable(enabled);
        _apiKeyValidator.Result = new ApiKeyValidationResult("org1", "proj1", MachineId, MachineAccessScope.DefaultRead);

        var (called, _) = await InvokeAsync(sendApiKey: true, sendCertificate: false);

        called.ShouldBe(accepted);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MachineCertificateWithoutAKind_IsUnauthorized(bool sendApiKey)
    {
        // Certificate is enabled, but the presented credentials don't tell the machine's own kind.
        _policyStore.Policy = Enable(MachineCredentialKind.Certificate, MachineCredentialKind.CertificateAndApiKey);
        GiveMachine(MachineCredentialKind.CertificateAndApiKey);
        _certificateValidator.Result = _certificateValidator.Result! with { CredentialKind = null };

        var (called, _) = await InvokeAsync(sendApiKey, sendCertificate: true);

        called.ShouldBeFalse();
        _middleware.Response!.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WithoutAProjectPolicy_TheDeploymentDefaultKindIsEnabled()
    {
        GiveMachine(MachineCredentialKind.Certificate);

        (await InvokeAsync(sendApiKey: false, sendCertificate: true)).Called.ShouldBeFalse();

        GiveMachine(MachineCredentialKind.ApiKey);

        (await InvokeAsync(sendApiKey: true, sendCertificate: false)).Called.ShouldBeTrue();
    }

    [Fact]
    public async Task SharedCertificate_WithTheKeyOfACertificateAndApiKeyMachine_IsAuthenticated()
    {
        _policyStore.Policy = Enable(MachineCredentialKind.CertificateAndApiKey);
        GiveMachineWithSharedCertificate(MachineCredentialKind.CertificateAndApiKey);

        var (called, context) = await InvokeAsync(sendApiKey: true, sendCertificate: true);

        called.ShouldBeTrue();
        context.Items[FunctionContextItemKeys.MachineIdentity].ShouldBe(MachineId);
        context.Items[FunctionContextItemKeys.PresentingCertificateThumbprint].ShouldBe("SHARED");
    }

    [Fact]
    public async Task SharedCertificate_WithTheKeyOfAnApiKeyMachine_IsUnauthorized()
    {
        // ApiKey is enabled, but a shared certificate belongs to CertificateAndApiKey: it must not ride along.
        _policyStore.Policy = Enable(MachineCredentialKind.ApiKey, MachineCredentialKind.CertificateAndApiKey);
        GiveMachineWithSharedCertificate(MachineCredentialKind.ApiKey);

        var (called, context) = await InvokeAsync(sendApiKey: true, sendCertificate: true);

        called.ShouldBeFalse();
        _middleware.Response!.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        context.Items.ContainsKey(FunctionContextItemKeys.PresentingCertificateThumbprint).ShouldBeFalse();
    }

    [Fact]
    public async Task SharedCertificate_OfAProjectThatNoLongerEnablesCertificateAndApiKey_IsUnauthorized()
    {
        _policyStore.Policy = Enable(MachineCredentialKind.ApiKey, MachineCredentialKind.Certificate);
        GiveMachineWithSharedCertificate(MachineCredentialKind.CertificateAndApiKey);

        (await InvokeAsync(sendApiKey: true, sendCertificate: true)).Called.ShouldBeFalse();
    }

    [Fact]
    public async Task SharedCertificate_WithoutAnApiKey_IsUnauthorized()
    {
        _policyStore.Policy = Enable(MachineCredentialKind.CertificateAndApiKey);
        GiveMachineWithSharedCertificate(MachineCredentialKind.CertificateAndApiKey);

        (await InvokeAsync(sendApiKey: false, sendCertificate: true)).Called.ShouldBeFalse();
    }

    [Fact]
    public async Task SharedCertificate_WithTheKeyOfAMachineOfUnknownKind_IsUnauthorized()
    {
        // The machine may be an ApiKey one: the shared certificate must not make it pass as CertificateAndApiKey.
        _apiKeyValidator.Result = new ApiKeyValidationResult("org1", "proj1", MachineId, MachineAccessScope.DefaultRead);
        _certificateValidator.Result = SharedCertificateResult();
        _policyStore.Policy = Enable(MachineCredentialKind.ApiKey, MachineCredentialKind.CertificateAndApiKey);

        var (called, context) = await InvokeAsync(sendApiKey: true, sendCertificate: true);

        called.ShouldBeFalse();
        _middleware.Response!.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        context.Items.ContainsKey(FunctionContextItemKeys.PresentingCertificateThumbprint).ShouldBeFalse();
    }

    private static ClientCertificateValidationResult SharedCertificateResult()
    {
        return new ClientCertificateValidationResult(
            "org1",
            "proj1",
            null,
            default,
            "SHARED",
            ClientCertificateKind.Shared,
            null);
    }

    private static MachineAuthenticationPolicy Enable(params MachineCredentialKind[] kinds)
    {
        return new MachineAuthenticationPolicy { EnabledCredentialKinds = kinds };
    }

    private static string CreateCertificateValue()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=machine", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        return Convert.ToBase64String(certificate.Export(X509ContentType.Cert));
    }

    private void GiveMachine(MachineCredentialKind kind)
    {
        _apiKeyValidator.Result = new ApiKeyValidationResult("org1", "proj1", MachineId, MachineAccessScope.DefaultRead, kind);
        _certificateValidator.Result = new ClientCertificateValidationResult(
            "org1",
            "proj1",
            MachineId,
            MachineAccessScope.DefaultRead,
            "THUMBPRINT",
            ClientCertificateKind.Machine,
            null,
            kind);
    }

    private void GiveMachineWithSharedCertificate(MachineCredentialKind kind)
    {
        _apiKeyValidator.Result = new ApiKeyValidationResult("org1", "proj1", MachineId, MachineAccessScope.DefaultRead, kind);
        _certificateValidator.Result = SharedCertificateResult();
    }

    private async Task<(bool Called, TestFunctionContext Context)> InvokeAsync(bool sendApiKey, bool sendCertificate)
    {
        var headers = new Dictionary<string, string>();

        if (sendApiKey)
        {
            headers["Authorization"] = $"ApiKey 2s.org1.proj1.{MachineId}.secret";
        }

        if (sendCertificate)
        {
            headers[CertificateHeader] = CertificateValue;
        }

        var services = new ServiceCollection()
            .AddSingleton(Options.Create(new MachineAuthenticationOptions
            {
                CertificateHeaderName = CertificateHeader,
                IsCertificateHeaderFromClientAllowed = true,
            }))
            .AddSingleton<IMachineAuthenticationPolicyStore>(_policyStore)
            .AddSingleton<IApiKeyValidator>(_apiKeyValidator)
            .AddSingleton<IClientCertificateValidator>(_certificateValidator)
            .BuildServiceProvider();
        var (context, _) = FunctionTestDoubles.CreateRequest(services, headers);
        var called = false;

        await _middleware.Invoke(context, _ =>
        {
            called = true;
            return Task.CompletedTask;
        });

        return (called, context);
    }

    private sealed class RecordingMiddleware : MachineAuthenticationMiddleware
    {
        public RecordingMiddleware()
            : base(NullLogger<MachineAuthenticationMiddleware>.Instance)
        {
        }

        public HttpResponseData? Response { get; private set; }

        protected override void AssignResponse(FunctionContext context, HttpResponseData response)
        {
            Response = response;
        }
    }

    private sealed class FakePolicyStore : IMachineAuthenticationPolicyStore
    {
        public MachineAuthenticationPolicy? Policy { get; set; }

        public Task<MachineAuthenticationPolicy?> GetAsync(string organization, string project)
        {
            return Task.FromResult(Policy);
        }

        public Task SetAsync(string organization, string project, MachineAuthenticationPolicy policy)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeApiKeyValidator : IApiKeyValidator
    {
        public ApiKeyValidationResult? Result { get; set; }

        public Task<ApiKeyValidationResult?> ValidateAsync(string rawApiKey)
        {
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeCertificateValidator : IClientCertificateValidator
    {
        public ClientCertificateValidationResult? Result { get; set; }

        public Task<ClientCertificateValidationResult?> ValidateAsync(X509Certificate2 clientCertificate)
        {
            return Task.FromResult(Result);
        }
    }
}

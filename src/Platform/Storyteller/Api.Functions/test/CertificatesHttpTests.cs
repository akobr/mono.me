using System.Net;
using System.Security.Claims;

using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using _42.Platform.Storyteller.Api.Models;
using _42.Platform.Storyteller.Api.Security;
using _42.Platform.Storyteller.Api.V1;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

public class CertificatesHttpTests
{
    // Without the shared certificate service, the body is never read.
    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("""{ "Label": "build-agent" }""")]
    public async Task PostSharedCertificate_WithoutTheService_IsNotImplementedWhateverTheBody(string body)
    {
        var http = CreateHttp(sharedCertService: null);

        var result = await http.PostSharedCertificate(UserRequest(body), "acme", "billing");

        result.ShouldBeOfType<StatusCodeResult>().StatusCode.ShouldBe((int)HttpStatusCode.NotImplemented);
    }

    // The worker's [FromBody] binding failed these bodies with a 500 before the handler ran. The label is required,
    // so {} is not a shared certificate either.
    [Theory]
    [InlineData("", "The shared certificate is missing.")]
    [InlineData("null", "The shared certificate is missing.")]
    [InlineData("[1, 2]", "The request body is not a shared certificate (at $).")]
    [InlineData("{}", "The request body is not a shared certificate (at $).")]
    [InlineData("""{ "label": "bad label!" }""", "Invalid certificate label. Only letters, digits, hyphens, underscores, and dots are allowed.")]
    public async Task PostSharedCertificate_MissingOrInvalidBody_ReturnsBadRequestWithoutIssuing(string body, string message)
    {
        var backend = new SharedCertificateBackend();
        var http = CreateHttp(backend.CreateService());

        var result = await http.PostSharedCertificate(UserRequest(body), "acme", "billing");

        var error = result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>();
        error.Message.ShouldBe(message);
        error.ErrorCode.ShouldBeNull();
        backend.Calls.ShouldBeEmpty();
    }

    // The worker's [FromBody] binding refused a body without a content type; the handler reads it.
    [Fact]
    public async Task PostSharedCertificate_BodyWithoutAContentType_IssuesTheCertificate()
    {
        var backend = new SharedCertificateBackend();
        var http = CreateHttp(backend.CreateService());
        var body = FunctionTestDoubles.ToJsonBody(new SharedCertificateCreate { Label = "build-agent", LifetimeDays = 30 });

        var result = await http.PostSharedCertificate(UserRequest(body, contentType: false), "acme", "billing");

        var certificate = result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<SharedCertificate>();
        certificate.Label.ShouldBe("build-agent");
        certificate.Thumbprint.ShouldBe("THUMBPRINT");
        backend.Calls.ShouldBe(["policy:acme.billing", "label:acme.billing:build-agent", "issue:acme.billing:build-agent:30", "store:acme.billing:THUMBPRINT"]);
    }

    private static CertificatesHttp CreateHttp(SharedCertificateService? sharedCertService)
    {
        return new CertificatesHttp(
            new RoleAccess(AccountRole.Administrator),
            FunctionTestDoubles.ApiJsonOptions,
            NullLogger<CertificatesHttp>.Instance,
            sharedCertService: sharedCertService);
    }

    // An administrator's request; the body goes as JSON, with its content type unless contentType is false.
    private static TestHttpRequestData UserRequest(string body, bool contentType = true)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var headers = contentType ? FunctionTestDoubles.JsonContentType() : null;
        var (context, request) = FunctionTestDoubles.CreateRequest(services, headers, "POST", body);
        context.Items[FunctionContextItemKeys.CachedClaims] = new List<Claim>
        {
            new("sub", "user-1"),
            new("scp", Scopes.User.Impersonation),
        };
        return request;
    }

    // The policy store, the certificate store and the authority of SharedCertificateService in one: a project that
    // enables CertificateAndApiKey, no label taken yet, and a certificate for every request.
    private sealed class SharedCertificateBackend : IMachineAuthenticationPolicyStore, ISharedCertificateStore, IClientCertificateAuthority
    {
        public List<string> Calls { get; } = [];

        public SharedCertificateService CreateService()
        {
            return new SharedCertificateService(
                this,
                this,
                this,
                Options.Create(new MachineAuthenticationOptions()),
                NullLogger<SharedCertificateService>.Instance);
        }

        public Task<MachineAuthenticationPolicy?> GetAsync(string organization, string project)
        {
            Calls.Add($"policy:{organization}.{project}");
            return Task.FromResult<MachineAuthenticationPolicy?>(new MachineAuthenticationPolicy
            {
                EnabledCredentialKinds = [MachineCredentialKind.CertificateAndApiKey],
            });
        }

        public Task<bool> LabelExistsAsync(string organization, string project, string label)
        {
            Calls.Add($"label:{organization}.{project}:{label}");
            return Task.FromResult(false);
        }

        public Task<IssuedCertificate> IssueSharedCertificateAsync(string organization, string project, string label, int lifetimeDays)
        {
            Calls.Add($"issue:{organization}.{project}:{label}:{lifetimeDays}");
            var notBefore = DateTimeOffset.UnixEpoch;
            return Task.FromResult(new IssuedCertificate([1, 2, 3], "secret", "THUMBPRINT", "01", notBefore, notBefore.AddDays(lifetimeDays)));
        }

        public Task StoreAsync(string organization, string project, SharedCertificate certificate)
        {
            Calls.Add($"store:{organization}.{project}:{certificate.Thumbprint}");
            return Task.CompletedTask;
        }

        public Task SetAsync(string organization, string project, MachineAuthenticationPolicy policy) => throw new NotSupportedException();

        public Task<IReadOnlyList<SharedCertificate>> ListAsync(string organization, string project) => throw new NotSupportedException();

        public Task<SharedCertificate?> GetByThumbprintAsync(string organization, string project, string thumbprint) => throw new NotSupportedException();

        public Task<bool> RevokeAsync(string organization, string project, string thumbprint) => throw new NotSupportedException();

        public Task<IssuedCertificate> IssueCertificateAsync(string organization, string project, string machineAccessId, MachineAccessScope scope, int lifetimeDays)
            => throw new NotSupportedException();
    }
}

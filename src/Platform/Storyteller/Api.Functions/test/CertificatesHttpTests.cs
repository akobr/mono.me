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
    private static bool DevAuth => HttpRequestDataExtensions.IsDevelopmentAuthentication;

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

    // A shared certificate belongs to CertificateAndApiKey: a project without that kind gets a coded 400 and no
    // certificate.
    [Fact]
    public async Task PostSharedCertificate_ProjectWithoutCertificateAndApiKey_IsBadRequestWithMachineAccessNotSupported()
    {
        var backend = new SharedCertificateBackend { EnabledCredentialKinds = [MachineCredentialKind.ApiKey] };
        var http = CreateHttp(backend.CreateService());
        var body = FunctionTestDoubles.ToJsonBody(new SharedCertificateCreate { Label = "build-agent" });

        var result = await http.PostSharedCertificate(UserRequest(body), "acme", "billing");

        var badRequest = result.ShouldBeOfType<ObjectResult>();
        badRequest.StatusCode.ShouldBe((int)HttpStatusCode.BadRequest);
        var error = badRequest.Value.ShouldBeOfType<ErrorResponse>();
        error.Message.ShouldBe("The project acme.billing does not enable CertificateAndApiKey, which shared certificates belong to.");
        error.ErrorCode.ShouldBe(ErrorCodes.MachineAccessNotSupported);
        error.Error.ShouldBeNull();
        backend.Calls.ShouldBe(["policy:acme.billing"]);
    }

    // Shared certificates are managed by users (spec 7.4): a machine is refused in every build, also one whose
    // credentials carry every scope and User.Impersonation, before the body is read or a certificate is listed, issued or
    // revoked.
    [Fact]
    public async Task SharedCertificateRoutes_Machine_IsAccessDeniedInEveryBuild()
    {
        var backend = new SharedCertificateBackend();
        var http = CreateHttp(backend.CreateService());
        var body = FunctionTestDoubles.ToJsonBody(new SharedCertificateCreate { Label = "build-agent", LifetimeDays = 30 });

        await UserOnlyRoutes.ShouldRefuseMachineAsync(() => http.GetSharedCertificates(UserOnlyRoutes.Machine(), "acme", "billing"));
        await UserOnlyRoutes.ShouldRefuseMachineAsync(() => http.PostSharedCertificate(UserOnlyRoutes.Machine("POST", body), "acme", "billing"));
        await UserOnlyRoutes.ShouldRefuseMachineAsync(() => http.DeleteSharedCertificate(UserOnlyRoutes.Machine("DELETE"), "acme", "billing", "THUMBPRINT"));

        backend.Calls.ShouldBeEmpty();
    }

    // They need an Administrator; a user role check, so Debug builds (DEV_AUTH) skip it.
    [Fact]
    public async Task SharedCertificateRoutes_ByAContributor_AreAccessDenied()
    {
        if (DevAuth)
        {
            return;
        }

        var backend = new SharedCertificateBackend();
        var http = CreateHttp(backend.CreateService(), role: AccountRole.ContributorWithSecrets);
        var body = FunctionTestDoubles.ToJsonBody(new SharedCertificateCreate { Label = "build-agent", LifetimeDays = 30 });

        foreach (var call in new Func<Task>[]
                 {
                     () => http.GetSharedCertificates(TestRequests.User(), "acme", "billing"),
                     () => http.PostSharedCertificate(UserRequest(body), "acme", "billing"),
                     () => http.DeleteSharedCertificate(TestRequests.User("DELETE"), "acme", "billing", "THUMBPRINT"),
                 })
        {
            (await Should.ThrowAsync<AccessDeniedException>(call)).Message.ShouldBe("No Administrator access to 'acme.billing'.");
        }

        backend.Calls.ShouldBeEmpty();
    }

    // A machine renews its own certificate: the presented certificate identifies it, and it needs no scope.
    [Fact]
    public async Task PostRenewCertificate_TheMachineOfTheRoute_RenewsWithoutAScope()
    {
        var renewal = new RecordingRenewal();
        var http = CreateHttp(sharedCertService: null, renewal: renewal);

        var result = await http.PostRenewCertificate(TestRequests.Machine([], "POST", mtls: true), "acme", "billing", TestRequests.MachineId);

        result.ShouldBeOfType<OkObjectResult>();
        renewal.Calls.ShouldBe([$"acme.billing:{TestRequests.MachineId}:ABCD"]);
    }

    // The renewal stays machine-only by identity (spec 7.4): a user, another machine and a machine without its
    // certificate are 401, in every build.
    [Fact]
    public async Task PostRenewCertificate_ByAUserAnotherMachineOrWithoutTheCertificate_IsUnauthorized()
    {
        var renewal = new RecordingRenewal();
        var http = CreateHttp(sharedCertService: null, renewal: renewal);

        (await http.PostRenewCertificate(TestRequests.User("POST"), "acme", "billing", TestRequests.MachineId)).ShouldBeOfType<UnauthorizedResult>();
        (await http.PostRenewCertificate(TestRequests.Machine(method: "POST", machineId: "machine-2", mtls: true), "acme", "billing", TestRequests.MachineId)).ShouldBeOfType<UnauthorizedResult>();
        (await http.PostRenewCertificate(TestRequests.Machine(method: "POST"), "acme", "billing", TestRequests.MachineId)).ShouldBeOfType<UnauthorizedResult>();

        renewal.Calls.ShouldBeEmpty();
    }

    private static CertificatesHttp CreateHttp(
        SharedCertificateService? sharedCertService,
        AccountRole role = AccountRole.Administrator,
        ICertificateRenewalService? renewal = null)
    {
        return new CertificatesHttp(
            new RoleAccess(role),
            FunctionTestDoubles.ApiJsonOptions,
            NullLogger<CertificatesHttp>.Instance,
            renewalService: renewal,
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
    // enables CertificateAndApiKey (unless the test enables other kinds), no label taken yet, and a certificate for
    // every request.
    private sealed class SharedCertificateBackend : IMachineAuthenticationPolicyStore, ISharedCertificateStore, IClientCertificateAuthority
    {
        public List<string> Calls { get; } = [];

        public IReadOnlyList<MachineCredentialKind> EnabledCredentialKinds { get; init; } = [MachineCredentialKind.CertificateAndApiKey];

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
                EnabledCredentialKinds = EnabledCredentialKinds,
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

        public Task<IssuedCertificate> IssueCertificateAsync(string organization, string project, string machineAccessId, int lifetimeDays)
            => throw new NotSupportedException();
    }

    private sealed class RecordingRenewal : ICertificateRenewalService
    {
        public List<string> Calls { get; } = [];

        public Task<CertificateRenewalResult> RenewAsync(string organization, string project, string machineAccessId, string presentingThumbprint)
        {
            Calls.Add($"{organization}.{project}:{machineAccessId}:{presentingThumbprint}");
            return Task.FromResult(new CertificateRenewalResult { Outcome = CertificateRenewalOutcome.Success, Thumbprint = "RENEWED" });
        }
    }
}

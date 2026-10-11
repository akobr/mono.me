using System.Security.Claims;

using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Api.Models;
using _42.Platform.Storyteller.Api.Security;
using _42.Platform.Storyteller.Api.V1;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

public class AnnotationsHttpTests
{
    private const string UsageBody = """
        {"AnnotationKey":"usg.northwind.invoicing","Name":"invoicing","AnnotationType":"Usage","ProjectName":"x","ViewName":"y",
         "SubjectKey":"sbt.northwind","ResponsibilityKey":"rst.invoicing","SubjectName":"northwind","ResponsibilityName":"invoicing",
         "Title":"Invoices of Northwind"}
        """;

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    public async Task Set_NewKey_CreatesTheTypedAnnotationUnderTheRoute(string method)
    {
        var annotations = new RecordingAnnotations();
        var http = new AnnotationsHttp(annotations, new ContributorAccess(), NullLogger<AnnotationsHttp>.Instance);

        var result = await http.SetAnnotation(Request(method, UsageBody), "acme", "billing", "default", "usg.northwind.invoicing");

        var stored = result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<Usage>();
        stored.ProjectName.ShouldBe("billing");
        stored.ViewName.ShouldBe("default");
        stored.Title.ShouldBe("Invoices of Northwind");
        stored.SubjectKey.ShouldBe("sbt.northwind");
        annotations.Calls.ShouldBe(["exists:acme:usg.northwind.invoicing", "create:acme:usg.northwind.invoicing"]);
    }

    [Fact]
    public async Task Set_ExistingKey_UpdatesWithPost()
    {
        var annotations = new RecordingAnnotations { Existing = ["usg.northwind.invoicing"] };
        var http = new AnnotationsHttp(annotations, new ContributorAccess(), NullLogger<AnnotationsHttp>.Instance);

        await http.SetAnnotation(Request("POST", UsageBody), "acme", "billing", "default", "usg.northwind.invoicing");

        annotations.Calls.ShouldBe(["exists:acme:usg.northwind.invoicing", "update:acme:usg.northwind.invoicing"]);
    }

    [Fact]
    public async Task Set_BodyWithoutTheTypeSpecificMembers_IsRejected()
    {
        var annotations = new RecordingAnnotations();
        var http = new AnnotationsHttp(annotations, new ContributorAccess(), NullLogger<AnnotationsHttp>.Instance);
        const string baseOnly = """{"AnnotationKey":"usg.northwind.invoicing","Name":"invoicing","AnnotationType":"Usage","ProjectName":"x","ViewName":"y"}""";

        var result = await http.SetAnnotation(Request("POST", baseOnly), "acme", "billing", "default", "usg.northwind.invoicing");

        result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>().Message.ShouldContain("SubjectKey");
        annotations.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Set_UnknownType_IsRejected()
    {
        var http = new AnnotationsHttp(new RecordingAnnotations(), new ContributorAccess(), NullLogger<AnnotationsHttp>.Instance);

        var result = await http.SetAnnotation(Request("POST", """{"AnnotationKey":"x.y","AnnotationType":"Thing"}"""), "acme", "billing", "default", "x.y");

        result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>().Message.ShouldContain("AnnotationType");
    }

    private static TestHttpRequestData Request(string method, string body)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var (context, request) = FunctionTestDoubles.CreateRequest(services, method: method, body: body);
        context.Items[FunctionContextItemKeys.CachedClaims] = new List<Claim> { new("sub", "user_01"), new("scp", Scopes.Annotation.Write) };
        return request;
    }

    private sealed class RecordingAnnotations : IAnnotationService
    {
        public List<string> Calls { get; } = [];

        public HashSet<string> Existing { get; init; } = [];

        public Task<bool> ExistAnnotationAsync(FullKey fullKey)
        {
            Calls.Add($"exists:{fullKey.OrganizationName}:{fullKey.Annotation}");
            return Task.FromResult(Existing.Contains(fullKey.Annotation.ToString()));
        }

        public Task<IEnumerable<Annotation>> CreateAnnotationAsync(string organization, Annotation annotation)
        {
            Calls.Add($"create:{organization}:{annotation.AnnotationKey}");
            return Task.FromResult<IEnumerable<Annotation>>([annotation]);
        }

        public Task UpdateAnnotationAsync(string organization, Annotation annotation)
        {
            Calls.Add($"update:{organization}:{annotation.AnnotationKey}");
            return Task.CompletedTask;
        }

        public Task<Annotation?> GetAnnotationAsync(FullKey fullKey) => throw new NotImplementedException();

        public Task<AnnotationsResponse> GetAnnotationsAsync(AnnotationsRequest request) => throw new NotImplementedException();

        public Task<IEnumerable<Annotation>> CreateAnnotationsAsync(string organization, IEnumerable<Annotation> annotations) => throw new NotImplementedException();

        public Task<IEnumerable<Annotation>> CreateAnnotationsFromStringAsync(string organization, IEnumerable<string> annotations) => throw new NotImplementedException();

        public Task DeleteAnnotationAsync(FullKey fullKey) => throw new NotImplementedException();
    }

    // The signed-in user is a Contributor of every project.
    private sealed class ContributorAccess : IAccessService
    {
        public Task<AccountRole> GetAccountRoleAsync(string accountId, string accessPointKey) => Task.FromResult(AccountRole.Contributor);

        public Task<Account?> GetAccountAsync(string id) => throw new NotImplementedException();

        public Task<Account> CreateAccountAsync(AccountCreate model) => throw new NotImplementedException();

        public Task<IEnumerable<AccessPoint>> GetAccessPointsAsync(string accountId) => throw new NotImplementedException();

        public Task<AccessPoint?> GetAccessPointAsync(string key) => throw new NotImplementedException();

        public Task<AccessPoint> CreateAccessPointAsync(AccessPointCreate model) => throw new NotImplementedException();

        public Task<bool> GrantPermissionAsync(Permission model) => throw new NotImplementedException();

        public Task<bool> RevokePermissionAsync(Permission model) => throw new NotImplementedException();

        public Task<IReadOnlyList<AccessPointMember>> GetMembersAsync(string accessPointKey, string actorId) => throw new NotImplementedException();

        public Task<AccessPointMember> SetMemberRoleAsync(string accessPointKey, string accountId, AccountRole role, string actorId) => throw new NotImplementedException();

        public Task RemoveMemberAsync(string accessPointKey, string accountId, string actorId) => throw new NotImplementedException();

        public Task<AccountRole> JoinAccessPointAsync(string accessPointKey, string accountId, AccountRole role) => throw new NotImplementedException();

        public Task<IEnumerable<MachineAccess>> GetMachineAccessesAsync(string organization, string project) => throw new NotImplementedException();

        public Task<MachineAccess?> GetMachineAccessAsync(string organization, string project, string id) => throw new NotImplementedException();

        public Task<MachineAccess> CreateMachineAccessAsync(MachineAccessCreate model) => throw new NotImplementedException();

        public Task<MachineAccess> ResetMachineAccessAsync(string organization, string project, string appId) => throw new NotImplementedException();

        public Task<bool> DeleteMachineAccessAsync(string organization, string project, string appId) => throw new NotImplementedException();

        public Task<bool> VerifyAccessForMachineAsync(string organization, string project, string appId) => throw new NotImplementedException();
    }
}

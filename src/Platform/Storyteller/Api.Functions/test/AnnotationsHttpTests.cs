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
    public async Task Set_StoresTheTypedAnnotationUnderTheRouteAndReturnsTheStoredOne(string method)
    {
        var annotations = new RecordingAnnotations();

        var result = await Http(annotations).SetAnnotation(Request(method, UsageBody), "acme", "billing", "default", "usg.northwind.invoicing");

        var stored = result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<Usage>();
        stored.ProjectName.ShouldBe("billing");
        stored.ViewName.ShouldBe("default");
        stored.Title.ShouldBe("Invoices of Northwind");
        stored.SubjectKey.ShouldBe("sbt.northwind");
        stored.Created.ShouldNotBeNull("the stored annotation is returned, not the body");
        annotations.Calls.ShouldBe(["set:acme:usg.northwind.invoicing"]);
    }

    [Fact]
    public async Task Set_InvalidContent_AnswersTheDomainError()
    {
        var annotations = new RecordingAnnotations { Failure = new InvalidInputException("Title is too long.", ErrorCodes.InvalidAnnotation) };

        var result = await Http(annotations).SetAnnotation(Request("POST", UsageBody), "acme", "billing", "default", "usg.northwind.invoicing");

        var error = result.ShouldBeOfType<ObjectResult>();
        error.StatusCode.ShouldBe(400);
        error.Value.ShouldBeOfType<ErrorResponse>().ErrorCode.ShouldBe(ErrorCodes.InvalidAnnotation);
    }

    [Fact]
    public async Task Set_InvalidKey_IsRejectedWithoutCallingTheService()
    {
        var annotations = new RecordingAnnotations();

        var result = await Http(annotations).SetAnnotation(Request("POST", UsageBody), "acme", "billing", "default", "nope");

        result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>().ErrorCode.ShouldBe(ErrorCodes.InvalidKey);
        annotations.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Set_BodyWithoutTheTypeSpecificMembers_IsRejected()
    {
        var annotations = new RecordingAnnotations();
        const string baseOnly = """{"AnnotationKey":"usg.northwind.invoicing","Name":"invoicing","AnnotationType":"Usage","ProjectName":"x","ViewName":"y"}""";

        var result = await Http(annotations).SetAnnotation(Request("POST", baseOnly), "acme", "billing", "default", "usg.northwind.invoicing");

        result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>().Message.ShouldContain("SubjectKey");
        annotations.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Set_UnknownType_IsRejected()
    {
        var result = await Http(new RecordingAnnotations()).SetAnnotation(Request("POST", """{"AnnotationKey":"x.y","AnnotationType":"Thing"}"""), "acme", "billing", "default", "x.y");

        result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>().Message.ShouldContain("AnnotationType");
    }

    [Fact]
    public async Task Delete_ReturnsTheDeletion()
    {
        var annotations = new RecordingAnnotations();

        var result = await Http(annotations).DeleteAnnotation(Request("DELETE"), "acme", "billing", "default", "sbt.northwind");

        var deletion = result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<AnnotationDeletionResult>();
        deletion.Deletion.RootKey.ShouldBe("sbt.northwind");
        annotations.Calls.ShouldBe(["delete:acme:sbt.northwind"]);
    }

    [Fact]
    public async Task Delete_UnknownKey_Answers404()
    {
        var annotations = new RecordingAnnotations { Failure = new NotFoundException("missing") };

        var result = await Http(annotations).DeleteAnnotation(Request("DELETE"), "acme", "billing", "default", "sbt.northwind");

        result.ShouldBeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Descendants_AskTheServiceForTheGraphOfTheKey()
    {
        var annotations = new RecordingAnnotations();

        await Http(annotations).GetDescendants(Request("GET"), "acme", "billing", "default", "cnt.northwind.prod", "all");

        var request = annotations.LastRequest.ShouldNotBeNull();
        request.DescendantsOf!.ToString().ShouldBe("cnt.northwind.prod");
        request.Conditions.ShouldBeEmpty();
        request.PartitionKey.ShouldBeNull();
    }

    [Fact]
    public async Task DeletedList_PassesTheFilters()
    {
        var annotations = new RecordingAnnotations();

        await Http(annotations).GetDeletedAnnotations(Request("GET"), "acme", "billing", "default", roots: false, deletionId: "d1", continuationToken: "t1");

        annotations.Calls.ShouldBe(["deleted:acme:billing:default:False:d1:t1"]);
    }

    [Fact]
    public async Task DeletedAnnotation_NotDeleted_Answers404()
    {
        var result = await Http(new RecordingAnnotations()).GetDeletedAnnotation(Request("GET"), "acme", "billing", "default", "sbt.northwind");

        result.ShouldBeOfType<NotFoundResult>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task Lookup_WithoutKeysOrWithTooMany_IsRejected(int count)
    {
        var annotations = new RecordingAnnotations();
        var keys = Enumerable.Range(0, count).Select(index => $"sbt.s{index}").ToList();

        var result = await Http(annotations).FindDeletedAnnotations(Request("POST"), new AnnotationKeysRequest { Keys = keys }, "acme", "billing", "default");

        result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>().ErrorCode.ShouldBe(ErrorCodes.InvalidKey);
        annotations.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Lookup_WithInvalidKeys_ListsThem()
    {
        var annotations = new RecordingAnnotations();

        var result = await Http(annotations).FindDeletedAnnotations(Request("POST"), new AnnotationKeysRequest { Keys = ["sbt.ok", "nope", "xyz.a"] }, "acme", "billing", "default");

        var error = result.ShouldBeOfType<BadRequestObjectResult>().Value.ShouldBeOfType<ErrorResponse>();
        error.ErrorCode.ShouldBe(ErrorCodes.InvalidKey);
        error.Message.ShouldContain("nope");
        error.Message.ShouldContain("xyz.a");
        annotations.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Lookup_PassesTheParsedKeys()
    {
        var annotations = new RecordingAnnotations();

        var result = await Http(annotations).FindDeletedAnnotations(Request("POST"), new AnnotationKeysRequest { Keys = ["sbt.a", "usg.a.b"] }, "acme", "billing", "default");

        result.ShouldBeOfType<OkObjectResult>();
        annotations.Calls.ShouldBe(["find:acme:billing:default:sbt.a,usg.a.b"]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Restore_PassesTheKeysTheDescendantsFlagAndTheCaller(bool descendants)
    {
        var annotations = new RecordingAnnotations();
        var request = Request("POST");

        var result = await Http(annotations).RestoreAnnotations(request, new AnnotationRestoreRequest { Keys = ["sbt.a"], Descendants = descendants }, "acme", "billing", "default");

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<AnnotationRestoreResult>();
        annotations.Calls.ShouldBe([$"restore:acme:billing:default:sbt.a:{descendants}"]);
        annotations.LastActor.ShouldBe(request.GetAuditActor());
    }

    [Fact]
    public async Task Purge_ByMachine_IsDenied()
    {
        var annotations = new RecordingAnnotations();
        var request = Request("DELETE");
        request.FunctionContext.Items[FunctionContextItemKeys.MachineIdentity] = "machine-1";

        await Should.ThrowAsync<AccessDeniedException>(() => Http(annotations).PurgeAnnotation(request, "acme", "billing", "default", "sbt.a"));
        annotations.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Purge_ByUser_CallsTheService()
    {
        var annotations = new RecordingAnnotations();

        var result = await Http(annotations).PurgeAnnotation(Request("DELETE"), "acme", "billing", "default", "sbt.a");

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<AnnotationPurgeResult>();
        annotations.Calls.ShouldBe(["purge:acme:sbt.a"]);
    }

    [Fact]
    public async Task History_OfUnknownKey_Answers404()
    {
        var result = await Http(new RecordingAnnotations()).GetAnnotationHistory(Request("GET"), "acme", "billing", "default", "sbt.a");

        result.ShouldBeOfType<NotFoundResult>();
    }

    private static AnnotationsHttp Http(RecordingAnnotations annotations)
    {
        return new AnnotationsHttp(annotations, new ContributorAccess(), NullLogger<AnnotationsHttp>.Instance);
    }

    private static TestHttpRequestData Request(string method, string body = "")
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var (context, request) = FunctionTestDoubles.CreateRequest(services, method: method, body: body);
        context.Items[FunctionContextItemKeys.CachedClaims] = new List<Claim>
        {
            new("sub", "user_01"),
            new("scp", $"{Scopes.Annotation.Read} {Scopes.Annotation.Write}"),
        };
        return request;
    }

    private sealed class RecordingAnnotations : IAnnotationService
    {
        private static readonly AuditStamp Stamp = new() { At = DateTimeOffset.UnixEpoch, By = AuditActor.System };

        public List<string> Calls { get; } = [];

        public Exception? Failure { get; init; }

        public AnnotationsRequest? LastRequest { get; private set; }

        public AuditActor? LastActor { get; private set; }

        public Task<Annotation> SetAnnotationAsync(string organization, Annotation annotation, AuditActor actor)
        {
            Calls.Add($"set:{organization}:{annotation.AnnotationKey}");
            LastActor = actor;
            return Failure is null
                ? Task.FromResult(annotation with { Created = Stamp, Modified = Stamp })
                : Task.FromException<Annotation>(Failure);
        }

        public Task<AnnotationDeletionResult> DeleteAnnotationAsync(FullKey fullKey, AuditActor actor)
        {
            if (Failure is not null)
            {
                return Task.FromException<AnnotationDeletionResult>(Failure);
            }

            Calls.Add($"delete:{fullKey.OrganizationName}:{fullKey.Annotation}");
            return Task.FromResult(new AnnotationDeletionResult
            {
                Deletion = new AnnotationDeletion { DeletionId = "d1", RootKey = fullKey.Annotation, DeletedAt = DateTimeOffset.UnixEpoch, DeletedBy = actor, IsRoot = true },
                DeletedKeys = [fullKey.Annotation],
            });
        }

        public Task<AnnotationsResponse> GetAnnotationsAsync(AnnotationsRequest request)
        {
            LastRequest = request;
            return Task.FromResult(new AnnotationsResponse());
        }

        public Task<AnnotationsResponse> GetDeletedAnnotationsAsync(string organization, string project, string view, bool rootsOnly = true, string? deletionId = null, string? continuationToken = null)
        {
            Calls.Add($"deleted:{organization}:{project}:{view}:{rootsOnly}:{deletionId}:{continuationToken}");
            return Task.FromResult(new AnnotationsResponse());
        }

        public Task<IReadOnlyList<Annotation>> FindDeletedAnnotationsAsync(string organization, string project, string view, IReadOnlyCollection<AnnotationKey> keys)
        {
            Calls.Add($"find:{organization}:{project}:{view}:{string.Join(",", keys.Select(key => key.ToString()))}");
            return Task.FromResult<IReadOnlyList<Annotation>>([]);
        }

        public Task<AnnotationRestoreResult> RestoreAnnotationsAsync(string organization, string project, string view, IReadOnlyCollection<AnnotationKey> keys, AuditActor actor, bool includeDescendants = true)
        {
            Calls.Add($"restore:{organization}:{project}:{view}:{string.Join(",", keys.Select(key => key.ToString()))}:{includeDescendants}");
            LastActor = actor;
            return Task.FromResult(new AnnotationRestoreResult { Annotations = [] });
        }

        public Task<AnnotationPurgeResult> PurgeAnnotationAsync(FullKey fullKey, AuditActor actor)
        {
            Calls.Add($"purge:{fullKey.OrganizationName}:{fullKey.Annotation}");
            return Task.FromResult(new AnnotationPurgeResult { PurgedKeys = [fullKey.Annotation] });
        }

        public Task<Annotation?> GetDeletedAnnotationAsync(FullKey fullKey) => Task.FromResult<Annotation?>(null);

        public Task<AnnotationHistory?> GetAnnotationHistoryAsync(FullKey fullKey) => Task.FromResult<AnnotationHistory?>(null);

        public Task<bool> ExistAnnotationAsync(FullKey fullKey) => throw new NotImplementedException();

        public Task<Annotation?> GetAnnotationAsync(FullKey fullKey) => throw new NotImplementedException();

        public Task<IEnumerable<Annotation>> CreateAnnotationAsync(string organization, Annotation annotation, AuditActor actor) => throw new NotImplementedException();

        public Task UpdateAnnotationAsync(string organization, Annotation annotation, AuditActor actor) => throw new NotImplementedException();

        public Task<IEnumerable<Annotation>> CreateAnnotationsAsync(string organization, IEnumerable<Annotation> annotations, AuditActor actor) => throw new NotImplementedException();

        public Task<IEnumerable<Annotation>> CreateAnnotationsFromStringAsync(string organization, IEnumerable<string> annotations, AuditActor actor) => throw new NotImplementedException();
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

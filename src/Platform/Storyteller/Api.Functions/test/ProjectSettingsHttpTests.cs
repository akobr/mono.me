using System.Net;
using System.Reflection;
using System.Text.Json.Nodes;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Api.Models;
using _42.Platform.Storyteller.Api.Security;
using _42.Platform.Storyteller.Api.V1;
using _42.Platform.Storyteller.KeyValues;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// The settings of a project over HTTP (spec 424-266 section 5): who reads and writes them, their ETag, the conditions
// of the writes, the bodies and the answers of invalid settings.
public class ProjectSettingsHttpTests
{
    private const string Org = "acme";
    private const string Project = "web";

    private static readonly ProjectSettings Stored = new()
    {
        Types = new Dictionary<string, ProjectTypeAppearance> { ["sbt"] = new() { Label = "Company", Color = "pink" } },
        DefaultTimeZone = "Europe/Prague",
        Version = 3,
        Modified = AuditStamp.Of(AuditActor.Account("user-0", "Grace"), new DateTimeOffset(2026, 11, 14, 8, 0, 0, TimeSpan.Zero)),
    };

    [Fact]
    public async Task Get_ByAReader_AnswersTheSettingsWithTheTagOfTheirVersion()
    {
        var settings = new FakeProjectSettings { Stored = Stored };
        var request = WithContext(TestRequests.User());

        var result = await Http(settings, new RoleAccess(AccountRole.Reader)).GetProjectSettings(request, Org, Project);

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBe(Stored);
        Headers(request).ETag.ToString().ShouldBe("\"3\"");
        Headers(request).CacheControl.ToString().ShouldBe("private, no-cache");
        settings.Calls.ShouldBe(["get:acme.web"]);
    }

    // Never 404 for an existing project: the defaults at version 0.
    [Fact]
    public async Task Get_NothingSet_AnswersTheDefaultsAtVersionZero()
    {
        var request = WithContext(TestRequests.User());

        var result = await Http(new FakeProjectSettings(), new RoleAccess(AccountRole.Reader)).GetProjectSettings(request, Org, Project);

        var value = result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<ProjectSettings>();
        value.Types.ShouldBeEmpty();
        value.DefaultTimeZone.ShouldBeNull();
        value.DateFormat.ShouldBeNull();
        value.Version.ShouldBe(0);
        Headers(request).ETag.ToString().ShouldBe("\"0\"");
    }

    // If-None-Match compares weakly, and * matches.
    [Theory]
    [InlineData("\"3\"")]
    [InlineData("W/\"3\"")]
    [InlineData("\"2\", \"3\"")]
    [InlineData("*")]
    public async Task Get_IfNoneMatchOfTheVersion_AnswersNotModified(string ifNoneMatch)
    {
        var request = WithContext(TestRequests.User());
        request.Headers.TryAddWithoutValidation(HeaderNames.IfNoneMatch, ifNoneMatch);

        var result = await Http(new FakeProjectSettings { Stored = Stored }, new RoleAccess(AccountRole.Reader)).GetProjectSettings(request, Org, Project);

        result.ShouldBeOfType<StatusCodeResult>().StatusCode.ShouldBe(StatusCodes.Status304NotModified);
        Headers(request).ETag.ToString().ShouldBe("\"3\"");
    }

    [Fact]
    public async Task Get_IfNoneMatchOfAnotherVersion_AnswersTheSettings()
    {
        var request = WithContext(TestRequests.User());
        request.Headers.TryAddWithoutValidation(HeaderNames.IfNoneMatch, "\"2\"");

        var result = await Http(new FakeProjectSettings { Stored = Stored }, new RoleAccess(AccountRole.Reader)).GetProjectSettings(request, Org, Project);

        result.ShouldBeOfType<OkObjectResult>();
    }

    // The settings hold no annotation data: either read scope of a machine reads them, as for the views.
    [Theory]
    [InlineData(MachineScope.AnnotationRead)]
    [InlineData(MachineScope.ConfigurationRead)]
    public async Task Get_ByAMachineWithOneReadScope_AnswersTheSettings(MachineScope scope)
    {
        var settings = new FakeProjectSettings { Stored = Stored };

        var result = await Http(settings, new RoleAccess(AccountRole.None, scopes: [scope])).GetProjectSettings(WithContext(TestRequests.Machine([scope])), Org, Project);

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBe(Stored);
    }

    [Fact]
    public async Task Get_ByAMachineRestrictedToAnAnnotation_AnswersTheSettings()
    {
        var access = new RoleAccess(AccountRole.None, machineAnnotationKey: "sbt.northwind");

        var result = await Http(new FakeProjectSettings { Stored = Stored }, access).GetProjectSettings(WithContext(TestRequests.Machine()), Org, Project);

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBe(Stored);
    }

    [Fact]
    public async Task Get_ByAMachineCreatedWithNeitherReadScope_IsAccessDenied()
    {
        var withoutReads = MachineScopes.All.Where(scope => scope is not (MachineScope.AnnotationRead or MachineScope.ConfigurationRead)).ToList();
        var http = Http(Untouched<IProjectSettingsService>(), new RoleAccess(AccountRole.None, scopes: withoutReads));

        var exception = await Should.ThrowAsync<AccessDeniedException>(() => http.GetProjectSettings(TestRequests.Machine(), Org, Project));

        exception.ErrorCode.ShouldBe(ErrorCodes.AccessDenied);
        exception.Message.ShouldBe("This operation needs one of the scopes Annotation.Read or Configuration.Read, which the machine was not created with.");
    }

    [Fact]
    public async Task Get_ByAUserWithoutARole_IsAccessDenied()
    {
        using var roleChecks = UserRoleChecks.Enforce();
        var http = Http(Untouched<IProjectSettingsService>(), new RoleAccess(AccountRole.None));

        var exception = await Should.ThrowAsync<AccessDeniedException>(() => http.GetProjectSettings(TestRequests.User(), Org, Project));

        exception.Message.ShouldBe("No Reader access to 'acme.web'.");
    }

    // Machines have no role: a write is refused before the machine's document or the settings are read, whatever its
    // credentials carry, in every build.
    [Fact]
    public async Task Writes_ByAMachine_AreRefusedBeforeAnyCheck()
    {
        var http = Http(Untouched<IProjectSettingsService>(), Untouched<IAccessService>());

        await UserOnlyRoutes.ShouldRefuseMachineAsync(() => http.SetProjectSettings(UserOnlyRoutes.Machine("PUT", "{}"), Org, Project), ProjectSettingsHttp.ChangingSettingsNeedsUser);
        await UserOnlyRoutes.ShouldRefuseMachineAsync(() => http.PatchProjectSettings(UserOnlyRoutes.Machine("PATCH", "{}"), Org, Project), ProjectSettingsHttp.ChangingSettingsNeedsUser);
        ProjectSettingsHttp.ChangingSettingsNeedsUser.ShouldBe("Changing the project settings needs a user with the Administrator role.");
    }

    // The role checks of a Release build run here in every build.
    [Theory]
    [InlineData(AccountRole.Reader)]
    [InlineData(AccountRole.Contributor)]
    [InlineData(AccountRole.ContributorWithSecrets)]
    public async Task Writes_BelowAdministrator_AreAccessDenied(AccountRole role)
    {
        using var roleChecks = UserRoleChecks.Enforce();
        var http = Http(Untouched<IProjectSettingsService>(), new RoleAccess(role));

        (await Should.ThrowAsync<AccessDeniedException>(() => http.SetProjectSettings(TestRequests.User("PUT", "{}"), Org, Project)))
            .Message.ShouldBe("No Administrator access to 'acme.web'.");
        (await Should.ThrowAsync<AccessDeniedException>(() => http.PatchProjectSettings(TestRequests.User("PATCH", "{}"), Org, Project)))
            .Message.ShouldBe("No Administrator access to 'acme.web'.");
    }

    [Theory]
    [InlineData(AccountRole.Administrator)]
    [InlineData(AccountRole.Owner)]
    public async Task Put_ByAnAdministrator_StoresTheNormalizedSettingsAndAnswersTheirTag(AccountRole role)
    {
        using var roleChecks = UserRoleChecks.Enforce();
        var settings = new FakeProjectSettings();
        var request = WithContext(TestRequests.User("PUT", """{ "types": { "SBT": { "label": " Company ", "color": "PINK" } }, "defaultTimeZone": "europe/prague" }"""));

        var result = await Http(settings, new RoleAccess(role)).SetProjectSettings(request, Org, Project);

        var value = result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<ProjectSettings>();
        value.Version.ShouldBe(1);
        Headers(request).ETag.ToString().ShouldBe("\"1\"");
        var written = settings.LastSettings.ShouldNotBeNull();
        written.Types.Keys.ShouldBe(["sbt"]);
        written.Types["sbt"].ShouldBe(new ProjectTypeAppearance { Label = "Company", Color = "pink" });
        written.DefaultTimeZone.ShouldBe("Europe/Prague");
        settings.LastCondition.ShouldBe(ProjectSettingsCondition.None);
        settings.LastCaller.ShouldNotBeNull().Actor.Id.ShouldBe(TestRequests.UserId);
        settings.LastCaller.Channel.ShouldBe(KeyValueChannel.User);
        settings.LastCaller.CanReadSecrets.ShouldBeFalse();
    }

    // A read, or a data set's file, goes back as it is: Version, Modified and Format are ignored.
    [Fact]
    public async Task Put_OfAReadOrAFile_IgnoresVersionModifiedAndFormat()
    {
        var settings = new FakeProjectSettings { Stored = Stored };
        var read = ProjectSettingsJson.ToJson(Stored, ProjectSettingsJsonShape.Api);
        read[nameof(ProjectSettings.Version)] = 99;
        read["Format"] = ProjectSettings.FileFormat;

        var result = await Http(settings, new RoleAccess(AccountRole.Administrator)).SetProjectSettings(WithContext(TestRequests.User("PUT", read.ToJsonString())), Org, Project);

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<ProjectSettings>().Version.ShouldBe(3);
        settings.LastSettings.ShouldNotBeNull().Version.ShouldBe(0);
        settings.LastSettings.Modified.ShouldBeNull();
    }

    // A write that changes nothing keeps the version, and answers it.
    [Fact]
    public async Task Put_OfTheStoredSettings_AnswersTheUnchangedVersion()
    {
        var request = WithContext(TestRequests.User("PUT", ProjectSettingsJson.ToJson(Stored, ProjectSettingsJsonShape.File).ToJsonString()));

        var result = await Http(new FakeProjectSettings { Stored = Stored }, new RoleAccess(AccountRole.Administrator)).SetProjectSettings(request, Org, Project);

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBe(Stored);
        Headers(request).ETag.ToString().ShouldBe("\"3\"");
    }

    // Every problem with its path and rule, the message naming the first and their count; nothing is written.
    [Fact]
    public async Task Put_OfInvalidSettings_IsABadRequestWithEveryProblem()
    {
        var settings = new FakeProjectSettings();
        var body = """{ "Types": { "sbt": { "Color": "beige" }, "xyz": {} }, "DateFormat": "yy" }""";

        var result = await Http(settings, new RoleAccess(AccountRole.Administrator)).SetProjectSettings(WithContext(TestRequests.User("PUT", body)), Org, Project);

        var error = BadRequest(result, ErrorCodes.InvalidSettings);
        var problems = error.Details.ShouldBeAssignableTo<IReadOnlyList<ProjectSettingsProblem>>().ShouldNotBeNull();
        problems.Select(problem => (problem.Path, problem.Rule)).ShouldBe(
            [("$.Types.xyz", ProjectSettingsRules.TypeCode), ("$.Types.sbt.Color", ProjectSettingsRules.Color), ("$.DateFormat", ProjectSettingsRules.DateFormat)],
            ignoreOrder: true);
        error.Message.ShouldStartWith("The project settings have 3 problems; the first is ");
        error.Message.ShouldContain(problems[0].Path);
        settings.Calls.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("[1, 2]")]
    [InlineData("\"settings\"")]
    [InlineData("{ \"Types\": ")]
    [InlineData("{ \"DateFormat\": \"yyyy-mm-dd\", \"DateFormat\": \"dd.mm.yyyy\" }")]
    public async Task Put_OfABodyThatIsNoJsonObject_IsABadRequest(string body)
    {
        var settings = new FakeProjectSettings();

        var result = await Http(settings, new RoleAccess(AccountRole.Administrator)).SetProjectSettings(WithContext(TestRequests.User("PUT", body)), Org, Project);

        var problem = BadRequest(result, ErrorCodes.InvalidSettings).Details.ShouldBeAssignableTo<IReadOnlyList<ProjectSettingsProblem>>().ShouldNotBeNull().ShouldHaveSingleItem();
        problem.Path.ShouldBe("$");
        problem.Rule.ShouldBe(ProjectSettingsRules.WrongType);
        settings.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Writes_OfABodyAboveTheLimit_AreTooLarge()
    {
        var body = $$"""{ "DefaultTimeZone": "{{new string(' ', RequestBodyLimits.ProjectSettings)}}" }""";
        var http = Http(new FakeProjectSettings(), new RoleAccess(AccountRole.Administrator));

        foreach (var result in new[]
                 {
                     await http.SetProjectSettings(WithContext(TestRequests.User("PUT", body)), Org, Project),
                     await http.PatchProjectSettings(WithContext(TestRequests.User("PATCH", body)), Org, Project),
                 })
        {
            var tooLarge = result.ShouldBeOfType<ObjectResult>();
            tooLarge.StatusCode.ShouldBe((int)HttpStatusCode.RequestEntityTooLarge);
            tooLarge.Value.ShouldBeOfType<ErrorResponse>().ErrorCode.ShouldBe(ErrorCodes.BodyTooLarge);
        }
    }

    // If-Match compares strongly against the quoted Version; * matches any version.
    [Theory]
    [InlineData("\"3\"")]
    [InlineData("\"1\", \"3\"")]
    [InlineData("*")]
    public async Task Writes_IfMatchOfTheVersion_AreWritten(string ifMatch)
    {
        var settings = new FakeProjectSettings { Stored = Stored };
        var http = Http(settings, new RoleAccess(AccountRole.Administrator));
        var put = WithContext(TestRequests.User("PUT", """{ "DateFormat": "dd.mm.yyyy" }"""));
        put.Headers.TryAddWithoutValidation(HeaderNames.IfMatch, ifMatch);

        (await http.SetProjectSettings(put, Org, Project)).ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<ProjectSettings>().Version.ShouldBe(4);

        var patch = WithContext(MergePatchRequest("""{ "DateFormat": null }"""));
        patch.Headers.TryAddWithoutValidation(HeaderNames.IfMatch, ifMatch.Replace("3", "4"));

        (await http.PatchProjectSettings(patch, Org, Project)).ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<ProjectSettings>().Version.ShouldBe(5);
    }

    // A weak tag never matches, nor does another version or a tag that is no version: 412 with the current version.
    [Theory]
    [InlineData("\"2\"")]
    [InlineData("W/\"3\"")]
    [InlineData("\"03\"")]
    [InlineData("\"a1.Ag\"")]
    public async Task Writes_IfMatchOfAnotherVersion_FailWithTheCurrentVersion(string ifMatch)
    {
        var http = Http(new FakeProjectSettings { Stored = Stored }, new RoleAccess(AccountRole.Administrator));
        var put = TestRequests.User("PUT", """{ "DateFormat": "dd.mm.yyyy" }""");
        var patch = MergePatchRequest("""{ "DateFormat": "dd.mm.yyyy" }""");
        put.Headers.TryAddWithoutValidation(HeaderNames.IfMatch, ifMatch);
        patch.Headers.TryAddWithoutValidation(HeaderNames.IfMatch, ifMatch);

        foreach (var result in new[] { await http.SetProjectSettings(put, Org, Project), await http.PatchProjectSettings(patch, Org, Project) })
        {
            var failed = result.ShouldBeOfType<ObjectResult>();
            failed.StatusCode.ShouldBe((int)HttpStatusCode.PreconditionFailed);
            var error = failed.Value.ShouldBeOfType<ErrorResponse>();
            error.ErrorCode.ShouldBe(ErrorCodes.SettingsPreconditionFailed);
            error.Message.ShouldBe("The project settings changed meanwhile; they are at version 3.");
            error.Details.ShouldBeOfType<ProjectSettingsVersionDetails>().Version.ShouldBe(3);
        }
    }

    [Theory]
    [InlineData("If-Match", "3")]
    [InlineData("If-Match", "*, \"3\"")]
    [InlineData("If-Match", "")]
    [InlineData("If-None-Match", "*")]
    [InlineData("If-None-Match", "\"3\"")]
    public async Task Writes_WithAnInvalidCondition_AreABadRequest(string header, string value)
    {
        var settings = new FakeProjectSettings { Stored = Stored };
        var http = Http(settings, new RoleAccess(AccountRole.Administrator));
        var put = TestRequests.User("PUT", "{}");
        var patch = MergePatchRequest("{}");
        put.Headers.TryAddWithoutValidation(header, value);
        patch.Headers.TryAddWithoutValidation(header, value);

        foreach (var result in new[] { await http.SetProjectSettings(put, Org, Project), await http.PatchProjectSettings(patch, Org, Project) })
        {
            var error = BadRequest(result, ErrorCodes.InvalidPrecondition);

            if (header == "If-None-Match")
            {
                error.Message.ShouldBe("The project settings always exist; a write takes If-Match.");
            }
        }

        settings.Calls.ShouldBeEmpty();
    }

    // RFC 7396 against the stored settings: a member left out stays, null removes, a type merges member by member.
    [Fact]
    public async Task Patch_MergesIntoTheStoredSettings()
    {
        var settings = new FakeProjectSettings { Stored = Stored };
        var request = WithContext(MergePatchRequest("""{ "Types": { "SBT": { "Color": null, "Icon": "i-lucide-building-2" }, "rst": { "Label": "Solution" } }, "DefaultTimeZone": null }"""));

        var result = await Http(settings, new RoleAccess(AccountRole.Administrator)).PatchProjectSettings(request, Org, Project);

        var value = result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<ProjectSettings>();
        value.Types.Keys.ShouldBe(["rst", "sbt"]);
        value.Types["sbt"].ShouldBe(new ProjectTypeAppearance { Label = "Company", Icon = "i-lucide-building-2" });
        value.Types["rst"].ShouldBe(new ProjectTypeAppearance { Label = "Solution" });
        value.DefaultTimeZone.ShouldBeNull();
        value.Version.ShouldBe(4);
        Headers(request).ETag.ToString().ShouldBe("\"4\"");
        settings.LastPatch.ShouldNotBeNull().ToJsonString().ShouldContain("\"Color\":null");
    }

    [Theory]
    [InlineData("application/merge-patch+json")]
    [InlineData("application/merge-patch+json; charset=utf-8")]
    [InlineData("application/json")]
    [InlineData(null)]
    public async Task Patch_TakesAMergePatchOrJson(string? contentType)
    {
        var request = WithContext(PatchRequest("""{ "DateFormat": "d mmm yyyy" }""", contentType));

        var result = await Http(new FakeProjectSettings(), new RoleAccess(AccountRole.Administrator)).PatchProjectSettings(request, Org, Project);

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<ProjectSettings>().DateFormat.ShouldBe("d mmm yyyy");
    }

    [Theory]
    [InlineData("application/json-patch+json")]
    [InlineData("text/plain")]
    [InlineData("not a media type")]
    public async Task Patch_OfAnotherMediaType_IsUnsupported(string contentType)
    {
        var settings = new FakeProjectSettings();
        var request = WithContext(PatchRequest("""{ "DateFormat": "d mmm yyyy" }""", contentType));

        var result = await Http(settings, new RoleAccess(AccountRole.Administrator)).PatchProjectSettings(request, Org, Project);

        var unsupported = result.ShouldBeOfType<ObjectResult>();
        unsupported.StatusCode.ShouldBe((int)HttpStatusCode.UnsupportedMediaType);
        unsupported.Value.ShouldBeOfType<ErrorResponse>().ErrorCode.ShouldBe(ErrorCodes.InvalidContentType);
        Headers(request)["Accept-Patch"].ToString().ShouldBe("application/merge-patch+json");
        settings.Calls.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("\"DateFormat\"")]
    [InlineData("{ \"DateFormat\": ")]
    [InlineData("{ \"DateFormat\": null, \"DateFormat\": \"yyyy-mm-dd\" }")]
    public async Task Patch_OfABodyThatIsNoJsonObject_IsAnInvalidPatch(string body)
    {
        var settings = new FakeProjectSettings();

        var result = await Http(settings, new RoleAccess(AccountRole.Administrator)).PatchProjectSettings(WithContext(MergePatchRequest(body)), Org, Project);

        BadRequest(result, ErrorCodes.PatchInvalid).Message.ShouldStartWith("The body is not a merge patch of the project settings: ");
        settings.Calls.ShouldBeEmpty();
    }

    // The merged result is checked as a full write.
    [Fact]
    public async Task Patch_ToInvalidSettings_IsABadRequestWithEveryProblem()
    {
        var result = await Http(new FakeProjectSettings { Stored = Stored }, new RoleAccess(AccountRole.Administrator))
            .PatchProjectSettings(WithContext(MergePatchRequest("""{ "Types": { "sbt": { "Label": null, "Plural": "Companies" } }, "DefaultTimeZone": "Central Europe Standard Time" }""")), Org, Project);

        var error = BadRequest(result, ErrorCodes.InvalidSettings);
        error.Details.ShouldBeAssignableTo<IReadOnlyList<ProjectSettingsProblem>>().ShouldNotBeNull().Select(problem => problem.Rule)
            .ShouldBe([ProjectSettingsRules.PluralWithoutLabel, ProjectSettingsRules.TimeZone], ignoreOrder: true);
    }

    private static ProjectSettingsHttp Http(IProjectSettingsService settings, IAccessService access) => new(settings, access);

    private static TestHttpRequestData MergePatchRequest(string body) => PatchRequest(body, "application/merge-patch+json");

    private static TestHttpRequestData PatchRequest(string body, string? contentType)
    {
        var request = TestRequests.User("PATCH", body);
        request.Headers.Remove(HeaderNames.ContentType);

        if (contentType is not null)
        {
            request.Headers.TryAddWithoutValidation(HeaderNames.ContentType, contentType);
        }

        return request;
    }

    private static TestHttpRequestData WithContext(TestHttpRequestData request) => ConfigurationConditionalReadTests.WithHttpContext(request, ifNoneMatch: null);

    private static IHeaderDictionary Headers(TestHttpRequestData request) => ConfigurationConditionalReadTests.Headers(request);

    private static ErrorResponse BadRequest(IActionResult result, string errorCode)
    {
        var error = result.ShouldBeAssignableTo<ObjectResult>().ShouldNotBeNull();
        error.StatusCode.ShouldBe((int)HttpStatusCode.BadRequest);
        var response = error.Value.ShouldBeOfType<ErrorResponse>();
        response.ErrorCode.ShouldBe(errorCode);
        return response;
    }

    private static T Untouched<T>()
        where T : class
    {
        return DispatchProxy.Create<T, MachineRestrictionHttpTests.UntouchedProxy>();
    }

    // The service as the Cosmos one behaves for one project: the condition against the stored version, the rules, a
    // no-op write that keeps the version, Version + 1 for a change.
    private sealed class FakeProjectSettings : IProjectSettingsService
    {
        public ProjectSettings Stored { get; set; } = new();

        public List<string> Calls { get; } = [];

        public ProjectSettings? LastSettings { get; private set; }

        public JsonObject? LastPatch { get; private set; }

        public ProjectSettingsCondition? LastCondition { get; private set; }

        public KeyValueCaller? LastCaller { get; private set; }

        public Task<ProjectSettings> GetAsync(string organization, string project, CancellationToken cancellationToken = default)
        {
            Calls.Add($"get:{organization}.{project}");
            return Task.FromResult(Stored);
        }

        public Task<ProjectSettingsWrite> SetAsync(string organization, string project, ProjectSettings settings, ProjectSettingsCondition condition, KeyValueCaller caller, CancellationToken cancellationToken = default)
        {
            Calls.Add($"set:{organization}.{project}");
            (LastSettings, LastCondition, LastCaller) = (settings, condition, caller);
            return Task.FromResult(Write(condition, ProjectSettingsRules.Normalize(settings with { Version = 0, Modified = null })));
        }

        public Task<ProjectSettingsWrite> PatchAsync(string organization, string project, JsonObject patch, ProjectSettingsCondition condition, KeyValueCaller caller, CancellationToken cancellationToken = default)
        {
            Calls.Add($"patch:{organization}.{project}");
            (LastPatch, LastCondition, LastCaller) = (patch, condition, caller);
            EnsureHolds(condition);
            return Task.FromResult(Write(condition, ProjectSettingsJson.Read(ProjectSettingsJson.ApplyMergePatch(ProjectSettingsJson.ToJson(Stored, ProjectSettingsJsonShape.Api), patch))));
        }

        private void EnsureHolds(ProjectSettingsCondition condition)
        {
            if (!condition.Holds(Stored.Version))
            {
                throw new ProjectSettingsPreconditionFailedException(Stored.Version);
            }
        }

        private ProjectSettingsWrite Write(ProjectSettingsCondition condition, ProjectSettingsCheck check)
        {
            EnsureHolds(condition);

            if (!check.IsValid)
            {
                throw new ProjectSettingsInvalidException(check.Problems);
            }

            if (ProjectSettingsRules.SameValues(Stored, check.Settings))
            {
                return new ProjectSettingsWrite(Stored, Changed: false);
            }

            Stored = check.Settings with { Version = Stored.Version + 1 };
            return new ProjectSettingsWrite(Stored, Changed: true);
        }
    }
}

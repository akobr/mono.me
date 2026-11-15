using System.Security.Claims;

using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using _42.Platform.Storyteller.Api.Exhibitions;
using _42.Platform.Storyteller.Api.Security;
using _42.Platform.Storyteller.Api.V1;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

using Exhibition = _42.Platform.Storyteller.Api.V1.Models.Exhibition;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// The exhibition routes: GET v1/access/exhibition and POST v1/access/exhibition/join. The names of the settings differ
// from every word of the messages, so a test can tell a setting's name from its value.
public class ExhibitionHttpTests
{
    private const string Organization = "fourth-wall";
    private const string Project = "toons-and-tyrants";
    private const string ProjectKey = "fourth-wall.toons-and-tyrants";
    private const string OwnerId = "owner-1";
    private const string UserId = TestRequests.UserId;
    private const string EntraAccountId = "8a7b6c5d-4e3f-4a1b-8c9d-0e1f2a3b4c5d:3f2c6a8e-6c1d-4a59-9a47-0a2f4f4c1b2d";

    // Every newcomer of a deployment without an exhibition asks: 404 without a body, no read and nothing logged.
    [Fact]
    public async Task BothRoutes_WithoutTheSettings_AreNotFoundWithoutCallingTheService()
    {
        var access = new RecordingAccess();
        var logger = new CapturingLogger<ExhibitionHttp>();
        var http = new ExhibitionHttp(access, logger);

        (await http.GetExhibition(TestRequests.User())).ShouldBeOfType<NotFoundResult>();
        (await http.JoinExhibition(TestRequests.User("POST"))).ShouldBeOfType<NotFoundResult>();

        access.Calls.ShouldBeEmpty();
        logger.Entries.ShouldBeEmpty();
    }

    // Settings that are live before the seed: the same 404, nothing written, and a Warning that names the settings.
    [Fact]
    public async Task BothRoutes_ProjectThatDoesNotExist_AreNotFoundAndWarn()
    {
        var access = new RecordingAccess();
        var logger = new CapturingLogger<ExhibitionHttp>();
        var http = Http(access, logger);

        (await http.GetExhibition(TestRequests.User())).ShouldBeOfType<NotFoundResult>();
        (await http.JoinExhibition(TestRequests.User("POST"))).ShouldBeOfType<NotFoundResult>();

        access.Calls.ShouldBe([$"point:{ProjectKey}", $"point:{ProjectKey}"]);
        ShouldWarnByTheNamesOfTheSettings(logger, count: 2, "Exhibition:Organization", "Exhibition:Project");
    }

    // A project of the configured names that another account owns is never offered or joined; a lower role of the
    // configured owner is not enough either.
    [Theory]
    [InlineData("somebody-else", AccountRole.Owner)]
    [InlineData(OwnerId, AccountRole.Administrator)]
    public async Task BothRoutes_ProjectThatTheConfiguredOwnerDoesNotOwn_AreNotFoundAndWarn(string memberId, AccountRole role)
    {
        var access = new RecordingAccess();
        access.AddProject(memberId, role);
        var logger = new CapturingLogger<ExhibitionHttp>();
        var http = Http(access, logger);

        (await http.GetExhibition(TestRequests.User())).ShouldBeOfType<NotFoundResult>();
        (await http.JoinExhibition(TestRequests.User("POST"))).ShouldBeOfType<NotFoundResult>();

        access.Calls.ShouldBe([$"point:{ProjectKey}", $"point:{ProjectKey}"]);
        access.Points[ProjectKey].AccessMap.Keys.ShouldBe([memberId]);
        ShouldWarnByTheNamesOfTheSettings(logger, count: 2, "Exhibition:Organization", "Exhibition:Project", "Exhibition:Owner");
        logger.Entries.ShouldAllBe(entry => entry.Message.Contains("is not the Owner", StringComparison.Ordinal));
    }

    // Owning is not creating: an Owner makes any existing account an Owner without its consent (POST v1/access/grant).
    // Whoever registered the names first could so add the configured owner to a project of their own, and would list
    // every joiner's name and email. A project that another account manages too is never offered or joined.
    [Theory]
    [InlineData(AccountRole.Owner)]
    [InlineData(AccountRole.Administrator)]
    public async Task BothRoutes_ProjectThatAnotherAccountManagesToo_AreNotFoundAndWarn(AccountRole role)
    {
        var access = new RecordingAccess();
        access.AddProject();
        access.AddAccount("somebody-else", role);
        var logger = new CapturingLogger<ExhibitionHttp>();
        var http = Http(access, logger);

        (await http.GetExhibition(TestRequests.User())).ShouldBeOfType<NotFoundResult>();
        (await http.JoinExhibition(TestRequests.User("POST"))).ShouldBeOfType<NotFoundResult>();

        access.Calls.ShouldBe([$"point:{ProjectKey}", $"point:{ProjectKey}"]);
        access.Created.ShouldBeNull();
        access.Accounts.Keys.ShouldBe(["somebody-else"]);
        access.Points[ProjectKey].AccessMap.ShouldBe(new Dictionary<string, AccountRole> { [OwnerId] = AccountRole.Owner, ["somebody-else"] = role }, ignoreOrder: true);
        ShouldWarnByTheNamesOfTheSettings(logger, count: 2, "Exhibition:Organization", "Exhibition:Project", "Exhibition:Owner");
        logger.Entries.ShouldAllBe(entry => entry.Message.Contains("is not the only Administrator or Owner", StringComparison.Ordinal));
    }

    // The visitors, and a member who writes the content, do not manage the project: it stays offered.
    [Theory]
    [InlineData(AccountRole.Reader)]
    [InlineData(AccountRole.Contributor)]
    [InlineData(AccountRole.ContributorWithSecrets)]
    public async Task GetExhibition_ProjectWithAnotherMemberBelowAdministrator_IsOffered(AccountRole role)
    {
        var access = new RecordingAccess();
        access.AddProject();
        access.AddAccount("somebody-else", role);
        var logger = new CapturingLogger<ExhibitionHttp>();
        var http = Http(access, logger);

        var result = await http.GetExhibition(TestRequests.User());

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBe(new Exhibition { Organization = Organization, Project = Project });
        logger.Entries.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("The demo of a fictional company.")]
    [InlineData(null)]
    public async Task GetExhibition_AnswersTheNamesAndTheDescriptionOfTheProject(string? description)
    {
        var access = new RecordingAccess();
        access.AddProject(description: description);
        var logger = new CapturingLogger<ExhibitionHttp>();
        var http = Http(access, logger);

        var result = await http.GetExhibition(TestRequests.User());

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBe(new Exhibition { Organization = Organization, Project = Project, Description = description });
        access.Calls.ShouldBe([$"point:{ProjectKey}"]);
        logger.Entries.ShouldBeEmpty();
    }

    // The account of a newcomer has no organization: it is created under the id a registration uses, with the names of
    // the token, and then joined. The role and the key are not the request's to choose.
    [Fact]
    public async Task JoinExhibition_UserWithoutAnAccount_CreatesABareAccountAndJoinsItAsReader()
    {
        var access = new RecordingAccess();
        access.AddProject();
        var http = Http(access);

        var result = await http.JoinExhibition(TestRequests.User("POST"));

        var account = result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<Account>();
        account.Id.ShouldBe(UserId);
        account.AccessMap.ShouldBe(new Dictionary<string, AccountRole> { [ProjectKey] = AccountRole.Reader });
        access.Created.ShouldBe(new AccountCreate { IdentityId = UserId, UserName = "ada@example.com", Name = "Ada" });
        access.Calls.ShouldBe(
        [
            $"point:{ProjectKey}",
            $"account:{UserId}",
            $"create:{UserId}",
            $"join:{ProjectKey}:{UserId}:Reader",
            $"account:{UserId}",
        ]);
        access.Points[ProjectKey].AccessMap.ShouldBe(new Dictionary<string, AccountRole> { [OwnerId] = AccountRole.Owner, [UserId] = AccountRole.Reader }, ignoreOrder: true);
    }

    // Under Entra ID the account is created under {tid}:{oid}, as PostAccount creates it.
    [Fact]
    public async Task JoinExhibition_EntraId_CreatesTheAccountUnderTheTenantAndObjectId()
    {
        var access = new RecordingAccess();
        access.AddProject();
        var http = Http(access, provider: IdentityProviderKind.EntraId);

        var result = await http.JoinExhibition(TestRequests.User("POST", userId: EntraAccountId));

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<Account>().Id.ShouldBe(EntraAccountId);
        access.Created.ShouldNotBeNull().IdentityId.ShouldBe(EntraAccountId);
    }

    // The join is idempotent and never lowers a role: the handler asks for Reader, and the service keeps what is higher.
    [Theory]
    [InlineData(AccountRole.None, AccountRole.Reader)]
    [InlineData(AccountRole.Reader, AccountRole.Reader)]
    [InlineData(AccountRole.Contributor, AccountRole.Contributor)]
    public async Task JoinExhibition_UserWithAnAccount_JoinsWithoutCreatingOne(AccountRole currentRole, AccountRole expectedRole)
    {
        var access = new RecordingAccess();
        access.AddProject();
        access.AddAccount(UserId, currentRole);
        var http = Http(access);

        var result = await http.JoinExhibition(TestRequests.User("POST"));

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<Account>().AccessMap[ProjectKey].ShouldBe(expectedRole);
        access.Created.ShouldBeNull();
        access.Calls.ShouldBe(
        [
            $"point:{ProjectKey}",
            $"account:{UserId}",
            $"join:{ProjectKey}:{UserId}:Reader",
            $"account:{UserId}",
        ]);
    }

    // An existing account needs no names: only a registration reads the profile.
    [Fact]
    public async Task JoinExhibition_UserWithAnAccountAndATokenWithoutNames_Joins()
    {
        var access = new RecordingAccess();
        access.AddProject();
        access.AddAccount(UserId);
        var http = Http(access);

        var result = await http.JoinExhibition(UserWithoutNames());

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<Account>().AccessMap[ProjectKey].ShouldBe(AccountRole.Reader);
        access.Created.ShouldBeNull();
    }

    // Through an actor (a Storyteller user token, the MCP server) the join works as for the person: an actor may grant
    // Reader, also under NoSecretReveal.
    [Fact]
    public async Task JoinExhibition_ThroughARestrictedActor_Joins()
    {
        var access = new RecordingAccess();
        access.AddProject();
        access.AddAccount(UserId);
        var http = Http(access);

        var result = await http.JoinExhibition(TestRequests.DelegatedUser(method: "POST"));

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<Account>().AccessMap[ProjectKey].ShouldBe(AccountRole.Reader);
    }

    // Two requests of one newcomer: the other one created the account between the read and the create. The service
    // answers AccountExists when its own read finds it, and whatever its write throws when both passed the read.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task JoinExhibition_CreateThatThrowsWhileTheAccountAppears_GoesOnWithTheJoin(bool isConflict)
    {
        Exception failure = isConflict
            ? new ConflictException($"The account 'ada@example.com#{UserId}' already exists.", ErrorCodes.AccountExists)
            : new InvalidOperationException("Response status code does not indicate success: Conflict (409).");
        var access = new RecordingAccess { CreateFailure = failure, AccountAppearsWhileCreating = true };
        access.AddProject();
        var http = Http(access);

        var result = await http.JoinExhibition(TestRequests.User("POST"));

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<Account>().AccessMap.ShouldBe(new Dictionary<string, AccountRole> { [ProjectKey] = AccountRole.Reader });
        access.Calls.ShouldBe(
        [
            $"point:{ProjectKey}",
            $"account:{UserId}",
            $"create:{UserId}",
            $"account:{UserId}",
            $"join:{ProjectKey}:{UserId}:Reader",
            $"account:{UserId}",
        ]);
    }

    // A create that left no account has failed: its exception goes out as it is, and nothing is joined.
    [Fact]
    public async Task JoinExhibition_CreateThatThrowsWhileTheAccountStaysMissing_ThrowsItsException()
    {
        var failure = new InvalidOperationException("The database is not reachable.");
        var access = new RecordingAccess { CreateFailure = failure };
        access.AddProject();
        var http = Http(access);

        var exception = await Should.ThrowAsync<InvalidOperationException>(() => http.JoinExhibition(TestRequests.User("POST")));

        exception.ShouldBeSameAs(failure);
        access.Calls.ShouldBe([$"point:{ProjectKey}", $"account:{UserId}", $"create:{UserId}", $"account:{UserId}"]);
        access.Points[ProjectKey].AccessMap.Keys.ShouldBe([OwnerId]);
    }

    // The store is not reachable: the read after the failed create fails too, and says nothing about the account. The
    // exception of the create goes out, not the one of that read, and nothing is joined.
    [Fact]
    public async Task JoinExhibition_CreateThatThrowsAndAReadAfterItThatThrowsToo_ThrowsTheExceptionOfTheCreate()
    {
        var failure = new InvalidOperationException("The database is not reachable.");
        var access = new RecordingAccess { CreateFailure = failure, ReadFailureAfterCreate = new TimeoutException("The read timed out.") };
        access.AddProject();
        var http = Http(access);

        var exception = await Should.ThrowAsync<InvalidOperationException>(() => http.JoinExhibition(TestRequests.User("POST")));

        exception.ShouldBeSameAs(failure);
        access.Calls.ShouldBe([$"point:{ProjectKey}", $"account:{UserId}", $"create:{UserId}", $"account:{UserId}"]);
        access.Points[ProjectKey].AccessMap.Keys.ShouldBe([OwnerId]);
    }

    // As PostAccount: a token without the names is 401 (the middleware answers the SecurityTokenException), and nothing
    // is created or joined; an account with empty names would be a blank row of the member list.
    [Fact]
    public async Task JoinExhibition_TokenWithoutNames_IsUnauthorizedAndCreatesNothing()
    {
        var access = new RecordingAccess();
        access.AddProject();
        var http = Http(access);

        var exception = await Should.ThrowAsync<SecurityTokenException>(() => http.JoinExhibition(UserWithoutNames()));

        exception.Message.ShouldBe("Missing preferred_username claim.");
        access.Created.ShouldBeNull();
        access.Calls.ShouldBe([$"point:{ProjectKey}", $"account:{UserId}"]);
        access.Accounts.ShouldBeEmpty();
    }

    // Every newcomer writes the same document. The Conflict of the service (it tried twice) is tried again, each time
    // after a delay of 50 to 250 ms; the tests replace the wait.
    [Fact]
    public async Task JoinExhibition_ConflictTwiceThenTheJoin_IsOkAfterThreeCalls()
    {
        var access = new RecordingAccess { JoinConflicts = 2 };
        access.AddProject();
        access.AddAccount(UserId);
        var delays = new List<TimeSpan>();
        var http = Http(access, delays: delays);

        var result = await http.JoinExhibition(TestRequests.User("POST"));

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<Account>().AccessMap[ProjectKey].ShouldBe(AccountRole.Reader);
        access.Calls.Count(call => call.StartsWith("join:", StringComparison.Ordinal)).ShouldBe(3);
        delays.Count.ShouldBe(2);
        delays.ShouldAllBe(delay => delay >= TimeSpan.FromMilliseconds(50) && delay <= TimeSpan.FromMilliseconds(250));
    }

    // Three more attempts, then the 409 goes out (the middleware answers the ConflictException).
    [Fact]
    public async Task JoinExhibition_ConflictFourTimes_IsConflictAfterFourCalls()
    {
        var access = new RecordingAccess { JoinConflicts = 4 };
        access.AddProject();
        access.AddAccount(UserId);
        var delays = new List<TimeSpan>();
        var http = Http(access, delays: delays);

        var exception = await Should.ThrowAsync<ConflictException>(() => http.JoinExhibition(TestRequests.User("POST")));

        exception.ErrorCode.ShouldBe(ErrorCodes.Conflict);
        access.Calls.Count(call => call.StartsWith("join:", StringComparison.Ordinal)).ShouldBe(4);
        delays.Count.ShouldBe(3);
        access.Points[ProjectKey].AccessMap.Keys.ShouldBe([OwnerId]);
    }

    // Only the Conflict of a concurrent change is worth another attempt; any other refusal goes out at once.
    [Fact]
    public async Task JoinExhibition_ConflictOfAnotherCode_IsNotTriedAgain()
    {
        var access = new RecordingAccess { JoinConflicts = 1, JoinConflictCode = ErrorCodes.LastOwner };
        access.AddProject();
        access.AddAccount(UserId);
        var delays = new List<TimeSpan>();
        var http = Http(access, delays: delays);

        var exception = await Should.ThrowAsync<ConflictException>(() => http.JoinExhibition(TestRequests.User("POST")));

        exception.ErrorCode.ShouldBe(ErrorCodes.LastOwner);
        access.Calls.Count(call => call.StartsWith("join:", StringComparison.Ordinal)).ShouldBe(1);
        delays.ShouldBeEmpty();
    }

    // The wait before another attempt gets the token of the invocation: an invocation that is cancelled while it waits
    // ends there, and nothing is tried or written after it.
    [Fact]
    public async Task JoinExhibition_InvocationCancelledWhileWaiting_IsCancelledWithoutAnotherAttempt()
    {
        var access = new RecordingAccess { JoinConflicts = 4 };
        access.AddProject();
        access.AddAccount(UserId);
        using var source = new CancellationTokenSource();
        var request = TestRequests.User("POST");
        ((TestFunctionContext)request.FunctionContext).Token = source.Token;
        var tokens = new List<CancellationToken>();
        var http = Http(access, delay: (_, token) =>
        {
            tokens.Add(token);
            source.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        });

        await Should.ThrowAsync<OperationCanceledException>(() => http.JoinExhibition(request));

        tokens.ShouldBe([source.Token]);
        access.Calls.Count(call => call.StartsWith("join:", StringComparison.Ordinal)).ShouldBe(1);
        access.Points[ProjectKey].AccessMap.Keys.ShouldBe([OwnerId]);
    }

    // The exhibition is for people: a machine is refused in every build, also one whose credentials carry every scope
    // and User.Impersonation, before the service is asked.
    [Fact]
    public async Task BothRoutes_Machine_IsAccessDeniedInEveryBuild()
    {
        var access = new RecordingAccess();
        access.AddProject();
        var http = Http(access);

        await UserOnlyRoutes.ShouldRefuseMachineAsync(() => http.GetExhibition(UserOnlyRoutes.Machine()));
        await UserOnlyRoutes.ShouldRefuseMachineAsync(() => http.JoinExhibition(UserOnlyRoutes.Machine("POST")));

        access.Calls.ShouldBeEmpty();
    }

    // As PostAccount: any other id under Entra ID means a token passed without the rewrite, a programming error, and no
    // account is read, created or joined with it. Without the options, the provider is Entra ID, the default of
    // Auth:Provider.
    [Theory]
    [InlineData("user-1", true)]
    [InlineData("user-1", false)]
    [InlineData("8A7B6C5D-4E3F-4A1B-8C9D-0E1F2A3B4C5D:3F2C6A8E-6C1D-4A59-9A47-0A2F4F4C1B2D", true)]
    [InlineData("8a7b6c5d-4e3f-4a1b-8c9d-0e1f2a3b4c5d", true)]
    public async Task JoinExhibition_EntraIdWithAnotherId_IsAProgrammingError(string userId, bool withOptions)
    {
        var access = new RecordingAccess();
        access.AddProject();
        var http = withOptions
            ? Http(access, provider: IdentityProviderKind.EntraId)
            : new ExhibitionHttp(access, new CapturingLogger<ExhibitionHttp>(), exhibitionOptions: ExhibitionSettings());

        await Should.ThrowAsync<InvalidOperationException>(() => http.JoinExhibition(TestRequests.User("POST", userId: userId)));

        access.Calls.ShouldBe([$"point:{ProjectKey}"]);
        access.Created.ShouldBeNull();
        access.Points[ProjectKey].AccessMap.Keys.ShouldBe([OwnerId]);
    }

    // The handler of a deployment with an exhibition, under the provider; AuthKit keys accounts by the sub as it is.
    // The wait before another attempt of the join is recorded and skipped, unless the test gives its own.
    private static ExhibitionHttp Http(
        RecordingAccess access,
        CapturingLogger<ExhibitionHttp>? logger = null,
        IdentityProviderKind provider = IdentityProviderKind.AuthKit,
        List<TimeSpan>? delays = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        return new ExhibitionHttp(
            access,
            logger ?? new CapturingLogger<ExhibitionHttp>(),
            exhibitionOptions: ExhibitionSettings(),
            authOptions: Options.Create(new UserAuthenticationOptions { Provider = provider }))
        {
            Delay = delay ?? ((wait, _) =>
            {
                delays?.Add(wait);
                return Task.CompletedTask;
            }),
        };
    }

    private static IOptions<ExhibitionOptions> ExhibitionSettings()
    {
        return Options.Create(new ExhibitionOptions { Organization = Organization, Project = Project, Owner = OwnerId });
    }

    // A user's token without the name claims, which only a registration needs.
    private static TestHttpRequestData UserWithoutNames()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var (context, request) = FunctionTestDoubles.CreateRequest(services, method: "POST");
        context.Items[FunctionContextItemKeys.CachedClaims] = new List<Claim>
        {
            new("sub", UserId),
            new("scp", Scopes.User.Impersonation),
        };
        return request;
    }

    // One Warning per request, by the names of the settings and without their values.
    private static void ShouldWarnByTheNamesOfTheSettings(CapturingLogger<ExhibitionHttp> logger, int count, params string[] settings)
    {
        var entries = logger.Entries.ToList();
        entries.Count.ShouldBe(count);

        foreach (var entry in entries)
        {
            entry.Level.ShouldBe(LogLevel.Warning);
            entry.Exception.ShouldBeNull();
            entry.Values.Select(value => value.Key).ShouldBe(["{OriginalFormat}"]);

            foreach (var setting in settings)
            {
                entry.Message.ShouldContain(setting, Case.Sensitive);
            }

            foreach (var value in new[] { Organization, Project, OwnerId, "somebody-else" })
            {
                entry.Message.ShouldNotContain(value, Case.Insensitive);
            }
        }
    }

    // The access service of the handler: the access points and accounts it holds, and every call in order. The join
    // follows the service: it needs both documents and never lowers a role.
    private sealed class RecordingAccess : IAccessService
    {
        public List<string> Calls { get; } = [];

        public Dictionary<string, AccessPoint> Points { get; } = new();

        public Dictionary<string, Account> Accounts { get; } = new();

        // The model the handler passed to CreateAccountAsync; null while nothing was created.
        public AccountCreate? Created { get; private set; }

        // What CreateAccountAsync throws, instead of creating the account.
        public Exception? CreateFailure { get; init; }

        // With CreateFailure: a concurrent request created the account, which is there after the failed create.
        public bool AccountAppearsWhileCreating { get; init; }

        // What GetAccountAsync throws once the create of that account was tried: the store fails that read too.
        public Exception? ReadFailureAfterCreate { get; init; }

        // The joins that answer a conflict before one succeeds, and the error code of that conflict.
        public int JoinConflicts { get; set; }

        public string JoinConflictCode { get; init; } = ErrorCodes.Conflict;

        // The project of the settings, owned by the configured owner unless another member is given.
        public void AddProject(string memberId = OwnerId, AccountRole role = AccountRole.Owner, string? description = null)
        {
            Points[ProjectKey] = new AccessPoint
            {
                Key = ProjectKey,
                AccessMap = new Dictionary<string, AccountRole> { [memberId] = role },
                Description = description,
            };
        }

        // An account, with a role in the project of the settings unless the role is None.
        public void AddAccount(string id, AccountRole role = AccountRole.None)
        {
            var accessMap = new Dictionary<string, AccountRole>();

            if (role != AccountRole.None)
            {
                accessMap[ProjectKey] = role;
                Points[ProjectKey] = Points[ProjectKey] with
                {
                    AccessMap = new Dictionary<string, AccountRole>(Points[ProjectKey].AccessMap) { [id] = role },
                };
            }

            Accounts[id] = new Account { Id = id, UserName = $"{id}@example.com", Name = id, AccessMap = accessMap };
        }

        public Task<AccessPoint?> GetAccessPointAsync(string key)
        {
            Calls.Add($"point:{key}");
            return Task.FromResult(Points.GetValueOrDefault(key));
        }

        public Task<Account?> GetAccountAsync(string id)
        {
            Calls.Add($"account:{id}");

            if (ReadFailureAfterCreate is not null && Calls.Contains($"create:{id}"))
            {
                throw ReadFailureAfterCreate;
            }

            return Task.FromResult(Accounts.GetValueOrDefault(id));
        }

        public Task<Account> CreateAccountAsync(AccountCreate model)
        {
            Calls.Add($"create:{model.IdentityId}");
            var account = new Account
            {
                Id = model.IdentityId,
                UserName = model.UserName,
                Name = model.Name,
                AccessMap = new Dictionary<string, AccountRole>(),
            };

            if (CreateFailure is not null)
            {
                if (AccountAppearsWhileCreating)
                {
                    Accounts[model.IdentityId] = account;
                }

                throw CreateFailure;
            }

            Created = model;
            Accounts[model.IdentityId] = account;
            return Task.FromResult(account);
        }

        public Task<AccountRole> JoinAccessPointAsync(string accessPointKey, string accountId, AccountRole role)
        {
            Calls.Add($"join:{accessPointKey}:{accountId}:{role}");

            if (JoinConflicts > 0)
            {
                JoinConflicts--;
                throw new ConflictException($"The membership of '{accountId}' in '{accessPointKey}' was changed concurrently, try again.", JoinConflictCode);
            }

            if (!Points.TryGetValue(accessPointKey, out var point))
            {
                throw new NotFoundException($"The access point '{accessPointKey}' doesn't exist.");
            }

            if (!Accounts.TryGetValue(accountId, out var account))
            {
                throw new NotFoundException($"The target account '{accountId}' doesn't exist.");
            }

            if (point.AccessMap.TryGetValue(accountId, out var currentRole) && currentRole >= role)
            {
                return Task.FromResult(currentRole);
            }

            Points[accessPointKey] = point with { AccessMap = new Dictionary<string, AccountRole>(point.AccessMap) { [accountId] = role } };
            Accounts[accountId] = account with { AccessMap = new Dictionary<string, AccountRole>(account.AccessMap) { [accessPointKey] = role } };
            return Task.FromResult(role);
        }

        public Task<AccountRole> GetAccountRoleAsync(string accountId, string accessPointKey) => throw new NotImplementedException();

        public Task<IEnumerable<AccessPoint>> GetAccessPointsAsync(string accountId) => throw new NotImplementedException();

        public Task<AccessPoint> CreateAccessPointAsync(AccessPointCreate model) => throw new NotImplementedException();

        public Task<bool> GrantPermissionAsync(Permission model) => throw new NotImplementedException();

        public Task<bool> RevokePermissionAsync(Permission model) => throw new NotImplementedException();

        public Task<IReadOnlyList<AccessPointMember>> GetMembersAsync(string accessPointKey, string actorId) => throw new NotImplementedException();

        public Task<AccessPointMember> SetMemberRoleAsync(string accessPointKey, string accountId, AccountRole role, string actorId) => throw new NotImplementedException();

        public Task RemoveMemberAsync(string accessPointKey, string accountId, string actorId) => throw new NotImplementedException();

        public Task<IEnumerable<MachineAccess>> GetMachineAccessesAsync(string organization, string project) => throw new NotImplementedException();

        public Task<MachineAccess?> GetMachineAccessAsync(string organization, string project, string id) => throw new NotImplementedException();

        public Task<MachineAccess> CreateMachineAccessAsync(MachineAccessCreate model) => throw new NotImplementedException();

        public Task<MachineAccess> ResetMachineAccessAsync(string organization, string project, string appId) => throw new NotImplementedException();

        public Task<bool> DeleteMachineAccessAsync(string organization, string project, string appId) => throw new NotImplementedException();

        public Task<bool> VerifyAccessForMachineAsync(string organization, string project, string appId) => throw new NotImplementedException();
    }
}

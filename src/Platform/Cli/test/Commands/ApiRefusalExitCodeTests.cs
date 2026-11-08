using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Commands;
using _42.Platform.Cli.Commands.AccessPoints;
using _42.Platform.Cli.Commands.Configuration;
using _42.Platform.Cli.Commands.KeyValues;
using _42.Platform.Cli.Commands.MachineAccess;
using _42.Platform.Cli.Commands.Storyteller;
using _42.Platform.Cli.Commands.Templates;
using _42.Platform.Cli.Configuration;
using _42.Platform.Cli.Output;
using _42.Platform.Cli.Output.Exceptions;
using _42.Platform.Cli.Services;
using _42.Platform.Cli.UnitTests.Commands.KeyValues;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Newtonsoft.Json.Linq;
using Shouldly;
using Xunit;

using static System.IO.Abstractions.TestingHelpers.MockUnixSupport;

namespace _42.Platform.Cli.UnitTests.Commands;

// A refusal or a failure of the API is never a crash of the tool (exit code 1). The server's message and error code are
// printed, and the exit code follows the status: 401 and 403 are 44, a rejected request (400, 404, 409, 412, 422, …) and
// a 501 or 505, which no retry changes, are 2, and 408, 429 and the other 5xx are a failure of the service, 3. D1 to D6
// are the defects the CLI test case catalogue found (docs/Platform/Cli/specs/reviews/2026-11-08 sform Defects Found by
// the CLI Test Cases.md).
public class ApiRefusalExitCodeTests
{
    private const string Key = "rst.billing";
    private const string Forbidden = "No Contributor access to 'org1.proj1'.";

    private readonly AnnotationCommandFixture _fixture = new();
    private readonly Mock<IConfigurationsApiClient> _configurations = new(MockBehavior.Strict);
    private readonly Mock<ITemplatesApiClient> _templates = new(MockBehavior.Strict);
    private readonly Mock<IAccessApiClient> _access = new(MockBehavior.Strict);
    private readonly Mock<IKeyValuesApiClient> _keyValues = new(MockBehavior.Strict);
    private readonly Mock<IEditorService> _editor = new(MockBehavior.Strict);
    private readonly MockFileSystem _fileSystem = new();

    private IReadOnlyList<string> Lines => _fixture.Console.Lines;

    [Theory]
    [InlineData(400, ExitCodes.ERROR_WRONG_INPUT)]
    [InlineData(404, ExitCodes.ERROR_WRONG_INPUT)]
    [InlineData(409, ExitCodes.ERROR_WRONG_INPUT)]
    [InlineData(412, ExitCodes.ERROR_WRONG_INPUT)]
    [InlineData(413, ExitCodes.ERROR_WRONG_INPUT)]
    [InlineData(422, ExitCodes.ERROR_WRONG_INPUT)]
    [InlineData(401, ExitCodes.WARNING_UNAUTHORIZED_ACCESS)]
    [InlineData(403, ExitCodes.WARNING_UNAUTHORIZED_ACCESS)]
    [InlineData(408, ExitCodes.ERROR_SERVICE_FAILURE)]
    [InlineData(429, ExitCodes.ERROR_SERVICE_FAILURE)]
    [InlineData(500, ExitCodes.ERROR_SERVICE_FAILURE)]
    [InlineData(502, ExitCodes.ERROR_SERVICE_FAILURE)]
    [InlineData(503, ExitCodes.ERROR_SERVICE_FAILURE)]
    [InlineData(504, ExitCodes.ERROR_SERVICE_FAILURE)]
    [InlineData(501, ExitCodes.ERROR_WRONG_INPUT)]
    [InlineData(505, ExitCodes.ERROR_WRONG_INPUT)]
    [InlineData(200, ExitCodes.ERROR_CRASH)]
    [InlineData(304, ExitCodes.ERROR_CRASH)]
    public void ExitCodeOf_FollowsTheStatus(int status, int expected)
    {
        KeyValueConsoleExtensions.ExitCodeOf((HttpStatusCode)status).ShouldBe(expected);
    }

    // What no command catches reaches Program.Main, which prints the server's message instead of "Total mayhem".
    [Theory]
    [InlineData(400, ExitCodes.ERROR_WRONG_INPUT)]
    [InlineData(409, ExitCodes.ERROR_WRONG_INPUT)]
    [InlineData(501, ExitCodes.ERROR_WRONG_INPUT)]
    [InlineData(503, ExitCodes.ERROR_SERVICE_FAILURE)]
    public async Task RunAsync_OfAnApiErrorNoCommandCaught_EndsWithTheCodeOfItsStatus(int status, int expected)
    {
        var exitCode = await Program.RunAsync(
            () => throw KeyValueTestContext.Error((HttpStatusCode)status, "The server refused it.", "SomeCode"),
            []);

        exitCode.ShouldBe(expected);
    }

    // D1 (CFG-SET-12): a Reader, or a machine without Configuration.Write.
    [Fact]
    public async Task ConfigSet_Forbidden_IsUnauthorizedWithTheServerMessage()
    {
        _configurations
            .Setup(api => api.SetConfigurationAsync("org1", "proj1", "default", Key, false, It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(AnnotationCommandFixture.Error(403, "AccessDenied", Forbidden));

        var exitCode = await ConfigSet().OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.WARNING_UNAUTHORIZED_ACCESS);
        Lines.ShouldBe([$"! {Forbidden}", "Error code: AccessDenied"]);
    }

    [Theory]
    [InlineData(409, "Conflict", ExitCodes.ERROR_WRONG_INPUT)]
    [InlineData(412, "PreconditionFailed", ExitCodes.ERROR_WRONG_INPUT)]
    [InlineData(500, "Unexpected", ExitCodes.ERROR_SERVICE_FAILURE)]
    public async Task ConfigSet_OtherApiError_PrintsTheServerMessageWithTheCodeOfItsStatus(int status, string errorCode, int expected)
    {
        _configurations
            .Setup(api => api.SetConfigurationAsync("org1", "proj1", "default", Key, false, It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(AnnotationCommandFixture.Error(status, errorCode, "The server refused it."));

        var exitCode = await ConfigSet().OnExecuteAsync();

        exitCode.ShouldBe(expected);
        Lines.ShouldBe(["! The server refused it.", $"Error code: {errorCode}"]);
        Lines.ShouldNotContain(line => line.StartsWith("Error occurred", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ConfigSet_SchemaViolation_ListsTheConfigurations()
    {
        _configurations
            .Setup(api => api.SetConfigurationAsync("org1", "proj1", "default", Key, false, It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiException<SchemaValidationErrorResponse>(
                "The configuration violates the schema.",
                409,
                string.Empty,
                new Dictionary<string, IEnumerable<string>>(),
                new SchemaValidationErrorResponse
                {
                    Message = "The configuration does not comply with the schema.",
                    Errors = [new SchemaValidationErrorDetail { AnnotationKey = Key, ViewName = "default", Errors = ["#/a: NumberExpected"] }],
                },
                null));

        var exitCode = await ConfigSet().OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        Lines.ShouldBe(["! The configuration does not comply with the schema.", $"{Key} (default)", "  #/a: NumberExpected"]);
    }

    // D6 (CFG-SET-05): the API stores no custom properties or labels of a configuration, so config set has no such options.
    [Theory]
    [InlineData("-c", "owner=t")]
    [InlineData("--custom-properties", "owner=t")]
    [InlineData("-l", "x")]
    [InlineData("--labels", "x")]
    public void ConfigSet_CustomPropertiesAndLabels_AreNotOptions(string option, string value)
    {
        var services = new ServiceCollection()
            .AddSingleton(_fixture.Console.Object)
            .AddSingleton(_fixture.Context.Object)
            .AddSingleton(_configurations.Object)
            .AddSingleton<IFileSystem>(_fileSystem)
            .BuildServiceProvider();
        var app = new CommandLineApplication<ConfigSetCommand>(NullConsole.Singleton);
        app.Conventions.UseDefaultConventions().UseConstructorInjection(services);

        Should.Throw<UnrecognizedCommandParsingException>(() => app.Parse(Key, "-x", "a=1", option, value));
    }

    // D3 (CFG-SET-10): a failing test operation changes nothing; it is a rejected request, not a crash.
    [Fact]
    public async Task ConfigPatch_FailingTestOperation_IsAWrongInputWithTheServerMessage()
    {
        PatchOfAStoredConfiguration()
            .ThrowsAsync(AnnotationCommandFixture.Error(412, "PatchTestFailed", "The test operation at '/a' failed: the value is 1, not 2."));

        var exitCode = await ConfigPatch().OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        Lines.ShouldContain("! The test operation at '/a' failed: the value is 1, not 2.");
        Lines.ShouldContain("Error code: PatchTestFailed");
        Lines.ShouldNotContain(line => line.StartsWith("Error occurred", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(409, "AnnotationDeleted", ExitCodes.ERROR_WRONG_INPUT)]
    [InlineData(403, "AccessDenied", ExitCodes.WARNING_UNAUTHORIZED_ACCESS)]
    [InlineData(503, "Unavailable", ExitCodes.ERROR_SERVICE_FAILURE)]
    public async Task ConfigPatch_OtherApiError_PrintsTheServerMessageWithTheCodeOfItsStatus(int status, string errorCode, int expected)
    {
        // the client reads a 409 of a write as a SchemaValidationErrorResponse, without entries for a deleted annotation
        ApiException error = status == 409
            ? new ApiException<SchemaValidationErrorResponse>(
                "The configuration violates the schema.",
                409,
                string.Empty,
                new Dictionary<string, IEnumerable<string>>(),
                new SchemaValidationErrorResponse { Message = "The server refused it.", ErrorCode = errorCode },
                null)
            : AnnotationCommandFixture.Error(status, errorCode, "The server refused it.");
        PatchOfAStoredConfiguration().ThrowsAsync(error);

        var exitCode = await ConfigPatch().OnExecuteAsync();

        exitCode.ShouldBe(expected);
        Lines.ShouldContain("! The server refused it.");
        Lines.ShouldContain($"Error code: {errorCode}");
    }

    [Fact]
    public async Task ConfigEdit_Forbidden_IsUnauthorizedWithTheServerMessage()
    {
        ConfigEditPatch().ThrowsAsync(AnnotationCommandFixture.Error(403, "AccessDenied", Forbidden));

        var exitCode = await ConfigEdit().OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.WARNING_UNAUTHORIZED_ACCESS);
        Lines.ShouldBe([$"! {Forbidden}", "Error code: AccessDenied"]);
    }

    [Theory]
    [InlineData(409, "Conflict", ExitCodes.ERROR_WRONG_INPUT)]
    [InlineData(412, "PatchTestFailed", ExitCodes.ERROR_WRONG_INPUT)]
    [InlineData(500, "Unexpected", ExitCodes.ERROR_SERVICE_FAILURE)]
    public async Task ConfigEdit_OtherApiError_PrintsTheServerMessageWithTheCodeOfItsStatus(int status, string errorCode, int expected)
    {
        ConfigEditPatch().ThrowsAsync(AnnotationCommandFixture.Error(status, errorCode, "The server refused it."));

        var exitCode = await ConfigEdit().OnExecuteAsync();

        exitCode.ShouldBe(expected);
        Lines.ShouldBe(["! The server refused it.", $"Error code: {errorCode}"]);
    }

    [Fact]
    public async Task ConfigEdit_SchemaViolation_ListsTheConfigurations()
    {
        ConfigEditPatch().ThrowsAsync(new ApiException<SchemaValidationErrorResponse>(
            "The configuration violates the schema.",
            409,
            string.Empty,
            new Dictionary<string, IEnumerable<string>>(),
            new SchemaValidationErrorResponse
            {
                Message = "The configuration does not comply with the schema.",
                Errors = [new SchemaValidationErrorDetail { AnnotationKey = Key, ViewName = "default", Errors = ["#/a: NumberExpected"] }],
            },
            null));

        var exitCode = await ConfigEdit().OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        Lines.ShouldBe(["! The configuration does not comply with the schema.", $"{Key} (default)", "  #/a: NumberExpected"]);
    }

    // A 403 without a message of the server prints the general sentence of a refusal, not the bare status.
    [Fact]
    public async Task ConfigSet_ForbiddenWithoutAMessage_PrintsTheGeneralRefusal()
    {
        _configurations
            .Setup(api => api.SetConfigurationAsync("org1", "proj1", "default", Key, false, It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(AnnotationCommandFixture.Error(403));

        var exitCode = await ConfigSet().OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.WARNING_UNAUTHORIZED_ACCESS);
        Lines.ShouldBe([$"! {ApiRefusal.GeneralMessage}"]);
    }

    [Theory]
    [InlineData(400, "InvalidSchema", ExitCodes.ERROR_WRONG_INPUT)]
    [InlineData(409, "Conflict", ExitCodes.ERROR_WRONG_INPUT)]
    [InlineData(412, "PatchTestFailed", ExitCodes.ERROR_WRONG_INPUT)]
    [InlineData(500, "Unexpected", ExitCodes.ERROR_SERVICE_FAILURE)]
    public async Task TemplateEdit_ApiError_PrintsTheServerMessageWithTheCodeOfItsStatus(int status, string errorCode, int expected)
    {
        _templates
            .Setup(api => api.GetTemplateAsync("org1", "proj1", "default", "unt"))
            .ReturnsAsync(new ConfigurationTemplate { Content = new JObject { ["owner"] = "billing" } });
        _templates
            .Setup(api => api.PatchTemplateAsync("org1", "proj1", "default", "unt", It.IsAny<object>()))
            .ThrowsAsync(AnnotationCommandFixture.Error(status, errorCode, "The server refused it."));
        EditedTo(new JObject { ["owner"] = "invoicing" });

        var exitCode = await new TemplateEditCommand(_fixture.Console.Object, _fixture.Context.Object, _templates.Object, _editor.Object, Options.Create(new EditorOptions()))
        {
            AnnotationType = "unt",
            Yes = true,
        }.OnExecuteAsync();

        exitCode.ShouldBe(expected);
        Lines.ShouldBe(["! The server refused it.", $"Error code: {errorCode}"]);
    }

    [Theory]
    [InlineData(400, ExitCodes.ERROR_WRONG_INPUT)]
    [InlineData(500, ExitCodes.ERROR_SERVICE_FAILURE)]
    public async Task TemplateSet_ApiError_PrintsTheServerMessageWithTheCodeOfItsStatus(int status, int expected)
    {
        _templates
            .Setup(api => api.SetTemplateAsync("org1", "proj1", "default", "unt", It.IsAny<object>()))
            .ThrowsAsync(AnnotationCommandFixture.Error(status, "SomeCode", "The server refused it."));

        var exitCode = await new TemplateSetCommand(_fixture.Console.Object, _fixture.Context.Object, _templates.Object, _fileSystem)
        {
            AnnotationType = "unt",
            InlineProperties = ["a=1"],
        }.OnExecuteAsync();

        exitCode.ShouldBe(expected);
        Lines.ShouldBe(["! The server refused it.", "Error code: SomeCode"]);
    }

    // D4 (ANN-SET-07): a key the API cannot parse.
    [Fact]
    public async Task StoryGet_KeyTheApiCannotParse_IsAWrongInputWithTheServerMessage()
    {
        _fixture.Api
            .Setup(api => api.GetAnnotationAsync("org1", "proj1", "default", "not-a-key", true, null))
            .ThrowsAsync(AnnotationCommandFixture.Error(400, "InvalidKey", "Invalid annotation key: not-a-key"));

        var exitCode = await new StorytellerGetCommand(_fixture.Console.Object, _fixture.Context.Object, _fixture.Api.Object, Mock.Of<IFileSystem>())
        {
            AnnotationKey = "not-a-key",
        }.OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        Lines.ShouldBe(["! Invalid annotation key: not-a-key", "Error code: InvalidKey"]);
    }

    // story get not-a-key: a key without a dot matches no route of the read, so the host answers 404 without a body, and
    // the lookup of the deleted annotation refuses the key with 400 InvalidKey.
    [Fact]
    public async Task StoryGet_KeyWithoutADot_IsAWrongInputWithTheMessageOfTheLookup()
    {
        _fixture.Api
            .Setup(api => api.GetAnnotationAsync("org1", "proj1", "default", "not-a-key", true, null))
            .ThrowsAsync(AnnotationCommandFixture.Error(404));
        _fixture.Api
            .Setup(api => api.GetDeletedAnnotationAsync("org1", "proj1", "default", "not-a-key"))
            .ThrowsAsync(AnnotationCommandFixture.Error(400, "InvalidKey", "Invalid annotation key: not-a-key"));

        var exitCode = await new StorytellerGetCommand(_fixture.Console.Object, _fixture.Context.Object, _fixture.Api.Object, Mock.Of<IFileSystem>())
        {
            AnnotationKey = "not-a-key",
        }.OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        Lines.ShouldBe(["! Invalid annotation key: not-a-key", "Error code: InvalidKey"]);
    }

    [Fact]
    public async Task StoryGet_Forbidden_IsLeftToProgramMain()
    {
        _fixture.Api
            .Setup(api => api.GetAnnotationAsync("org1", "proj1", "default", Key, true, null))
            .ThrowsAsync(AnnotationCommandFixture.Error(403, "AccessDenied", Forbidden));

        var exception = await Should.ThrowAsync<ApiException>(new StorytellerGetCommand(_fixture.Console.Object, _fixture.Context.Object, _fixture.Api.Object, Mock.Of<IFileSystem>())
        {
            AnnotationKey = Key,
        }.OnExecuteAsync());

        exception.StatusCode.ShouldBe(403);
    }

    // A revoke of a role lower than the member holds: the API answers 409, which the client does not declare.
    [Fact]
    public async Task PointsRevoke_Conflict_IsAWrongInputWithTheServerMessage()
    {
        _access.Setup(api => api.GetAccountAsync()).ReturnsAsync(new _42.Platform.Storyteller.Sdk.Account { Id = "acc-1" });
        _access
            .Setup(api => api.RevokeUserAccessAsync(It.IsAny<Permission>()))
            .ThrowsAsync(KeyValueTestContext.Error(HttpStatusCode.Conflict, "The account acc-2 holds Contributor on org1.proj1; revoke that role.", "Conflict"));

        var exitCode = await new AccessPointRevokeCommand(_fixture.Console.Object, _access.Object, Options.Create(new AccessDefaultOptions()))
        {
            AccountId = "acc-2",
            ProjectKey = "org1.proj1",
            Role = "Reader",
        }.OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        Lines.ShouldBe(["! The account acc-2 holds Contributor on org1.proj1; revoke that role.", "Error code: Conflict"]);
    }

    [Fact]
    public async Task PointsGrant_BadRequest_IsAWrongInputWithTheServerMessage()
    {
        _access.Setup(api => api.GetAccountAsync()).ReturnsAsync(new _42.Platform.Storyteller.Sdk.Account { Id = "acc-1" });
        _access
            .Setup(api => api.GrantUserAccessAsync(It.IsAny<Permission>()))
            .ThrowsAsync(KeyValueTestContext.Error(HttpStatusCode.BadRequest, "The account acc-2 does not exist.", "InvalidAccount"));

        var exitCode = await new AccessPointGrantCommand(_fixture.Console.Object, _access.Object, Options.Create(new AccessDefaultOptions()))
        {
            AccountId = "acc-2",
            ProjectKey = "org1.proj1",
        }.OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        Lines.ShouldContain("! The account acc-2 does not exist.");
    }

    // A 401 is not an account without a registration (that is the 404): Program.Main prints the refusal, exit code 44.
    [Fact]
    public async Task PointsList_Unauthorized_IsLeftToProgramMain()
    {
        _access.Setup(api => api.GetAccountAsync()).ThrowsAsync(AnnotationCommandFixture.Error(401));

        var exception = await Should.ThrowAsync<ApiException>(new AccessPointListCommand(_fixture.Console.Object, _access.Object).OnExecuteAsync());

        exception.StatusCode.ShouldBe(401);
        Lines.ShouldBeEmpty();
    }

    [Fact]
    public async Task PointsList_NotRegistered_AsksForTheRegistration()
    {
        _access.Setup(api => api.GetAccountAsync()).ThrowsAsync(AnnotationCommandFixture.Error(404));

        var exitCode = await new AccessPointListCommand(_fixture.Console.Object, _access.Object).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.WARNING_INTERACTION_NEEDED);
    }

    // A deployment without a customer-managed key provider: 501, which no retry changes, so not a failure of the service.
    [Fact]
    public async Task ByokSetup_NotConfigured_IsAWrongInputWithTheServerMessage()
    {
        _keyValues
            .Setup(api => api.GetByokSetupAsync("org1", "proj1"))
            .ThrowsAsync(KeyValueTestContext.Error(HttpStatusCode.NotImplemented, "Customer-managed keys are not configured in this deployment.", "ProtectionNotConfigured"));

        var exitCode = await new KeyValueByokSetupCommand(_fixture.Console.Object, _fixture.Context.Object, _keyValues.Object).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        Lines.ShouldBe(["! Customer-managed keys are not configured in this deployment.", "Error code: ProtectionNotConfigured"]);
    }

    [Fact]
    public async Task ByokVerify_NotConfigured_IsAWrongInputWithTheServerMessage()
    {
        _keyValues
            .Setup(api => api.VerifyByokAsync("org1", "proj1"))
            .ThrowsAsync(KeyValueTestContext.Error(HttpStatusCode.NotImplemented, "Customer-managed keys are not configured in this deployment.", "ProtectionNotConfigured"));

        var exitCode = await new KeyValueByokVerifyCommand(_fixture.Console.Object, _fixture.Context.Object, _keyValues.Object).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        Lines.ShouldContain("! Customer-managed keys are not configured in this deployment.");
    }

    // D5 (MCH-CRD-05): the documented result carries the certificate as Pkcs12 and Password.
    [Fact]
    public async Task MachineRenew_Renewed_WritesTheCertificateAndPrintsItsPassword()
    {
        Renewal().ReturnsAsync(new CertificateRenewalResult
        {
            Outcome = CertificateRenewalResultOutcome.Success,
            Pkcs12 = [1, 2, 3],
            Password = "pa55",
            Thumbprint = "RENEWED",
            LastRenewalAt = DateTimeOffset.UnixEpoch,
        });

        var exitCode = await MachineRenew(output: Path(@"C:\work\renewed.pfx")).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _fileSystem.File.ReadAllBytes(Path(@"C:\work\renewed.pfx")).ShouldBe(new byte[] { 1, 2, 3 });
        Lines.ShouldBe(
        [
            $"! Renewed certificate written to {Path(@"C:\work\renewed.pfx")}",
            "! Certificate password: pa55",
            "! Make sure to copy the password, it is not stored anywhere.",
        ]);
    }

    // An API deployed before the fix answers Certificate (base64) and CertificatePassword, which the client keeps aside.
    [Fact]
    public async Task MachineRenew_RenewedByAnOlderApi_WritesTheCertificateToo()
    {
        var result = Newtonsoft.Json.JsonConvert.DeserializeObject<CertificateRenewalResult>(
            $$"""{"Thumbprint":"RENEWED","Certificate":"{{Convert.ToBase64String([4, 5, 6])}}","CertificatePassword":"old-api","LastRenewalAt":"2026-11-08T10:00:00+00:00"}""")!;
        Renewal().ReturnsAsync(result);

        var exitCode = await MachineRenew(output: Path(@"C:\work\renewed.pfx")).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _fileSystem.File.ReadAllBytes(Path(@"C:\work\renewed.pfx")).ShouldBe(new byte[] { 4, 5, 6 });
        Lines.ShouldContain("! Certificate password: old-api");
    }

    // The documented JSON of the API, read by the generated client.
    [Fact]
    public void RenewalResult_OfTheApi_IsReadWithItsCertificate()
    {
        var result = Newtonsoft.Json.JsonConvert.DeserializeObject<CertificateRenewalResult>(
            $$"""{"Outcome":"Success","Pkcs12":"{{Convert.ToBase64String([1, 2, 3])}}","Password":"pa55","Thumbprint":"RENEWED","LastRenewalAt":"2026-11-08T10:00:00+00:00"}""")!;

        result.Outcome.ShouldBe(CertificateRenewalResultOutcome.Success);
        result.Pkcs12.ShouldBe(new byte[] { 1, 2, 3 });
        result.Password.ShouldBe("pa55");
        result.AdditionalProperties.ShouldBeEmpty();
    }

    [Fact]
    public async Task MachineRenew_AlreadyRenewed_PrintsTheMessageAndWritesNothing()
    {
        Renewal().ReturnsAsync(new CertificateRenewalResult
        {
            Outcome = CertificateRenewalResultOutcome.AlreadyRenewed,
            Message = "The certificate for this machine access was already renewed.",
            LastRenewalAt = new DateTimeOffset(2026, 11, 8, 10, 0, 0, TimeSpan.Zero),
        });

        var exitCode = await MachineRenew(output: Path(@"C:\work\r2.pfx")).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _fileSystem.File.Exists(Path(@"C:\work\r2.pfx")).ShouldBeFalse();
        Lines.ShouldBe(["! The certificate for this machine access was already renewed.", "Last renewed at: 2026-11-08T10:00:00.0000000+00:00"]);
    }

    // The window opens 30 days before the expiry; the client declares the 400 and the 404 without a body, so it keeps the
    // text, which the command reads.
    [Theory]
    [InlineData(400, "Renewal is not yet permitted. The renewal window opens at 2026-12-01T00:00:00.0000000+00:00.", "NotInRenewalWindow")]
    [InlineData(404, "The presenting certificate does not match the current certificate for this machine access.", "NotFound")]
    public async Task MachineRenew_Refused_IsAWrongInputWithTheServerMessageAndCode(int status, string message, string errorCode)
    {
        Renewal().ThrowsAsync(new ApiException(
            "Not in renewal window.",
            status,
            $$"""{"Message":"{{message}}","ErrorCode":"{{errorCode}}"}""",
            new Dictionary<string, IEnumerable<string>>(),
            null));

        var exitCode = await MachineRenew(output: null).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        Lines.ShouldBe([$"! {message}", $"Error code: {errorCode}"]);
    }

    // A refusal the command prints itself, without a message of the server, reads like the one Program.Main prints.
    [Fact]
    public async Task MachineRenew_ForbiddenWithoutAMessage_PrintsTheGeneralRefusal()
    {
        Renewal().ThrowsAsync(AnnotationCommandFixture.Error(403));

        var exitCode = await MachineRenew(output: null).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.WARNING_UNAUTHORIZED_ACCESS);
        Lines.ShouldBe([$"! {ApiRefusal.GeneralMessage}"]);
    }

    // A deployment without the renewal service.
    [Fact]
    public async Task MachineRenew_NotImplemented_IsAWrongInput()
    {
        Renewal().ThrowsAsync(AnnotationCommandFixture.Error(501));

        var exitCode = await MachineRenew(output: null).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        Lines.ShouldBe(["! The request failed with 501 NotImplemented."]);
    }

    [Fact]
    public async Task MachineRenew_Unauthorized_IsLeftToProgramMain()
    {
        Renewal().ThrowsAsync(AnnotationCommandFixture.Error(401));

        var exception = await Should.ThrowAsync<ApiException>(MachineRenew(output: null).OnExecuteAsync());

        exception.StatusCode.ShouldBe(401);
    }

    // The catalogue's crash paths of a file that is not a JSON object.
    [Theory]
    [InlineData("not json")]
    [InlineData("[1, 2]")]
    public async Task JsonInput_OfAFileThatIsNoJsonObject_IsAWrongInput(string content)
    {
        var file = Path(@"C:\work\input.json");
        _fileSystem.AddFile(file, new MockFileData(content));

        var exception = await Should.ThrowAsync<WrongInputException>(JsonInputBuilder.BuildAsync(_fixture.Console.Object, _fileSystem, file, null));

        exception.ExitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        Lines.ShouldHaveSingleItem().ShouldStartWith($"! The file '{file}' is not a JSON object: ");
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1, 2]")]
    public async Task StorySet_OfAnImportFileThatIsNoJsonObject_IsAWrongInput(string content)
    {
        var file = Path(@"C:\work\annotation.json");
        _fileSystem.AddFile(file, new MockFileData(content));
        _fixture.Console.UseInnerConsole(redirectedInput: false);

        var exitCode = await new StorytellerSetCommand(
            _fixture.Console.Object,
            _fixture.Context.Object,
            _fixture.Api.Object,
            Options.Create(new AccessDefaultOptions()),
            _fileSystem)
        {
            AnnotationKey = Key,
            ImportFilePath = file,
        }.OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _fixture.Console.ErrorLines.ShouldHaveSingleItem().ShouldStartWith("The import file 'annotation.json' does not contain a valid annotation: ");
    }

    private ConfigSetCommand ConfigSet()
    {
        return new ConfigSetCommand(_fixture.Console.Object, _fixture.Context.Object, _configurations.Object, _fileSystem)
        {
            AnnotationKey = Key,
            InlineProperties = ["a=1"],
        };
    }

    private ConfigPatchCommand ConfigPatch()
    {
        var file = Path(@"C:\work\patch.json");
        _fileSystem.AddFile(file, new MockFileData("""[ { "op": "test", "path": "/a", "value": 2 }, { "op": "replace", "path": "/a", "value": 3 } ]"""));

        return new ConfigPatchCommand(_fixture.Console.Object, _fixture.Context.Object, _configurations.Object, _editor.Object, Options.Create(new EditorOptions()), _fileSystem)
        {
            AnnotationKey = Key,
            ImportFilePath = file,
            Yes = true,
        };
    }

    private ConfigEditCommand ConfigEdit()
    {
        return new ConfigEditCommand(_fixture.Console.Object, _fixture.Context.Object, _configurations.Object, _editor.Object, Options.Create(new EditorOptions()))
        {
            AnnotationKey = Key,
            Yes = true,
        };
    }

    // The stored configuration, edited to another content, whose write the caller sets up.
    private Moq.Language.Flow.ISetup<IConfigurationsApiClient, Task<_42.Platform.Storyteller.Sdk.Configuration>> ConfigEditPatch()
    {
        StoredConfiguration(new JObject { ["a"] = 1 });
        EditedTo(new JObject { ["a"] = 2 });
        return _configurations.Setup(api => api.PatchConfigurationAsync("org1", "proj1", "default", Key, false, It.IsAny<object>(), It.IsAny<CancellationToken>()));
    }

    private void EditedTo(JObject edited)
    {
        _editor
            .Setup(service => service.EditJsonAsync(
                It.IsAny<IExtendedConsole>(),
                It.IsAny<EditorOptions>(),
                It.IsAny<JObject>(),
                It.IsAny<string>(),
                false,
                false))
            .ReturnsAsync(new JsonEditResult(edited, ExitCodes.SUCCESS));
    }

    private Moq.Language.Flow.ISetup<IConfigurationsApiClient, Task<_42.Platform.Storyteller.Sdk.Configuration>> PatchOfAStoredConfiguration()
    {
        StoredConfiguration(new JObject { ["a"] = 1 });
        return _configurations.Setup(api => api.PatchConfigurationAsync("org1", "proj1", "default", Key, false, It.IsAny<object>(), It.IsAny<CancellationToken>()));
    }

    private void StoredConfiguration(JObject content)
    {
        _configurations
            .Setup(api => api.GetConfigurationVersionsAsync("org1", "proj1", "default", Key))
            .ReturnsAsync(new List<ConfigurationVersion> { new() { Version = 3, ExpirationTime = DateTimeOffset.MaxValue } });
        _configurations
            .Setup(api => api.GetConfigurationVersionAsync("org1", "proj1", "default", Key, 3))
            .ReturnsAsync(new _42.Platform.Storyteller.Sdk.Configuration { Content = content });
    }

    private Moq.Language.Flow.ISetup<IAccessApiClient, Task<CertificateRenewalResult>> Renewal()
    {
        return _access.Setup(api => api.RenewMachineCertificateAsync("org1", "proj1", "m1", It.IsAny<CancellationToken>()));
    }

    private MachineRenewCommand MachineRenew(string? output)
    {
        _fileSystem.AddDirectory(Path(@"C:\work"));
        var services = new Mock<IServiceProvider>();
        services.Setup(provider => provider.GetService(typeof(IAccessApiClient))).Returns(_access.Object);

        return new MachineRenewCommand(_fixture.Console.Object, _fixture.Context.Object, services.Object, new SdkConfiguration(), _fileSystem)
        {
            MachineId = "m1",
            OutputPath = output,
        };
    }
}

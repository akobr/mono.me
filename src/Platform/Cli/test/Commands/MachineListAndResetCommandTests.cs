using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Commands;
using _42.Platform.Cli.Commands.MachineAccess;
using _42.Platform.Storyteller.Sdk;
using Alba.CsConsoleFormat;
using Moq;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Commands;

public class MachineListAndResetCommandTests
{
    private const string MachineId = "3f1c2a9e-5b7d-4c11-9e0a-2d4f6b8c1a07";

    private readonly RecordingConsole _console = new();
    private readonly Mock<IAccessApiClient> _accessApi = new();
    private readonly Mock<ICommandContext> _context = new();

    public MachineListAndResetCommandTests()
    {
        _context.SetupGet(context => context.OrganizationName).Returns("org1");
        _context.SetupGet(context => context.ProjectName).Returns("proj1");
    }

    [Fact]
    public async Task List_ShowsADashInsteadOfASecretForFederatedAndCertificateMachines()
    {
        _accessApi
            .Setup(api => api.GetMachineAccessesAsync("org1", "proj1"))
            .ReturnsAsync(new List<MachineAccess>
            {
                new() { Id = "m-key", AccessKey = "2s.***", CredentialKind = MachineAccessCredentialKind.ApiKey, Scopes = [MachineScope.AnnotationRead] },
                new() { Id = "m-cert", AccessKey = string.Empty, CredentialKind = MachineAccessCredentialKind.Certificate, Scopes = [MachineScope.AnnotationRead] },
                new() { Id = "m-fed", AccessKey = string.Empty, CredentialKind = MachineAccessCredentialKind.Federated, Scopes = [MachineScope.AnnotationWrite] },
                new() { Id = "m-fed-key", AccessKey = "unexpected", CredentialKind = MachineAccessCredentialKind.Federated, Scopes = [MachineScope.AnnotationRead] },
            });
        var rows = new List<string[]>();
        _console.Mock
            .Setup(console => console.WriteTable(
                It.IsAny<IEnumerable<MachineAccess>>(),
                It.IsAny<Func<MachineAccess, IEnumerable<Cell>>>(),
                It.IsAny<IEnumerable<IHeaderColumn>>()))
            .Callback((IEnumerable<MachineAccess> items, Func<MachineAccess, IEnumerable<Cell>> render, IEnumerable<IHeaderColumn>? _) =>
                rows.AddRange(items.Select(item => render(item).Select(cell => RecordingConsole.ToText(cell)).ToArray())));

        var exitCode = await new MachineListCommand(_console.Object, _context.Object, _accessApi.Object).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        rows.Select(row => (row[0], row[2], row[3])).ShouldBe(
        [
            ("m-key", "ApiKey", "2s.***"),
            ("m-cert", "Certificate", "–"),
            ("m-fed", "Federated", "–"),
            ("m-fed-key", "Federated", "–"),
        ]);
    }

    [Fact]
    public async Task List_ShowsTheRuntimeScopeStringsSecondAndTheManagementScopeStringsLast()
    {
        _accessApi
            .Setup(api => api.GetMachineAccessesAsync("org1", "proj1"))
            .ReturnsAsync(new List<MachineAccess>
            {
                new()
                {
                    Id = "m-sync",
                    AccessKey = "2s.***",
                    CredentialKind = MachineAccessCredentialKind.ApiKey,
                    Scopes =
                    [
                        MachineScope.AnnotationRead,
                        MachineScope.AnnotationWrite,
                        MachineScope.ConfigurationRead,
                        MachineScope.ConfigurationWrite,
                        MachineScope.ConfigurationTransfer,
                    ],
                },
                new()
                {
                    Id = "m-all",
                    AccessKey = "2s.***",
                    CredentialKind = MachineAccessCredentialKind.ApiKey,
                    Scopes =
                    [
                        MachineScope.AnnotationRead,
                        MachineScope.AnnotationWrite,
                        MachineScope.ConfigurationRead,
                        MachineScope.ConfigurationWrite,
                        MachineScope.SecretsRead,
                        MachineScope.SecretsWrite,
                        MachineScope.ConfigurationTransfer,
                        MachineScope.SchemaWrite,
                        MachineScope.TemplateWrite,
                        MachineScope.AnnotationManage,
                        MachineScope.KeyValueManage,
                    ],
                },
                new() { Id = "m-runtime", AccessKey = "2s.***", CredentialKind = MachineAccessCredentialKind.ApiKey, Scopes = [MachineScope.ConfigurationRead, MachineScope.SecretsRead] },
                new() { Id = "m-empty", AccessKey = "2s.***", CredentialKind = MachineAccessCredentialKind.ApiKey, Scopes = [] },
                new() { Id = "m-missing", AccessKey = "2s.***", CredentialKind = MachineAccessCredentialKind.ApiKey },
            });

        var exitCode = await new MachineListCommand(_console.Object, _context.Object, _accessApi.Object).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        var table = _console.Tables.ShouldHaveSingleItem();
        table.Headers.ShouldBe(["Id", "Scopes", "Kind", "Secret", "RestrictionTo", "Management"]);
        table.Rows.Select(row => (row[0], row[1], row[^1])).ShouldBe(
        [
            ("m-sync", "Annotation.Read, Annotation.Write, Configuration.Read, Configuration.Write", "Configuration.Transfer"),
            ("m-all", "Annotation.Read, Annotation.Write, Configuration.Read, Configuration.Write, Secrets.Read, Secrets.Write", "Configuration.Transfer, Schema.Write, Template.Write, Annotation.Manage, KeyValue.Manage"),
            ("m-runtime", "Configuration.Read, Secrets.Read", string.Empty),
            ("m-empty", string.Empty, string.Empty),
            ("m-missing", string.Empty, string.Empty),
        ]);
    }

    [Fact]
    public async Task Reset_OfAFederatedMachine_PrintsTheServerMessage()
    {
        // ResetMachineAccess declares no 400, so the body arrives as text.
        _accessApi
            .Setup(api => api.ResetMachineAccessAsync("org1", "proj1", MachineId))
            .ThrowsAsync(new ApiException(
                "The HTTP status code of the response was not expected (400).",
                400,
                """{"Message":"A Federated machine has no secret to reset. Change its federation rules instead."}""",
                new Dictionary<string, IEnumerable<string>>(),
                null));

        var exitCode = await Reset();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _console.Lines.ShouldContain("! A Federated machine has no secret to reset. Change its federation rules instead.");
        _console.Lines.ShouldNotContain(line => line.Contains("copy the access key"));
    }

    [Fact]
    public async Task Reset_OfAnApiKeyMachine_StillAsksToCopyTheNewKey()
    {
        _accessApi
            .Setup(api => api.ResetMachineAccessAsync("org1", "proj1", MachineId))
            .ReturnsAsync(new MachineAccess { Id = MachineId, AccessKey = "2s.new-key", CredentialKind = MachineAccessCredentialKind.ApiKey });

        var exitCode = await Reset();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _console.Lines.ShouldContain("! Make sure to copy the access key (secret), it is not stored anywhere.");
    }

    private Task<int> Reset()
    {
        return new MachineResetCommand(_console.Object, _context.Object, _accessApi.Object) { MachineId = MachineId }.OnExecuteAsync();
    }
}

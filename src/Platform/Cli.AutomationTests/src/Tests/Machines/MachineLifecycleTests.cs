using _42.Platform.Cli.AutomationTests.Access;
using _42.Platform.Cli.AutomationTests.Running;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;

namespace _42.Platform.Cli.AutomationTests.Tests.Machines;

// Machine Lifecycle: machines of the project, created, reset and deleted through sform account machine.
[Area(Area.Machines)]
public sealed class MachineLifecycleTests : MachineAccessTest
{
    private const string AllScopeNames = "AnnotationRead, AnnotationWrite, ConfigurationRead, ConfigurationWrite, SecretsRead, SecretsWrite, ConfigurationTransfer, SchemaWrite, TemplateWrite, AnnotationManage or KeyValueManage";

    private static readonly string[] ManagementScopes = ["ConfigurationTransfer", "SchemaWrite", "TemplateWrite", "AnnotationManage", "KeyValueManage"];

    private static readonly string[] WriteScopes = ["AnnotationRead", "AnnotationWrite", "ConfigurationRead", "ConfigurationWrite"];

    // MCH-LIF-01: account machine (and agent) lists the project's machines with kind and scopes.
    // As an Editor, a machine created with account machine create -r is listed by account machine -p <org>.<project> and
    // by its alias account agent: a row of the "Machine accesses" table with its id, the kind ApiKey and the scope strings
    // Annotation.Read, Configuration.Read.
    [Test]
    [TestCase(242)]
    [UserRole(Role.Editor)]
    [Covers("account machine")]
    [Covers("account agent")]
    public async Task Machines_List_ShowsKindAndScopes()
    {
        var machine = await CreateMachineAsync("-r");

        foreach (var command in new[] { "machine", "agent" })
        {
            var list = (await Sform(InProject("account", command))).ShouldSucceed().ShouldContain("Machine accesses");
            var row = LineWith(list, machine.Id);
            await Assert.That(row).Contains("ApiKey");
            await Assert.That(row).Contains("Annotation.Read, Configuration.Read");
        }
    }

    // MCH-LIF-02: A Reader is refused the machine list.
    // As a Reader of the project, account machine -p <org>.<project> and account agent -p <org>.<project> both end with 44,
    // the server's "No Contributor access to '<org>.<project>'." and "Error code: AccessDenied".
    [Test]
    [TestCase(258)]
    [UserRole(Role.Reader)]
    [Covers("account machine")]
    [Covers("account agent")]
    public async Task Machines_ListAsReader_Unauthorized()
    {
        foreach (var command in new[] { "machine", "agent" })
        {
            (await Sform(InProject("account", command)))
                .ShouldExitWith(CliExit.Unauthorized)
                .ShouldContain($"No Contributor access to '{ProjectKey}'.")
                .ShouldContain("Error code: AccessDenied");
        }
    }

    // MCH-LIF-03: A created API key machine signs in with its key and reads with the default read-only scopes.
    // As an Editor, sform account machine create -r in the project prints the machine (its id, the key once, the kind
    // ApiKey and the scopes AnnotationRead and ConfigurationRead, nothing that writes), says to copy the access key and
    // lists the STORYTELLER_* variables of a pipeline; with that key in the STORYTELLER_* variables, sform account signs
    // the machine in, and sform story get reads an annotation the Editor created. The machine is deleted when the test
    // ends.
    [Test]
    [TestCase(259)]
    [Elementary]
    [UserRole(Role.Editor)]
    [Covers("account machine create", "-r")]
    public async Task Machine_CreateApiKey_ReadsOnly()
    {
        var key = CliData.ResponsibilityKey();
        (await Sform(InProject("story", "set", key))).ShouldSucceed();

        var machine = await CreateMachineAsync("-r");

        await Assert.That(machine.CredentialKind).IsEqualTo("ApiKey");
        await Assert.That(machine.AccessKey).IsNotNull();
        await Assert.That(machine.Scopes.Select(NormalizeScope)).IsEquivalentTo(new[] { "annotationread", "configurationread" });
        machine.Result
            .ShouldContain("Make sure to copy the access key (secret), it is not stored anywhere.")
            .ShouldContain("To sign in sform in a pipeline, set:")
            .ShouldContain($"STORYTELLER_ENDPOINT={Target.BaseUrl}")
            .ShouldContain("STORYTELLER_API_KEY=<access key above>");

        var asMachine = AsMachine(machine.ApiKeyCredentials);
        (await Sform(asMachine, "account"))
            .ShouldSucceed()
            .ShouldContain($"Signed in as machine {machine.Id} with an API key")
            .ShouldContain($"Project {ProjectKey} with {View} view.");

        var read = (await Sform(asMachine, "story", "get", key)).ShouldSucceed();
        await Assert.That(read.JsonNode()["AnnotationKey"]!.GetValue<string>()).IsEqualTo(key);
    }

    // MCH-LIF-04: -w gives read and write scopes, and the machine writes a configuration.
    // As an Editor, a machine created with -w and another with --write each hold AnnotationRead, AnnotationWrite,
    // ConfigurationRead and ConfigurationWrite; as each machine, config set <rst> -x a=<n> (of an annotation the Editor
    // created) succeeds and stores the value.
    [Test]
    [TestCase(260)]
    [UserRole(Role.Editor)]
    [Covers("account machine create", "-w", "--write")]
    public async Task Machine_CreateWrite_WritesConfiguration()
    {
        var key = await CreateAnnotationAsync();

        var value = 0L;
        foreach (var option in new[] { "-w", "--write" })
        {
            var machine = await CreateMachineAsync(option);
            await ShouldHaveScopesAsync(machine.Scopes, WriteScopes);

            value++;
            var saved = (await Sform(AsMachine(InView(machine.SignInCredentials)), "story", "config", "set", key, "-x", $"a={value}")).ShouldSucceed().JsonNode();
            await Assert.That(saved["Content"]?["a"]?.GetValue<long>()).IsEqualTo(value);
        }
    }

    // MCH-LIF-05: --read-only gives no write scope: a write is refused with exit 44.
    // As an Editor, a machine created with --read-only holds no write scope; as the machine, story set <new rst> ends
    // with 44, "This operation needs the scope Annotation.Write, which the machine was not created with." and "Error code:
    // AccessDenied", and the Editor's story get <rst> finds no annotation.
    [Test]
    [TestCase(261)]
    [UserRole(Role.Editor)]
    [Covers("account machine create", "--read-only")]
    public async Task Machine_ReadOnly_WriteRefused()
    {
        var machine = await CreateMachineAsync("--read-only");
        await Assert.That(machine.Scopes.Select(NormalizeScope).Any(scope => scope.EndsWith("write", StringComparison.Ordinal))).IsFalse();

        var key = CliData.ResponsibilityKey();
        (await Sform(AsMachine(InView(machine.SignInCredentials)), "story", "set", key))
            .ShouldExitWith(CliExit.Unauthorized)
            .ShouldContain("This operation needs the scope Annotation.Write, which the machine was not created with.")
            .ShouldContain("Error code: AccessDenied");

        (await Sform(InProject("story", "get", key)))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain($"The annotation '{key}' has not been found.");
    }

    // MCH-LIF-06: -a restricts the machine to one annotation and its descendants.
    // As an Editor, with <rst>, a unit <unt> of it and another responsibility <rst2> created, a machine created with
    // -w -a <rst> and another with -w --annotation <rst> are restricted to <rst> (AnnotationKey of the machine); as each
    // machine, story get <unt> reads the unit and story get <rst2> ends with 44.
    [Test]
    [TestCase(262)]
    [UserRole(Role.Editor)]
    [Covers("account machine create", "-a", "--annotation")]
    public async Task Machine_Annotation_RestrictsAccess()
    {
        var key = CliData.ResponsibilityKey();
        var unit = CliData.UnitKeyOf(key);
        var other = CliData.ResponsibilityKey();
        foreach (var annotation in new[] { key, unit, other })
        {
            (await Sform(InProject("story", "set", annotation))).ShouldSucceed();
        }

        foreach (var option in new[] { "-a", "--annotation" })
        {
            var machine = await CreateMachineAsync("-w", option, key);
            await Assert.That(machine.Result.JsonNode()["AnnotationKey"]?.GetValue<string>()).IsEqualTo(key);

            var asMachine = AsMachine(InView(machine.SignInCredentials));
            var read = (await Sform(asMachine, "story", "get", unit)).ShouldSucceed();
            await Assert.That(read.JsonNode()["AnnotationKey"]!.GetValue<string>()).IsEqualTo(unit);

            (await Sform(asMachine, "story", "get", other)).ShouldExitWith(CliExit.Unauthorized);
        }
    }

    // MCH-LIF-07: --scope accepts the enum name, the scope string and kebab case, repeated or comma-separated; an unknown
    // scope fails with exit 2.
    // As a ContributorWithSecrets, --scope AnnotationRead,annotation-write --scope Configuration.Read --scope
    // configuration-write --scope SecretsRead --scope Secrets.Write stores the six scopes, and --scope
    // secrets-read,configuration-read stores those two; any other stored scope is a read scope the server adds. --scope
    // Superuser ends with 2 before any call: "Unknown machine scope 'Superuser'. Use AnnotationRead, …", and no machine.
    [Test]
    [TestCase(263)]
    [UserRole(Role.Secrets)]
    [Covers("account machine create", "--scope", "--scope=AnnotationRead", "--scope=AnnotationWrite", "--scope=ConfigurationRead", "--scope=ConfigurationWrite", "--scope=SecretsRead", "--scope=SecretsWrite")]
    public async Task Machine_ScopeSyntaxes_Accepted()
    {
        var all = await CreateMachineAsync("--scope", "AnnotationRead,annotation-write", "--scope", "Configuration.Read", "--scope", "configuration-write", "--scope", "SecretsRead", "--scope", "Secrets.Write");
        await ShouldHoldRequestedScopesAsync(all, "AnnotationRead", "AnnotationWrite", "ConfigurationRead", "ConfigurationWrite", "SecretsRead", "SecretsWrite");

        var reads = await CreateMachineAsync("--scope", "secrets-read,configuration-read");
        await ShouldHoldRequestedScopesAsync(reads, "SecretsRead", "ConfigurationRead");

        var unknown = (await RunCreatingMachineAsync(ProjectKey, InProject("account", "machine", "create", "--scope", "Superuser")))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain($"Unknown machine scope 'Superuser'. Use {AllScopeNames}.");
        await Assert.That(TryMachineIdOf(unknown)).IsNull();
    }

    // MCH-LIF-08: A Secrets.* scope needs ContributorWithSecrets: a Contributor is refused.
    // As a Contributor of the project (Target:RoleProjects:Contributor), account machine create --scope SecretsRead ends
    // with 44, "No ContributorWithSecrets access to '<contributor project>'." and "Error code: AccessDenied", and prints no
    // machine.
    [Test]
    [TestCase(279)]
    [UserRole(Role.Contributor)]
    [Covers("account machine create", "--scope")]
    public async Task Machine_SecretsScopeAsContributor_Refused()
    {
        var refused = (await RunCreatingMachineAsync(ProjectKey, InProject("account", "machine", "create", "--scope", "SecretsRead")))
            .ShouldExitWith(CliExit.Unauthorized)
            .ShouldContain($"No ContributorWithSecrets access to '{ProjectKey}'.")
            .ShouldContain("Error code: AccessDenied");
        await Assert.That(TryMachineIdOf(refused)).IsNull();
    }

    // MCH-LIF-09: -m grants each management scope, with an Administrator only; --scope grants them too, and -m refuses a
    // runtime scope.
    // As an Administrator, -w -m ConfigurationTransfer,SchemaWrite -m TemplateWrite --manage AnnotationManage --manage
    // KeyValueManage and -w --scope ConfigurationTransfer,SchemaWrite,TemplateWrite,AnnotationManage,KeyValueManage both
    // store the five management scopes next to the four runtime scopes of -w; -m SecretsRead ends with 2 before any call:
    // "Secrets.Read is not a management scope; give it with --scope. --manage takes …".
    [Test]
    [TestCase(281)]
    [UserRole(Role.Admin)]
    [Covers("account machine create", "-m", "--manage", "--manage=ConfigurationTransfer", "--manage=SchemaWrite", "--manage=TemplateWrite", "--manage=AnnotationManage", "--manage=KeyValueManage", "--scope=ConfigurationTransfer", "--scope=SchemaWrite", "--scope=TemplateWrite", "--scope=AnnotationManage", "--scope=KeyValueManage")]
    public async Task Machine_ManagementScopes_Granted()
    {
        var managed = await CreateMachineAsync("-w", "-m", "ConfigurationTransfer,SchemaWrite", "-m", "TemplateWrite", "--manage", "AnnotationManage", "--manage", "KeyValueManage");
        await ShouldHaveScopesAsync(managed.Scopes, [.. WriteScopes, .. ManagementScopes]);

        var scoped = await CreateMachineAsync("-w", "--scope", "ConfigurationTransfer,SchemaWrite,TemplateWrite,AnnotationManage,KeyValueManage");
        await ShouldHaveScopesAsync(scoped.Scopes, [.. WriteScopes, .. ManagementScopes]);

        var runtime = (await RunCreatingMachineAsync(ProjectKey, InProject("account", "machine", "create", "-m", "SecretsRead")))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain("Secrets.Read is not a management scope; give it with --scope. --manage takes ConfigurationTransfer, SchemaWrite, TemplateWrite, AnnotationManage or KeyValueManage.");
        await Assert.That(TryMachineIdOf(runtime)).IsNull();
    }

    // MCH-LIF-10: -m by a ContributorWithSecrets is refused.
    // In a disposable project, a second pool user granted ContributorWithSecrets there runs account machine create -p
    // <org>.<project> -m SchemaWrite: exit 44, "No Administrator access to '<org>.<project>'." and "Error code:
    // AccessDenied"; the Owner's account machine lists no machine.
    [Test]
    [TestCase(283)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("account machine create", "-m")]
    public async Task Machine_ManageAsNonAdmin_Refused()
    {
        await CreateDisposableProjectAsync();
        var second = await AsGrantedUserAsync(PlatformRoles.ContributorWithSecrets);

        (await RunCreatingMachineAsync(ProjectKey, second, new SformRunOptions(), InProject("account", "machine", "create", "-m", "SchemaWrite")))
            .ShouldExitWith(CliExit.Unauthorized)
            .ShouldContain($"No Administrator access to '{ProjectKey}'.")
            .ShouldContain("Error code: AccessDenied");

        (await Sform(InProject("account", "machine")))
            .ShouldSucceed()
            .ShouldContain("No machine access granted, yet.");
    }

    // MCH-LIF-11: machine set is an alias of create.
    // As an Editor, account machine set -p … -r creates a machine as create does: the machine JSON with its id, the kind
    // ApiKey and the scopes AnnotationRead and ConfigurationRead, "Make sure to copy the access key (secret), …" and the
    // variables of a pipeline; account machine lists it.
    [Test]
    [TestCase(287)]
    [UserRole(Role.Editor)]
    [Covers("account machine set")]
    public async Task Machine_SetAlias_Creates()
    {
        var created = (await RunCreatingMachineAsync(ProjectKey, InProject("account", "machine", "set", "-r")))
            .ShouldSucceed()
            .ShouldContain("Make sure to copy the access key (secret), it is not stored anywhere.")
            .ShouldContain("To sign in sform in a pipeline, set:");
        var id = MachineIdOf(created);
        await Assert.That(created.JsonNode()["CredentialKind"]?.GetValue<string>()).IsEqualTo("ApiKey");
        await Assert.That(ScopesOf(created).Select(NormalizeScope)).IsEquivalentTo(new[] { "annotationread", "configurationread" });

        var list = (await Sform(InProject("account", "machine"))).ShouldSucceed();
        LineWith(list, id);
    }

    // MCH-LIF-12: machine reset issues a new API key, and the old one stops working.
    // As an Editor, account machine reset <id> of a created machine prints the machine with a new access key ("Make sure
    // to copy the access key (secret), …"); story get <rst> as the machine ends with 44 with the old key and 0 with the
    // new one.
    [Test]
    [TestCase(289)]
    [UserRole(Role.Editor)]
    [Covers("account machine reset", "arg0")]
    public async Task Machine_Reset_RotatesKey()
    {
        var key = await CreateAnnotationAsync();
        var machine = await CreateMachineAsync("-r");

        var reset = (await Sform(InProject("account", "machine", "reset", machine.Id)))
            .ShouldSucceed()
            .ShouldContain("Make sure to copy the access key (secret), it is not stored anywhere.");
        var json = reset.JsonNode();
        await Assert.That(json["Id"]?.GetValue<string>()).IsEqualTo(machine.Id);
        var newKey = json["AccessKey"]?.GetValue<string>() ?? throw reset.Fail("printed no AccessKey");
        AddSecret(newKey);
        await Assert.That(newKey).IsNotEqualTo(machine.AccessKey);

        (await Sform(AsMachine(machine.ApiKeyCredentials), "story", "get", key)).ShouldExitWith(CliExit.Unauthorized);

        var endpoint = Target.BaseUrl ?? throw new InvalidOperationException("Target:BaseUrl is not configured.");
        (await Sform(AsMachine(MachineCredentials.ForApiKey(newKey, endpoint, ProjectKey, View)), "story", "get", key)).ShouldSucceed();
    }

    // MCH-LIF-13: machine delete (and remove) removes the machine, and its key stops working.
    // As an Editor, of two created machines account machine delete <id1> and account machine remove <id2> each say "The
    // machine access <id> has been deleted."; account machine lists neither, and story get <rst> with either key ends with
    // 44.
    [Test]
    [TestCase(291)]
    [UserRole(Role.Editor)]
    [Covers("account machine delete", "arg0")]
    [Covers("account machine remove", "arg0")]
    public async Task Machine_Delete_RevokesAccess()
    {
        var key = await CreateAnnotationAsync();
        var first = await CreateMachineAsync("-r");
        var second = await CreateMachineAsync("-r");

        (await Sform(InProject("account", "machine", "delete", first.Id)))
            .ShouldSucceed()
            .ShouldContain($"The machine access {first.Id} has been deleted.");
        (await Sform(InProject("account", "machine", "remove", second.Id)))
            .ShouldSucceed()
            .ShouldContain($"The machine access {second.Id} has been deleted.");

        (await Sform(InProject("account", "machine")))
            .ShouldSucceed()
            .ShouldNotContain(first.Id)
            .ShouldNotContain(second.Id);

        foreach (var machine in new[] { first, second })
        {
            (await Sform(AsMachine(machine.ApiKeyCredentials), "story", "get", key)).ShouldExitWith(CliExit.Unauthorized);
        }
    }

    // The machine holds the requested scopes, and any other scope it holds is a read scope the server added.
    private static async Task ShouldHoldRequestedScopesAsync(CreatedMachine machine, params string[] requested)
    {
        await ShouldHaveScopesAsync(machine.Scopes, requested);
        var wanted = requested.Select(NormalizeScope).ToHashSet(StringComparer.Ordinal);
        var added = machine.Scopes.Select(NormalizeScope).Where(scope => !wanted.Contains(scope)).ToArray();
        await Assert.That(added.All(scope => scope.EndsWith("read", StringComparison.Ordinal))).IsTrue();
    }
}

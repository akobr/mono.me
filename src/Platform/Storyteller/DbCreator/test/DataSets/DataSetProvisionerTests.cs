using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.DbCreator.DataSets;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace _42.Platform.Storyteller.DbCreator.UnitTests.DataSets;

// The provisioning of a target over Moq of the access model and the views: what the services refuse, and what someone
// else created in the meantime.
public class DataSetProvisionerTests
{
    private const string OwnerId = "user_01owner";

    private static readonly DataSetOwner Owner = new(OwnerId, OwnerId, OwnerId);
    private static readonly DataSetTarget Target = new("fabrikam", "invoicing-suite", "preview");

    private readonly Mock<IAccessService> _access = new();
    private readonly Mock<IViewService> _views = new();

    public DataSetProvisionerTests()
    {
        _views.Setup(views => views.GetViewsAsync(Target.Organization, Target.Project, false))
            .ReturnsAsync([new View { Name = "default", IsDefault = true }]);
    }

    [Fact]
    public async Task Provision_ADottedOrganizationThatIsAProject_IsRefusedBeforeAnythingIsWritten()
    {
        // the key of a project reads as an access point the owner owns, but a dotted name is never an organization
        _access.Setup(access => access.GetAccessPointAsync("42dotnet.main")).ReturnsAsync(new AccessPoint
        {
            Key = "42dotnet.main",
            AccessMap = new Dictionary<string, AccountRole> { [OwnerId] = AccountRole.Owner },
        });

        var exception = await Should.ThrowAsync<DataSetException>(
            () => Provisioner().ProvisionAsync(new DataSetTarget("42dotnet.main", "xyz", "default"), Owner, null, null, dryRun: false, TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("organization name '42dotnet.main' is not valid");
        _access.Verify(access => access.CreateAccountAsync(It.IsAny<AccountCreate>()), Times.Never);
        _access.Verify(access => access.CreateAccessPointAsync(It.IsAny<AccessPointCreate>()), Times.Never);
    }

    [Fact]
    public async Task Provision_WhatTheAccessModelRefusesAfterAll_IsWrongInput()
    {
        _access.Setup(access => access.CreateAccessPointAsync(It.IsAny<AccessPointCreate>()))
            .ThrowsAsync(new AccessDeniedException("The account 'user_01owner' doesn't have owner rights to the organization 'fabrikam'."));

        (await Should.ThrowAsync<DataSetException>(() => Provision())).Message.ShouldContain("doesn't have owner rights");

        _access.Setup(access => access.CreateAccessPointAsync(It.IsAny<AccessPointCreate>()))
            .ThrowsAsync(new InvalidInputException("The project name 'x' is not valid.", ErrorCodes.InvalidName));

        (await Should.ThrowAsync<DataSetException>(() => Provision())).Message.ShouldBe("The project name 'x' is not valid.");
    }

    [Fact]
    public async Task Provision_AViewNameTheRegistryRefuses_IsWrongInput()
    {
        _views.Setup(views => views.CreateViewAsync(Target.Organization, Target.Project, It.IsAny<ViewCreate>(), AuditActor.System))
            .ThrowsAsync(new InvalidInputException("The view name 'preview' is reserved.", ErrorCodes.InvalidName));

        (await Should.ThrowAsync<DataSetException>(() => Provision())).Message.ShouldBe("The view name 'preview' is reserved.");
    }

    [Fact]
    public async Task Provision_WhatWasCreatedInTheMeantime_IsUsedAsItIs()
    {
        _access.Setup(access => access.CreateAccountAsync(It.IsAny<AccountCreate>()))
            .ThrowsAsync(new ConflictException("The account exists.", ErrorCodes.AccountExists));
        _access.Setup(access => access.CreateAccessPointAsync(It.IsAny<AccessPointCreate>()))
            .ThrowsAsync(new ConflictException("The access point exists.", ErrorCodes.AccessPointExists));
        _views.Setup(views => views.CreateViewAsync(Target.Organization, Target.Project, It.IsAny<ViewCreate>(), AuditActor.System))
            .ThrowsAsync(new ConflictException("The view exists.", ErrorCodes.ViewExists));

        (await Provision()).ShouldBe(new DataSetProvisioning(false, false, false, false));

        _access.Verify(access => access.CreateAccountAsync(It.IsAny<AccountCreate>()), Times.Once);
        _access.Verify(access => access.CreateAccessPointAsync(It.IsAny<AccessPointCreate>()), Times.Once);
        _views.Verify(views => views.CreateViewAsync(Target.Organization, Target.Project, It.IsAny<ViewCreate>(), AuditActor.System), Times.Once);
    }

    private DataSetProvisioner Provisioner()
    {
        return new DataSetProvisioner(_access.Object, _views.Object, NullLogger<DataSetProvisioner>.Instance);
    }

    private Task<DataSetProvisioning> Provision()
    {
        return Provisioner().ProvisionAsync(Target, Owner, "The invoices.", "A preview.", dryRun: false, TestContext.Current.CancellationToken);
    }
}

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using _42.Platform.Storyteller.Annotating;
using Microsoft.Extensions.Logging;

namespace _42.Platform.Storyteller.DbCreator.DataSets;

// Makes the target of a seed exist, through the access model and the view registry as the API does: the owner's
// account, a missing organization and project with the owner as Owner, and a view other than "default". An existing
// project is used as it is: its access is not changed. The data services check none of this, so it runs first.
public sealed class DataSetProvisioner
{
    private readonly IAccessService _access;
    private readonly IViewService _views;
    private readonly ILogger<DataSetProvisioner> _logger;

    public DataSetProvisioner(IAccessService access, IViewService views, ILogger<DataSetProvisioner> logger)
    {
        _access = access;
        _views = views;
        _logger = logger;
    }

    // DataSetException, before anything is written, for a name that breaks the rules and for a missing project of an
    // organization the owner does not own (the seed never takes an organization over); also for a name or an access
    // that the access model or the view registry refuses after all. A dry run writes nothing and reports what it would
    // create.
    public async Task<DataSetProvisioning> ProvisionAsync(
        DataSetTarget target,
        DataSetOwner owner,
        string? projectDescription,
        string? viewDescription,
        bool dryRun,
        CancellationToken cancellationToken = default)
    {
        var organizationPoint = await _access.GetAccessPointAsync(target.Organization);
        var projectPoint = await _access.GetAccessPointAsync(target.ProjectKey);

        target.EnsureNames(isNewOrganization: organizationPoint is null);

        if (projectPoint is null
            && organizationPoint is not null
            && (!organizationPoint.AccessMap.TryGetValue(owner.Id, out var role) || role != AccountRole.Owner))
        {
            throw new DataSetException(
                $"The organization '{target.Organization}' exists, and the owner '{owner.Id}' is not its Owner, so the project '{target.Project}' is not created there: give an Owner of the organization as the owner, or another organization.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        // first, so creating the project mirrors the membership into the account
        var isAccountCreated = await _access.GetAccountAsync(owner.Id) is null;

        if (isAccountCreated && !dryRun)
        {
            try
            {
                await _access.CreateAccountAsync(new AccountCreate { IdentityId = owner.Id, UserName = owner.UserName, Name = owner.Name });
                _logger.LogInformation("Created the account {AccountId} of the owner.", owner.Id);
            }
            catch (ConflictException exception) when (exception.ErrorCode == ErrorCodes.AccountExists)
            {
                isAccountCreated = false;
            }
        }

        var isProjectCreated = projectPoint is null;

        if (isProjectCreated && !dryRun)
        {
            try
            {
                await _access.CreateAccessPointAsync(new AccessPointCreate
                {
                    OwnerId = owner.Id,
                    Organization = target.Organization,
                    Project = target.Project,
                    ProjectDescription = projectDescription,
                });
                _logger.LogInformation("Created the project {ProjectKey} with the owner {AccountId}.", target.ProjectKey, owner.Id);
            }
            catch (ConflictException exception) when (exception.ErrorCode == ErrorCodes.AccessPointExists)
            {
                // created by someone else in the meantime: used as it is
                isProjectCreated = false;
            }
            catch (AccessDeniedException exception)
            {
                throw new DataSetException(exception.Message, exception);
            }
            catch (InvalidInputException exception)
            {
                // the access model checks the names too; EnsureNames applies its rules first, so this is a safety net
                throw new DataSetException(exception.Message, exception);
            }
        }

        var isViewRegistered = false;

        if (!target.IsDefaultView)
        {
            var views = await _views.GetViewsAsync(target.Organization, target.Project, discover: false);
            isViewRegistered = !views.Any(view => view.Name == target.View && view.IsRegistered);

            if (isViewRegistered && !dryRun)
            {
                try
                {
                    await _views.CreateViewAsync(
                        target.Organization,
                        target.Project,
                        new ViewCreate { Name = target.View, Description = viewDescription },
                        AuditActor.System);
                    _logger.LogInformation("Registered the view {View} of {ProjectKey}.", target.View, target.ProjectKey);
                }
                catch (ConflictException exception) when (exception.ErrorCode == ErrorCodes.ViewExists)
                {
                    isViewRegistered = false;
                }
                catch (InvalidInputException exception)
                {
                    throw new DataSetException(exception.Message, exception);
                }
            }
        }

        return new DataSetProvisioning(
            isAccountCreated,
            isProjectCreated && organizationPoint is null,
            isProjectCreated,
            isViewRegistered);
    }
}

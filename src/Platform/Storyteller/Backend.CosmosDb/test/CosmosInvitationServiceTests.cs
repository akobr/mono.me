#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using _42.Platform.Storyteller.Entities.Access;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

public class CosmosInvitationServiceTests(Startup startup)
    : BaseTestsClass(startup)
{
    // one organization for the whole class, every test works in its own project
    private const string Organization = "inv-tests";
    private const string OwnerId = "inv-tests-owner";

    private readonly RecordingSender _sender = new();
    private readonly TestClock _clock = new() { Now = new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero) };

    private IAccessService Access => Context.Services.GetRequiredService<IAccessService>();

    private CosmosInvitationService Invitations => new(
        Context.Services.GetRequiredService<IContainerRepositoryProvider>(),
        Access,
        _sender,
        Context.Services.GetRequiredService<IOptions<JsonSerializerOptions>>(),
        Options.Create(new InvitationOptions()),
        NullLogger<CosmosInvitationService>.Instance,
        _clock);

    [Fact]
    public async Task Create_ByOwner_StoresAPendingInvitationAndSendsTheEmail()
    {
        var pointKey = await CreateProjectAsync();
        var email = NewEmail();

        var invitation = await Invitations.CreateInvitationAsync(pointKey, Create($"  {email.ToUpperInvariant()} ", AccountRole.Contributor), OwnerId, "Owner");

        invitation.Status.Should().Be(InvitationStatus.Pending);
        invitation.Email.Should().Be(email);
        invitation.ExpiresAt.Should().Be(_clock.Now.AddDays(7));
        invitation.InvitedByName.Should().Be("Owner");
        invitation.IsEmailSent.Should().BeTrue();
        invitation.ExternalInvitationId.Should().Be(_sender.Sent.Single().ExternalId);
        (await Invitations.GetInvitationsAsync(pointKey, OwnerId)).Select(item => item.Id).Should().Equal(invitation.Id);
    }

    [Fact]
    public async Task Create_SenderFails_KeepsTheInvitationWithoutEmail()
    {
        var pointKey = await CreateProjectAsync();
        _sender.Failure = new InvalidOperationException("WorkOS is down");

        var invitation = await Invitations.CreateInvitationAsync(pointKey, Create(NewEmail(), AccountRole.Reader), OwnerId, null);

        invitation.IsEmailSent.Should().BeFalse();
        invitation.ExternalInvitationId.Should().BeNull();
        (await Invitations.GetInvitationsAsync(pointKey, OwnerId)).Should().ContainSingle();
    }

    [Fact]
    public async Task Create_PendingDuplicate_ThrowsInvitationExistsUntilItExpires()
    {
        var pointKey = await CreateProjectAsync();
        var email = NewEmail();
        await Invitations.CreateInvitationAsync(pointKey, Create(email, AccountRole.Reader), OwnerId, null);

        var duplicate = () => Invitations.CreateInvitationAsync(pointKey, Create(email, AccountRole.Contributor), OwnerId, null);
        (await duplicate.Should().ThrowAsync<ConflictException>()).Which.ErrorCode.Should().Be(ErrorCodes.InvitationExists);

        _clock.Now = _clock.Now.AddDays(8);
        var renewed = await Invitations.CreateInvitationAsync(pointKey, Create(email, AccountRole.Contributor), OwnerId, null);
        renewed.Status.Should().Be(InvitationStatus.Pending);
    }

    [Fact]
    public async Task Create_OwnerRoleByAdministrator_ThrowsAccessDenied()
    {
        var pointKey = await CreateProjectAsync();
        var adminId = await CreateMemberAsync(pointKey, AccountRole.Administrator);

        var owner = () => Invitations.CreateInvitationAsync(pointKey, Create(NewEmail(), AccountRole.Owner), adminId, null);
        var reader = await Invitations.CreateInvitationAsync(pointKey, Create(NewEmail(), AccountRole.Reader), adminId, null);

        await owner.Should().ThrowAsync<AccessDeniedException>();
        reader.InvitedById.Should().Be(adminId);
    }

    [Fact]
    public async Task Create_ByContributor_ThrowsAccessDenied()
    {
        var pointKey = await CreateProjectAsync();
        var contributorId = await CreateMemberAsync(pointKey, AccountRole.Contributor);

        var act = () => Invitations.CreateInvitationAsync(pointKey, Create(NewEmail(), AccountRole.Reader), contributorId, null);

        await act.Should().ThrowAsync<AccessDeniedException>();
    }

    [Theory]
    [InlineData("not-an-email", AccountRole.Reader, null)]
    [InlineData("Ada <ada@example.com>", AccountRole.Reader, null)]
    [InlineData("ada@example.com", AccountRole.None, null)]
    [InlineData("ada@example.com", AccountRole.Reader, 0)]
    [InlineData("ada@example.com", AccountRole.Reader, 31)]
    public async Task Create_InvalidInput_ThrowsArgumentException(string email, AccountRole role, int? days)
    {
        var pointKey = await CreateProjectAsync();

        var act = () => Invitations.CreateInvitationAsync(pointKey, Create(email, role) with { ExpiresInDays = days }, OwnerId, null);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetPending_ReturnsOnlyPendingUnexpiredInvitationsForTheEmail()
    {
        var firstKey = await CreateProjectAsync();
        var secondKey = await CreateProjectAsync();
        var email = NewEmail();
        var first = await Invitations.CreateInvitationAsync(firstKey, Create(email, AccountRole.Reader), OwnerId, null);
        _clock.Now = _clock.Now.AddMinutes(1);
        var second = await Invitations.CreateInvitationAsync(secondKey, Create(email, AccountRole.Reader) with { ExpiresInDays = 1 }, OwnerId, null);
        await Invitations.CreateInvitationAsync(firstKey, Create(NewEmail(), AccountRole.Reader), OwnerId, null);

        (await Invitations.GetPendingInvitationsAsync(email.ToUpperInvariant())).Select(item => item.Id).Should().Equal(first.Id, second.Id);

        _clock.Now = _clock.Now.AddDays(2);
        (await Invitations.GetPendingInvitationsAsync(email)).Select(item => item.Id).Should().Equal(first.Id);
        (await Invitations.GetInvitationsAsync(secondKey, OwnerId)).Single().Status.Should().Be(InvitationStatus.Expired);
    }

    [Fact]
    public async Task Accept_MatchingVerifiedEmail_CreatesTheAccountAndGrantsTheRole()
    {
        var pointKey = await CreateProjectAsync();
        var email = NewEmail();
        var invitation = await Invitations.CreateInvitationAsync(pointKey, Create(email, AccountRole.Contributor), OwnerId, null);
        var invitee = Invitee(email);

        var account = await Invitations.AcceptInvitationAsync(invitation.Id, invitee);

        account.Id.Should().Be(invitee.AccountId);
        account.Name.Should().Be("Invitee");
        account.AccessMap.Should().Contain(pointKey, AccountRole.Contributor);
        (await Access.GetAccessPointAsync(pointKey))!.AccessMap.Should().Contain(invitee.AccountId, AccountRole.Contributor);
        var stored = (await Invitations.GetInvitationsAsync(pointKey, OwnerId)).Single();
        stored.Status.Should().Be(InvitationStatus.Accepted);
        stored.AcceptedById.Should().Be(invitee.AccountId);
        stored.RespondedAt.Should().Be(_clock.Now);

        var again = await Invitations.AcceptInvitationAsync(invitation.Id, invitee);
        again.AccessMap.Should().Contain(pointKey, AccountRole.Contributor);
    }

    [Fact]
    public async Task Accept_ExistingMemberWithHigherRole_KeepsTheHigherRole()
    {
        var pointKey = await CreateProjectAsync();
        var email = NewEmail();
        var memberId = await CreateMemberAsync(pointKey, AccountRole.Administrator);
        var invitation = await Invitations.CreateInvitationAsync(pointKey, Create(email, AccountRole.Reader), OwnerId, null);

        var account = await Invitations.AcceptInvitationAsync(invitation.Id, Invitee(email) with { AccountId = memberId });

        account.AccessMap.Should().Contain(pointKey, AccountRole.Administrator);
    }

    [Fact]
    public async Task Accept_DifferentEmail_ThrowsEmailMismatch()
    {
        var pointKey = await CreateProjectAsync();
        var invitation = await Invitations.CreateInvitationAsync(pointKey, Create(NewEmail(), AccountRole.Reader), OwnerId, null);

        var act = () => Invitations.AcceptInvitationAsync(invitation.Id, Invitee(NewEmail()));

        (await act.Should().ThrowAsync<AccessDeniedException>()).Which.ErrorCode.Should().Be(ErrorCodes.EmailMismatch);
    }

    [Fact]
    public async Task Accept_UnverifiedEmail_ThrowsEmailNotVerified()
    {
        var pointKey = await CreateProjectAsync();
        var email = NewEmail();
        var invitation = await Invitations.CreateInvitationAsync(pointKey, Create(email, AccountRole.Reader), OwnerId, null);

        var act = () => Invitations.AcceptInvitationAsync(invitation.Id, Invitee(email) with { IsEmailVerified = false });

        (await act.Should().ThrowAsync<AccessDeniedException>()).Which.ErrorCode.Should().Be(ErrorCodes.EmailNotVerified);
    }

    [Fact]
    public async Task Accept_Expired_ThrowsInvitationExpired()
    {
        var pointKey = await CreateProjectAsync();
        var email = NewEmail();
        var invitation = await Invitations.CreateInvitationAsync(pointKey, Create(email, AccountRole.Reader) with { ExpiresInDays = 1 }, OwnerId, null);
        _clock.Now = _clock.Now.AddDays(1);

        var act = () => Invitations.AcceptInvitationAsync(invitation.Id, Invitee(email));

        (await act.Should().ThrowAsync<ConflictException>()).Which.ErrorCode.Should().Be(ErrorCodes.InvitationExpired);
    }

    [Fact]
    public async Task Accept_Revoked_ThrowsInvitationNotPending()
    {
        var pointKey = await CreateProjectAsync();
        var email = NewEmail();
        var invitation = await Invitations.CreateInvitationAsync(pointKey, Create(email, AccountRole.Reader), OwnerId, null);
        var revoked = await Invitations.RevokeInvitationAsync(pointKey, invitation.Id, OwnerId);

        var act = () => Invitations.AcceptInvitationAsync(invitation.Id, Invitee(email));

        revoked.Status.Should().Be(InvitationStatus.Revoked);
        _sender.Revoked.Should().Equal(invitation.ExternalInvitationId);
        (await act.Should().ThrowAsync<ConflictException>()).Which.ErrorCode.Should().Be(ErrorCodes.InvitationNotPending);
    }

    [Fact]
    public async Task Accept_UnknownInvitation_ThrowsNotFound()
    {
        var act = () => Invitations.AcceptInvitationAsync(Guid.NewGuid().ToString("N"), Invitee(NewEmail()));

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Decline_MarksTheInvitationAndRevokesTheEmail()
    {
        var pointKey = await CreateProjectAsync();
        var email = NewEmail();
        var invitation = await Invitations.CreateInvitationAsync(pointKey, Create(email, AccountRole.Reader), OwnerId, null);

        var declined = await Invitations.DeclineInvitationAsync(invitation.Id, Invitee(email));

        declined.Status.Should().Be(InvitationStatus.Declined);
        _sender.Revoked.Should().Equal(invitation.ExternalInvitationId);
        (await Access.GetAccessPointAsync(pointKey))!.AccessMap.Should().ContainSingle();
    }

    [Fact]
    public async Task Resend_RestartsTheValidityWithANewEmail()
    {
        var pointKey = await CreateProjectAsync();
        var invitation = await Invitations.CreateInvitationAsync(pointKey, Create(NewEmail(), AccountRole.Reader) with { ExpiresInDays = 3 }, OwnerId, null);
        _clock.Now = _clock.Now.AddDays(5);

        var resent = await Invitations.ResendInvitationAsync(pointKey, invitation.Id, OwnerId);

        resent.Status.Should().Be(InvitationStatus.Pending);
        resent.ExpiresAt.Should().Be(_clock.Now.AddDays(3));
        resent.ExternalInvitationId.Should().NotBe(invitation.ExternalInvitationId);
        _sender.Revoked.Should().Equal(invitation.ExternalInvitationId);
        _sender.Sent.Should().HaveCount(2);
    }

    [Fact]
    public async Task Resend_FromAnotherAccessPoint_ThrowsNotFound()
    {
        var pointKey = await CreateProjectAsync();
        var otherKey = await CreateProjectAsync();
        var invitation = await Invitations.CreateInvitationAsync(pointKey, Create(NewEmail(), AccountRole.Reader), OwnerId, null);

        var act = () => Invitations.ResendInvitationAsync(otherKey, invitation.Id, OwnerId);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task GetInvitations_ByContributor_ThrowsAccessDenied()
    {
        var pointKey = await CreateProjectAsync();
        var contributorId = await CreateMemberAsync(pointKey, AccountRole.Contributor);

        var act = () => Invitations.GetInvitationsAsync(pointKey, contributorId);

        await act.Should().ThrowAsync<AccessDeniedException>();
    }

    private static InvitationCreate Create(string email, AccountRole role) => new() { Email = email, Role = role };

    private static string NewEmail() => $"user-{Guid.NewGuid():N}@example.com";

    private static InvitationIdentity Invitee(string email) =>
        new($"user_{Guid.NewGuid():N}", email, IsEmailVerified: true, UserName: email, Name: "Invitee");

    private static string NewProjectName() => $"p-{Guid.NewGuid():N}"[..14];

    private async Task<string> CreateProjectAsync()
    {
        if (await Access.GetAccountAsync(OwnerId) is null)
        {
            await Access.CreateAccountAsync(new AccountCreate
            {
                IdentityId = OwnerId,
                UserName = "owner@example.com",
                Name = "Owner",
                Organization = Organization,
                Project = "base",
            });
        }

        var point = await Access.CreateAccessPointAsync(new AccessPointCreate
        {
            Organization = Organization,
            Project = NewProjectName(),
            OwnerId = OwnerId,
        });
        return point.Key;
    }

    private async Task<string> CreateMemberAsync(string pointKey, AccountRole role)
    {
        var id = $"acc-{Guid.NewGuid():N}";
        var repository = Context.Services.GetRequiredService<IContainerRepositoryProvider>().GetCore();
        await repository.Container.CreateItemAsync(
            new AccountEntity { Id = id, UserName = $"{id}@example.com", Name = id, AccessMap = new() },
            new PartitionKey("access"));
        await Access.JoinAccessPointAsync(pointKey, id, role);
        return id;
    }

    private sealed class RecordingSender : IInvitationSender
    {
        public List<(string Email, string ExternalId)> Sent { get; } = [];

        public List<string> Revoked { get; } = [];

        public Exception? Failure { get; set; }

        public Task<string?> SendAsync(Invitation invitation, CancellationToken cancellationToken = default)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            var externalId = $"invitation_{Guid.NewGuid():N}";
            Sent.Add((invitation.Email, externalId));
            return Task.FromResult<string?>(externalId);
        }

        public Task RevokeAsync(string externalInvitationId, CancellationToken cancellationToken = default)
        {
            Revoked.Add(externalInvitationId);
            return Task.CompletedTask;
        }
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; }

        public override DateTimeOffset GetUtcNow() => Now;
    }
}

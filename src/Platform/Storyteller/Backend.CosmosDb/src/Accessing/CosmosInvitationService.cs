using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text.Json;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Accessing.Model;
using _42.Platform.Storyteller.Entities.Access;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Accessing;

// Invitations are Storyteller documents. The identity provider (IInvitationSender) only delivers the email,
// so an invitation can always be accepted by a signed-in user whose verified email matches.
public class CosmosInvitationService : IInvitationService
{
    private const string MainPartitionKey = "access";

    private readonly IContainerRepositoryProvider _repositoryProvider;
    private readonly IAccessService _accessService;
    private readonly IInvitationSender _sender;
    private readonly JsonSerializerOptions _serializerOptions;
    private readonly InvitationOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CosmosInvitationService> _logger;

    public CosmosInvitationService(
        IContainerRepositoryProvider repositoryProvider,
        IAccessService accessService,
        IInvitationSender sender,
        IOptions<JsonSerializerOptions> serializerOptions,
        IOptions<InvitationOptions> options,
        ILogger<CosmosInvitationService> logger,
        TimeProvider? timeProvider = null)
    {
        _repositoryProvider = repositoryProvider;
        _accessService = accessService;
        _sender = sender;
        _serializerOptions = serializerOptions.Value;
        _options = options.Value;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    private Container Container => _repositoryProvider.GetCore().Container;

    private DateTimeOffset Now => _timeProvider.GetUtcNow();

    public async Task<IReadOnlyList<Invitation>> GetInvitationsAsync(string accessPointKey, string actorId)
    {
        await EnsureAdministratorAsync(accessPointKey, actorId, AccountRole.Reader);

        var query = new QueryDefinition("SELECT * FROM c WHERE STARTSWITH(c.id, @prefix) AND c.AccessPointKey = @key")
            .WithParameter("@prefix", InvitationEntity.IdPrefix)
            .WithParameter("@key", accessPointKey);
        var now = Now;

        return (await QueryAsync(query))
            .Select(entity => entity.ToInvitation(now))
            .OrderByDescending(invitation => invitation.CreatedAt)
            .ToList();
    }

    public async Task<Invitation> CreateInvitationAsync(string accessPointKey, InvitationCreate model, string actorId, string? actorName)
    {
        var email = NormalizeEmail(model.Email)
            ?? throw new ArgumentException($"'{model.Email}' is not a valid email address.", nameof(model));

        if (model.Role == AccountRole.None)
        {
            throw new ArgumentException("The role None can't be offered.", nameof(model));
        }

        var days = model.ExpiresInDays ?? _options.DefaultExpiresInDays;

        if (days < 1 || days > _options.MaxExpiresInDays)
        {
            throw new ArgumentException($"An invitation expires in 1 to {_options.MaxExpiresInDays} days.", nameof(model));
        }

        await EnsureAdministratorAsync(accessPointKey, actorId, model.Role);

        var now = Now;
        var pending = await GetPendingAsync(email, accessPointKey);

        if (pending.Any(entity => entity.ExpiresAt > now))
        {
            throw new ConflictException($"A pending invitation for '{email}' to '{accessPointKey}' already exists.", ErrorCodes.InvitationExists);
        }

        var entity = new InvitationEntity
        {
            InvitationId = Guid.CreateVersion7().ToString("N"),
            AccessPointKey = accessPointKey,
            Email = email,
            Role = model.Role,
            Status = InvitationStatus.Pending,
            InvitedById = actorId,
            InvitedByName = actorName,
            CreatedAt = now,
            ExpiresAt = now.AddDays(days),
        };

        var externalId = await TrySendAsync(entity.ToInvitation(now));
        entity = entity with { ExternalInvitationId = externalId, IsEmailSent = externalId is not null };

        try
        {
            await Container.CreateItemAsync(entity, new PartitionKey(MainPartitionKey));
        }
        catch
        {
            await TryRevokeExternalAsync(externalId);
            throw;
        }

        return entity.ToInvitation(now);
    }

    public async Task<Invitation> ResendInvitationAsync(string accessPointKey, string invitationId, string actorId)
    {
        var (entity, eTag) = await ReadForPointAsync(accessPointKey, invitationId);
        await EnsureAdministratorAsync(accessPointKey, actorId, entity.Role);
        EnsurePending(entity, allowExpired: true);

        // WorkOS only resends pending invitations with their old expiry, so the old one is revoked and a new one sent.
        var now = Now;
        var days = Math.Clamp((int)Math.Ceiling((entity.ExpiresAt - entity.CreatedAt).TotalDays), 1, _options.MaxExpiresInDays);
        var renewed = entity with { ExpiresAt = now.AddDays(days) };

        await TryRevokeExternalAsync(entity.ExternalInvitationId);
        var externalId = await TrySendAsync(renewed.ToInvitation(now));
        renewed = renewed with { ExternalInvitationId = externalId, IsEmailSent = externalId is not null };

        await ReplaceAsync(renewed, eTag);
        return renewed.ToInvitation(now);
    }

    public async Task<Invitation> RevokeInvitationAsync(string accessPointKey, string invitationId, string actorId)
    {
        var (entity, eTag) = await ReadForPointAsync(accessPointKey, invitationId);
        await EnsureAdministratorAsync(accessPointKey, actorId, entity.Role);
        EnsurePending(entity, allowExpired: true);

        var now = Now;
        var revoked = entity with { Status = InvitationStatus.Revoked, RespondedAt = now };
        await ReplaceAsync(revoked, eTag);
        await TryRevokeExternalAsync(entity.ExternalInvitationId);
        return revoked.ToInvitation(now);
    }

    public async Task<IReadOnlyList<Invitation>> GetPendingInvitationsAsync(string email)
    {
        var normalized = NormalizeEmail(email);

        if (normalized is null)
        {
            return [];
        }

        var now = Now;
        return (await GetPendingAsync(normalized, accessPointKey: null))
            .Where(entity => entity.ExpiresAt > now)
            .Select(entity => entity.ToInvitation(now))
            .OrderBy(invitation => invitation.CreatedAt)
            .ToList();
    }

    public async Task<Account> AcceptInvitationAsync(string invitationId, InvitationIdentity invitee)
    {
        var (entity, eTag) = await ReadAsync(invitationId);
        EnsureInvitee(entity, invitee);

        if (entity.Status == InvitationStatus.Accepted
            && string.Equals(entity.AcceptedById, invitee.AccountId, StringComparison.Ordinal))
        {
            // Accepting twice is harmless: the first acceptance already granted the role.
            return await GetAccountAsync(invitee.AccountId);
        }

        EnsurePending(entity, allowExpired: false);
        await EnsureAccountAsync(invitee, entity.Email);

        // The role was authorized when the invitation was created, so the inviter's current role is not checked again.
        await _accessService.JoinAccessPointAsync(entity.AccessPointKey, invitee.AccountId, entity.Role);

        var accepted = entity with
        {
            Status = InvitationStatus.Accepted,
            AcceptedById = invitee.AccountId,
            RespondedAt = Now,
        };
        await ReplaceAsync(accepted, eTag);
        return await GetAccountAsync(invitee.AccountId);
    }

    public async Task<Invitation> DeclineInvitationAsync(string invitationId, InvitationIdentity invitee)
    {
        var (entity, eTag) = await ReadAsync(invitationId);
        EnsureInvitee(entity, invitee);
        EnsurePending(entity, allowExpired: true);

        var now = Now;
        var declined = entity with { Status = InvitationStatus.Declined, RespondedAt = now };
        await ReplaceAsync(declined, eTag);
        await TryRevokeExternalAsync(entity.ExternalInvitationId);
        return declined.ToInvitation(now);
    }

    // Lower-case, trimmed, and a single plain address. Null when it is not an email address.
    public static string? NormalizeEmail(string? email)
    {
        var trimmed = email?.Trim();

        return !string.IsNullOrEmpty(trimmed)
            && MailAddress.TryCreate(trimmed, out var address)
            && string.Equals(address.Address, trimmed, StringComparison.OrdinalIgnoreCase)
                ? trimmed.ToLowerInvariant()
                : null;
    }

    private static void EnsureInvitee(InvitationEntity entity, InvitationIdentity invitee)
    {
        if (!string.Equals(NormalizeEmail(invitee.Email), entity.Email, StringComparison.Ordinal))
        {
            throw new AccessDeniedException("The invitation was sent to a different email address.", ErrorCodes.EmailMismatch);
        }

        if (!invitee.IsEmailVerified)
        {
            throw new AccessDeniedException("The email address of the signed-in user is not verified.", ErrorCodes.EmailNotVerified);
        }
    }

    private void EnsurePending(InvitationEntity entity, bool allowExpired)
    {
        if (entity.Status != InvitationStatus.Pending)
        {
            throw new ConflictException($"The invitation is {entity.Status.ToString().ToLowerInvariant()}.", ErrorCodes.InvitationNotPending);
        }

        if (!allowExpired && entity.ExpiresAt <= Now)
        {
            throw new ConflictException("The invitation has expired, ask for a new one.", ErrorCodes.InvitationExpired);
        }
    }

    // Managing invitations needs Administrator; offering Owner needs Owner. The access point's map is the authority.
    private async Task EnsureAdministratorAsync(string accessPointKey, string actorId, AccountRole offeredRole)
    {
        var point = await _accessService.GetAccessPointAsync(accessPointKey)
            ?? throw new NotFoundException($"The access point '{accessPointKey}' doesn't exist.");
        var actorRole = point.AccessMap.GetValueOrDefault(actorId, AccountRole.None);

        if (actorRole < AccountRole.Administrator
            || (offeredRole == AccountRole.Owner && actorRole != AccountRole.Owner))
        {
            throw new AccessDeniedException($"The account '{actorId}' can't manage invitations with the role {offeredRole} on '{accessPointKey}'.");
        }
    }

    private async Task EnsureAccountAsync(InvitationIdentity invitee, string email)
    {
        if (await _accessService.GetAccountAsync(invitee.AccountId) is not null)
        {
            return;
        }

        try
        {
            await _accessService.CreateAccountAsync(new AccountCreate
            {
                IdentityId = invitee.AccountId,
                UserName = string.IsNullOrWhiteSpace(invitee.UserName) ? email : invitee.UserName,
                Name = string.IsNullOrWhiteSpace(invitee.Name) ? email : invitee.Name,
            });
        }
        catch (ConflictException)
        {
            // Created by a parallel request of the same user.
        }
    }

    private async Task<Account> GetAccountAsync(string accountId)
    {
        return await _accessService.GetAccountAsync(accountId)
            ?? throw new NotFoundException($"The account '{accountId}' doesn't exist.");
    }

    private async Task<(InvitationEntity Entity, string? ETag)> ReadAsync(string invitationId)
    {
        var (entity, eTag) = await Container.TryReadItemWithETagAsync(
            $"{InvitationEntity.IdPrefix}{invitationId}",
            new PartitionKey(MainPartitionKey),
            stream => stream.DeserializeSystemTextJson<InvitationEntity>(_serializerOptions));

        return entity is null
            ? throw new NotFoundException($"The invitation '{invitationId}' doesn't exist.")
            : (entity, eTag);
    }

    private async Task<(InvitationEntity Entity, string? ETag)> ReadForPointAsync(string accessPointKey, string invitationId)
    {
        var (entity, eTag) = await ReadAsync(invitationId);

        return string.Equals(entity.AccessPointKey, accessPointKey, StringComparison.Ordinal)
            ? (entity, eTag)
            : throw new NotFoundException($"The invitation '{invitationId}' doesn't exist in '{accessPointKey}'.");
    }

    private async Task ReplaceAsync(InvitationEntity entity, string? eTag)
    {
        try
        {
            await Container.ReplaceItemAsync(
                entity,
                entity.Id,
                new PartitionKey(MainPartitionKey),
                new ItemRequestOptions { IfMatchEtag = eTag });
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            throw new ConflictException("The invitation was changed concurrently, try again.", ErrorCodes.Conflict);
        }
    }

    private async Task<List<InvitationEntity>> GetPendingAsync(string email, string? accessPointKey)
    {
        var text = "SELECT * FROM c WHERE STARTSWITH(c.id, @prefix) AND c.Email = @email AND c.Status = @status";
        var query = new QueryDefinition(accessPointKey is null ? text : $"{text} AND c.AccessPointKey = @key")
            .WithParameter("@prefix", InvitationEntity.IdPrefix)
            .WithParameter("@email", email)
            .WithParameter("@status", nameof(InvitationStatus.Pending));

        if (accessPointKey is not null)
        {
            query = query.WithParameter("@key", accessPointKey);
        }

        return await QueryAsync(query);
    }

    private async Task<List<InvitationEntity>> QueryAsync(QueryDefinition query)
    {
        using var iterator = Container.GetItemQueryStreamIterator(
            query,
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(MainPartitionKey) });
        var result = new List<InvitationEntity>();

        while (iterator.HasMoreResults)
        {
            using var response = await iterator.ReadNextAsync();
            response.EnsureSuccessStatusCode();
            var page = await JsonSerializer.DeserializeAsync<QueryPage>(response.Content, _serializerOptions);
            result.AddRange(page?.Documents ?? []);
        }

        return result;
    }

    // The email is a courtesy: a failure is logged and the invitation is kept, so it can be accepted or resent.
    private async Task<string?> TrySendAsync(Invitation invitation)
    {
        try
        {
            return await _sender.SendAsync(invitation);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Sending the invitation {InvitationId} to {AccessPointKey} failed.", invitation.Id, invitation.AccessPointKey);
            return null;
        }
    }

    private async Task TryRevokeExternalAsync(string? externalInvitationId)
    {
        if (externalInvitationId is null)
        {
            return;
        }

        try
        {
            await _sender.RevokeAsync(externalInvitationId);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Revoking the external invitation {ExternalInvitationId} failed.", externalInvitationId);
        }
    }

    private sealed record QueryPage(List<InvitationEntity>? Documents);
}

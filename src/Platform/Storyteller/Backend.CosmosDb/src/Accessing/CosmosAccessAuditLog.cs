using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Entities.Access;
using _42.Platform.Storyteller.Entities.KeyValues;
using Microsoft.Azure.Cosmos;

namespace _42.Platform.Storyteller.Accessing;

// The access audit trail of an organization, read where its records are written: core "access" (memberships, access
// points, policies) and the organization's partitions of organizations (machines, shared certificates, trusted issuers).
// Keyset paging on the id, whose order is the time order, merges both sources without skipping or repeating a record:
// a page is the newest records below the last id of the page before, and the token carries only that id.
public partial class CosmosAccessAuditLog : IAccessAuditLog
{
    private const string TokenVersion = "1";
    private const int MaxTokenIdLength = 128;
    private const string InvalidTokenMessage = "The continuation token is not one of the access audit; start again without it.";

    private readonly IContainerRepositoryProvider _repositoryProvider;

    public CosmosAccessAuditLog(IContainerRepositoryProvider repositoryProvider)
    {
        _repositoryProvider = repositoryProvider;
    }

    public async Task<AccessAuditResponse> QueryAsync(string organization, AccessAuditQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(organization);
        ArgumentNullException.ThrowIfNull(query);

        var pageSize = Math.Clamp(query.PageSize, 1, AccessAuditQuery.MaxPageSize);
        var before = DecodeToken(query.ContinuationToken);

        // One more than a page from each source tells whether another page follows.
        var take = pageSize + 1;
        var definition = BuildQuery(organization, query, before, take);

        var core = _repositoryProvider.GetCore();
        var coreRecords = ReadAsync(
            core.Container,
            definition,
            new QueryRequestOptions { PartitionKey = new PartitionKey(AccessPointEntity.Partition), MaxItemCount = take },
            take,
            cancellationToken);

        // The organization as the key prefix routes the query to its physical partitions; the predicate on
        // c.Organization isolates it.
        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var organizationRecords = ReadAsync(
            repository.Container,
            definition,
            repository.QueryOptions(maxItemCount: take),
            take,
            cancellationToken);

        var merged = (await coreRecords)
            .Concat(await organizationRecords)
            .OrderByDescending(record => record.Id, StringComparer.Ordinal)
            .Take(take)
            .ToList();
        var page = merged.Take(pageSize).ToList();

        return new AccessAuditResponse
        {
            Items = page.Select(record => record.ToEntry()).ToList(),
            ContinuationToken = merged.Count > pageSize ? EncodeToken(page[^1].Id) : null,
            Count = page.Count,
        };
    }

    public static string EncodeToken(string lastId)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes($"{TokenVersion}:{lastId}"))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    // The last id of the page before, or null for the first page. InvalidQuery for anything this log did not issue.
    public static string? DecodeToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        string text;

        try
        {
            var base64 = token.Trim().Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');
            text = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        }
        catch (FormatException)
        {
            throw new InvalidInputException(InvalidTokenMessage, ErrorCodes.InvalidQuery);
        }

        var separator = text.IndexOf(':', StringComparison.Ordinal);

        if (separator < 0
            || !string.Equals(text[..separator], TokenVersion, StringComparison.Ordinal)
            || text.Length - separator - 1 > MaxTokenIdLength
            || !RecordId().IsMatch(text[(separator + 1)..]))
        {
            throw new InvalidInputException(InvalidTokenMessage, ErrorCodes.InvalidQuery);
        }

        return text[(separator + 1)..];
    }

    private static QueryDefinition BuildQuery(string organization, AccessAuditQuery query, string? before, int take)
    {
        var text = new StringBuilder("SELECT TOP @take * FROM c WHERE STARTSWITH(c.id, @prefix) AND c.Organization = @organization");

        if (!string.IsNullOrWhiteSpace(query.Project))
        {
            text.Append(" AND c.Project = @project");
        }

        if (query.Action is not null)
        {
            text.Append(" AND c.Action = @action");
        }

        if (!string.IsNullOrWhiteSpace(query.Target))
        {
            text.Append(" AND c.Target.Id = @target");
        }

        if (query.From is not null)
        {
            text.Append(" AND c.Time >= @from");
        }

        if (query.To is not null)
        {
            text.Append(" AND c.Time <= @to");
        }

        if (before is not null)
        {
            text.Append(" AND c.id < @before");
        }

        text.Append(" ORDER BY c.id DESC");

        return new QueryDefinition(text.ToString())
            .WithParameter("@take", take)
            .WithParameter("@prefix", AccessAuditEntity.IdPrefix)
            .WithParameter(OrganizationContainer.OrganizationParameter, organization)
            .WithParameter("@project", query.Project?.Trim())
            .WithParameter("@action", query.Action?.ToString())
            .WithParameter("@target", query.Target?.Trim())
            .WithParameter("@from", query.From is null ? null : KeyValueAuditEntity.FormatTime(query.From.Value))
            .WithParameter("@to", query.To is null ? null : KeyValueAuditEntity.FormatTime(query.To.Value))
            .WithParameter("@before", before);
    }

    private static async Task<List<AccessAuditEntity>> ReadAsync(
        Container container,
        QueryDefinition definition,
        QueryRequestOptions options,
        int take,
        CancellationToken cancellationToken)
    {
        var records = new List<AccessAuditEntity>();

        try
        {
            using var iterator = container.GetItemQueryIterator<AccessAuditEntity>(definition, requestOptions: options);

            while (iterator.HasMoreResults && records.Count < take)
            {
                records.AddRange(await iterator.ReadNextAsync(cancellationToken));
            }
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            // No container organizations yet: no records of machines, issuers or certificates.
            return records;
        }

        return records;
    }

    [GeneratedRegex(@"^aca\.[0-9]{21}\.[0-9a-f]{32}\z", RegexOptions.CultureInvariant)]
    private static partial Regex RecordId();
}

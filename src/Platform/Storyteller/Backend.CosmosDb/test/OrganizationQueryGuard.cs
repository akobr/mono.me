#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// In the pipeline of the suite's Cosmos client: refuses every query on the container "organizations" that does not name
// one full partition (organization and partition) unless its text filters on the organization, as the SQL queries write it
// ("c.Organization = @organization" with the parameter) or as LINQ translates ".Where(r => r.Organization == organization)"
// (root["Organization"] = "..."); a ReadMany over several partitions passes as the SDK's query with the full key of every
// item. The prefix key alone keeps other organizations out on the emulators, so without this
// guard no test fails when a query forgets the predicate.
public sealed partial class OrganizationQueryGuard : RequestHandler
{
    public const string PartitionKeyHeader = "x-ms-documentdb-partitionkey";

    private static readonly AsyncLocal<bool> CrossOrganization = new();

    // A test that looks over every organization on purpose (never the code under test) runs its query in this scope.
    public static IDisposable AllowCrossOrganization()
    {
        CrossOrganization.Value = true;
        return new Scope();
    }

    public override async Task<ResponseMessage> SendAsync(RequestMessage request, CancellationToken cancellationToken)
    {
        if (!CrossOrganization.Value && IsQueryOnOrganizations(request) && !NamesFullPartition(request))
        {
            var (text, organization) = await ReadQueryAsync(request, cancellationToken);
            if (!FiltersOnOrganization(text, organization))
            {
                throw new InvalidOperationException(
                    $"An organization-wide query on '{CosmosConstants.OrganizationsContainerName}' without the predicate "
                    + $"c.Organization = {OrganizationContainer.OrganizationParameter}: {text}");
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }

    public static bool FiltersOnOrganization(string text, string? organizationParameter)
    {
        if (SqlPredicate().IsMatch(text))
        {
            return !string.IsNullOrWhiteSpace(organizationParameter);
        }

        // ReadMany over several partitions: the SDK writes the full key of every item (c.id, c["Organization"], c["PartitionKey"])
        var readManyItems = ReadManyItem().Count(text);
        if (readManyItems > 0)
        {
            return ReadManyOrganization().Count(text) == readManyItems;
        }

        return LinqPredicate().Match(text) is { Success: true } match
            && !string.IsNullOrWhiteSpace(match.Groups["organization"].Value);
    }

    private static bool IsQueryOnOrganizations(RequestMessage request)
    {
        // "dbs/{database}/colls/organizations" at this point of the pipeline (an organization-wide query carries the range
        // of the prefix in x-ms-start-epk and x-ms-end-epk, not a partition key header)
        var uri = (request.RequestUri?.OriginalString ?? string.Empty).TrimEnd('/');
        var isQuery = request.Headers.TryGetValue("x-ms-documentdb-isquery", out var flag)
            && string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase);
        var container = $"/colls/{CosmosConstants.OrganizationsContainerName}";

        return isQuery
            && (uri.EndsWith(container, StringComparison.Ordinal) || uri.EndsWith($"{container}/docs", StringComparison.Ordinal));
    }

    // A full key (organization, partition) routes the query to one logical partition of one organization.
    private static bool NamesFullPartition(RequestMessage request)
    {
        if (!request.Headers.TryGetValue(PartitionKeyHeader, out var header) || string.IsNullOrEmpty(header))
        {
            return false;
        }

        using var key = JsonDocument.Parse(header);
        return key.RootElement.ValueKind == JsonValueKind.Array
            && key.RootElement.GetArrayLength() == ContainerFactory.OrganizationsPartitionKeyPaths.Count;
    }

    private static async Task<(string Text, string? Organization)> ReadQueryAsync(RequestMessage request, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await request.Content.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();

        // the request sends its content after this handler: rewind it, or give it a copy when it cannot rewind
        if (request.Content.CanSeek)
        {
            request.Content.Position = 0;
        }
        else
        {
            request.Content = new MemoryStream(bytes);
        }

        using var body = JsonDocument.Parse(bytes);
        var text = body.RootElement.GetProperty("query").GetString() ?? string.Empty;
        string? organization = null;

        if (body.RootElement.TryGetProperty("parameters", out var parameters))
        {
            organization = parameters.EnumerateArray()
                .Where(parameter => parameter.GetProperty("name").GetString() == OrganizationContainer.OrganizationParameter)
                .Select(parameter => parameter.GetProperty("value").GetString())
                .FirstOrDefault();
        }

        return (text, organization);
    }

    [GeneratedRegex(@"\bc\.Organization\s*=\s*@organization\b")]
    private static partial Regex SqlPredicate();

    [GeneratedRegex(@"root\[""Organization""\]\s*=\s*""(?<organization>[^""]*)""")]
    private static partial Regex LinqPredicate();

    [GeneratedRegex(@"\bc\.id\s*=\s*@param_id\d+")]
    private static partial Regex ReadManyItem();

    [GeneratedRegex(@"\bc\[""Organization""\]\s*=\s*@param_pk\d+")]
    private static partial Regex ReadManyOrganization();

    private sealed class Scope : IDisposable
    {
        public void Dispose()
        {
            CrossOrganization.Value = false;
        }
    }
}

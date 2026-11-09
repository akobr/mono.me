using System;
using System.Collections.Generic;

namespace _42.Platform.Storyteller.DbCreator.Logic;

// The database holds containers of the layout before the shared container "organizations" (org.{organization}), or an
// "organizations" with other partition key paths. Thrown before anything is written; the data is not migrated, the
// database is seeded again.
public sealed class DatabaseLayoutException : Exception
{
    public DatabaseLayoutException(IReadOnlyList<string> problems)
        : base($"The database '{CosmosConstants.DatabaseName}' does not have the layout of the shared container '{CosmosConstants.OrganizationsContainerName}': {string.Join(" ", problems)}")
    {
        Problems = problems;
    }

    public IReadOnlyList<string> Problems { get; }
}

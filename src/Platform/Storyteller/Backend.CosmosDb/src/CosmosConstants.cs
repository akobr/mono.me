namespace _42.Platform.Storyteller;

public class CosmosConstants
{
    public const string DatabaseName = "42.Platform.2S";
    public const string CoreContainerName = "core";

    // The shared container of every organization's data, keyed by /Organization and then /PartitionKey.
    public const string OrganizationsContainerName = "organizations";

    // The prefix of the per-organization containers (org.{organization}) of the layout before the shared container;
    // DbCreator refuses a database that still has them.
    public const string LegacyOrganizationContainerNamePrefix = "org.";

    public const int MaxItemCountPerPage = 1000;
}

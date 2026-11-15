namespace _42.Platform.Storyteller.Api.Exhibitions;

// Settings of the exhibition, the demo project of the deployment that a newcomer may join as a Reader (section
// Exhibition). All three are set, or none: without them the deployment has no exhibition.
public class ExhibitionOptions
{
    public const string SectionName = "Exhibition";

    // The organization of the exhibition project.
    public string? Organization { get; set; }

    // The exhibition project.
    public string? Project { get; set; }

    // The id of the account that owns the exhibition project: the --owner of its seed. The project is offered and joined
    // only while this account is its Owner and no other account is its Administrator or Owner.
    public string? Owner { get; set; }

    // All three are set: the deployment has an exhibition.
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Organization)
        && !string.IsNullOrWhiteSpace(Project)
        && !string.IsNullOrWhiteSpace(Owner);

    // None is set, or all three are, both names are valid names and the owner has no surrounding white space. All
    // three are compared as written: a valid name is lower-case, and no account id has white space around it (a
    // registration and the seed trim it), so a padded owner would start and never match.
    public bool IsValid => IsConfigured
        ? NameRules.IsValidName(Organization) && NameRules.IsValidName(Project) && Owner!.Trim().Length == Owner.Length
        : string.IsNullOrWhiteSpace(Organization) && string.IsNullOrWhiteSpace(Project) && string.IsNullOrWhiteSpace(Owner);

    // "organization.project"; only for a configured exhibition.
    public string ProjectKey => $"{Organization}.{Project}";
}

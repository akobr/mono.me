namespace _42.Platform.Storyteller.DbCreator.DataSets;

// What the seed created for its target (in a dry run: would create). An existing project is used as it is.
public sealed record class DataSetProvisioning(
    bool IsAccountCreated,
    bool IsOrganizationCreated,
    bool IsProjectCreated,
    bool IsViewRegistered);

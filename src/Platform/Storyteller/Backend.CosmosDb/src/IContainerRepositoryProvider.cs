namespace _42.Platform.Storyteller;

public interface IContainerRepositoryProvider
{
    IContainerRepository GetCore();

    // The data of one organization in the shared container "organizations".
    OrganizationContainer GetOrganizationContainer(string organizationName);
}

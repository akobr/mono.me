namespace _42.Platform.Storyteller;

public interface IContainerRepositoryProvider
{
    IContainerRepository GetCore();

    IContainerRepository GetOrganizationContainer(string organizationName);
}

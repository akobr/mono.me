namespace _42.Platform.Storyteller;

public interface IContainerRepositoryProvider
{
    IContainerRepository GetCore();

    IContainerRepository Get(string containerName);
}

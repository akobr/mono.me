namespace _42.Crumble;

public interface IVolumeClientFactory
{
    IVolumeClient CreateClient(ICrumbExecutionContext crumbContext);

    IVolumeClient<TVolumeContext> CreateClient<TVolumeContext>(ICrumbExecutionContext crumbContext)
        where TVolumeContext : IVolumeContext;
}

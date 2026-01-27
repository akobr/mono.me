namespace _42.Crumble;

public class NullVolumeClientFactory : IVolumeClientFactory
{
    public IVolumeClient CreateClient(ICrumbExecutionContext crumbContext)
    {
        return new NullVolumeClient();
    }

    public IVolumeClient<TVolumeContext> CreateClient<TVolumeContext>(ICrumbExecutionContext crumbContext)
        where TVolumeContext : IVolumeContext
    {
        return new NullVolumeClient<TVolumeContext>();
    }
}

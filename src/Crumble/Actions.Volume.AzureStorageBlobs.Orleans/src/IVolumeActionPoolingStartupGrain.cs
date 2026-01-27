namespace _42.Crumble;

public interface IVolumeActionPoolingStartupGrain : IGrainWithStringKey
{
    Task StartPooling();
}

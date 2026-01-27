namespace _42.Crumble;

public interface IVolumeActionPoolingGrain : IGrainWithStringKey
{
    Task StartPooling();
}

namespace _42.Crumble;

public interface IMessageActionPoolingStartupGrain : IGrainWithStringKey
{
    Task StartPooling();
}

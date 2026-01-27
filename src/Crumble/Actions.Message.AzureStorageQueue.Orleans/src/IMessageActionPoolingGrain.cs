namespace _42.Crumble;

public interface IMessageActionPoolingGrain : IGrainWithStringKey
{
    Task StartPooling();
}

using Orleans;

namespace _42.Crumble;

public interface ICrumbGrain : IGrainWithStringKey
{
    Task ExecuteCrumb();
}

public interface ICrumbGrain<in TInput> : IGrainWithStringKey
{
    Task ExecuteCrumb(TInput input);
}

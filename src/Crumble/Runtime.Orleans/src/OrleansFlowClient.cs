using Orleans;

namespace _42.Crumble;

public class OrleansFlowClient(IGrainFactory factory, ICrumbToGrainProvider register) : IFlowClient
{
    public Task ExecuteCrumbAsync(Delegate crumb)
    {
        ArgumentNullException.ThrowIfNull(crumb);
        var crumbKey = crumb.Method.GetCrumbKey();
        return ExecuteCrumbAsync(crumbKey);
    }

    public Task ExecuteCrumbAsync(string crumbKey)
    {
        var type = register.GetCrumbTypeByKey(crumbKey);
        var grain = (ICrumbGrain)factory.GetGrain(type, crumbKey);
        return grain.ExecuteCrumb();
    }

    public Task<TOutput> ExecuteCrumbAsync<TOutput>(Delegate crumb)
    {
        ArgumentNullException.ThrowIfNull(crumb);
        var crumbKey = crumb.Method.GetCrumbKey();
        return ExecuteCrumbAsync<TOutput>(crumbKey);
    }

    public Task<TOutput> ExecuteCrumbAsync<TOutput>(string crumbKey)
    {
        var type = register.GetCrumbTypeByKey(crumbKey);
        var grain = (ICrumbGrainWithOutput<TOutput>)factory.GetGrain(type, crumbKey);
        return grain.ExecuteCrumb();
    }

    public Task<TOutput> ExecuteCrumbAsync<TInput, TOutput>(Delegate crumb, TInput input)
    {
        ArgumentNullException.ThrowIfNull(crumb);
        var crumbKey = crumb.Method.GetCrumbKey();
        return ExecuteCrumbAsync<TInput, TOutput>(crumbKey, input);
    }

    public Task<TOutput> ExecuteCrumbAsync<TInput, TOutput>(string crumbKey, TInput input)
    {
        var type = register.GetCrumbTypeByKey(crumbKey);
        var grain = (ICrumbGrainWithOutput<TOutput, TInput>)factory.GetGrain(type, crumbKey);
        return grain.ExecuteCrumb(input);
    }

    public string StartCrumb(Delegate crumb)
    {
        ArgumentNullException.ThrowIfNull(crumb);
        var crumbKey = crumb.Method.GetCrumbKey();
        return StartCrumb(crumbKey);
    }

    public string StartCrumb(string crumbKey)
    {
        var type = register.GetCrumbTypeByKey(crumbKey);
        var grain = (ICrumbGrain)factory.GetGrain(type, crumbKey);
        grain.ExecuteCrumb();
        return Guid.CreateVersion7().ToString("N");
    }

    public string StartCrumb<TInput>(Delegate crumb, TInput input)
    {
        ArgumentNullException.ThrowIfNull(crumb);
        var crumbKey = crumb.Method.GetCrumbKey();
        return StartCrumb(crumbKey, input);
    }

    public string StartCrumb<TInput>(string crumbKey, TInput input)
    {
        var type = register.GetCrumbTypeByKey(crumbKey);
        var grain = (ICrumbGrain<TInput>)factory.GetGrain(type, crumbKey);
        grain.ExecuteCrumb(input);
        return Guid.CreateVersion7().ToString("N");
    }
}

using Orleans;

namespace _42.Crumble;

public class OrleansFlowClient(IGrainFactory factory, ICrumbToGrainProvider register) : IFlowClient
{
    public Task ExecuteCrumbAsync(Delegate crumb)
    {
        ArgumentNullException.ThrowIfNull(crumb);

        if (crumb.Method is { DeclaringType: null })
        {
            throw new ArgumentException(
                "The delegate must be a valid method which represent a crumb, has [Crumb] attribute.",
                nameof(crumb));
        }

        var crumbKey = $"{crumb.Method.DeclaringType!.FullName}.{crumb.Method.Name}";
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
        throw new NotImplementedException();
    }

    public Task<TOutput> ExecuteCrumbAsync<TOutput>(string crumbKey)
    {
        var type = register.GetCrumbTypeByKey(crumbKey);
        var grain = (ICrumbGrainWithOutput<TOutput>)factory.GetGrain(type, crumbKey);
        return grain.ExecuteCrumb();
    }

    public Task<TOutput> ExecuteCrumbAsync<TInput, TOutput>(Delegate crumb, TInput input)
    {
        throw new NotImplementedException();
    }

    public Task<TOutput> ExecuteCrumbAsync<TInput, TOutput>(string crumbKey, TInput input)
    {
        var type = register.GetCrumbTypeByKey(crumbKey);
        var grain = (ICrumbGrainWithOutput<TOutput, TInput>)factory.GetGrain(type, crumbKey);
        return grain.ExecuteCrumb(input);
    }

    public string StartCrumb(Delegate crumb)
    {
        throw new NotImplementedException();
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
        throw new NotImplementedException();
    }

    public string StartCrumb<TInput>(string crumbKey, TInput input)
    {
        var type = register.GetCrumbTypeByKey(crumbKey);
        var grain = (ICrumbGrain<TInput>)factory.GetGrain(type, crumbKey);
        grain.ExecuteCrumb(input);
        return Guid.CreateVersion7().ToString("N");
    }
}

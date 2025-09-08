using System.Linq.Expressions;

namespace _42.Crumble;

public interface IFlowClient
{
    Task ExecuteCrumbAsync(Delegate crumb);

    // Task ExecuteCrumbAsync(Expression crumb);

    Task ExecuteCrumbAsync(string crumbKey);

    Task<TOutput> ExecuteCrumbAsync<TOutput>(Delegate crumb);

    Task<TOutput> ExecuteCrumbAsync<TOutput>(string crumbKey);

    Task<TOutput> ExecuteCrumbAsync<TInput, TOutput>(Delegate crumb, TInput input);

    Task<TOutput> ExecuteCrumbAsync<TInput, TOutput>(string crumbKey, TInput input);

    string StartCrumb(Delegate crumb);

    string StartCrumb(string crumbKey);

    string StartCrumb<TInput>(Delegate crumb, TInput input);

    string StartCrumb<TInput>(string crumbKey, TInput input);
}

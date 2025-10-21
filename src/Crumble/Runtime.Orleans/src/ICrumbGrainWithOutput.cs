namespace _42.Crumble;

public interface ICrumbGrainWithOutput<TOutput> : ICrumbGrain
{
    Task<TOutput> ExecuteCrumb();
}

public interface ICrumbGrainWithOutput<TOutput, in TInput> : ICrumbGrain<TInput>
{
    Task<TOutput> ExecuteCrumb(TInput input);
}

namespace _42.Crumble.Playground.Examples.Implementation;

public interface IFirstCrumbsExecutor
{
    Task HelloWorld();

    Task<string> HelloWorldWithOutput();

    Task HelloWorldWithInput(string input);

    Task<string> HelloWorldWithInputAndOutput(string input);
}

public class FirstCrumbsExecutor(IGrainFactory grainFactory) : IFirstCrumbsExecutor
{
    public Task HelloWorld()
    {
        //var grain = grainFactory.GetGrain<IFirstCrumbsHelloWorldGrain>("default");
        //return grain.ExecuteCrumb();
        throw new NotImplementedException();
    }

    public Task<string> HelloWorldWithOutput()
    {
        throw new NotImplementedException();
    }

    public Task HelloWorldWithInput(string input)
    {
        throw new NotImplementedException();
    }

    public Task<string> HelloWorldWithInputAndOutput(string input)
    {
        throw new NotImplementedException();
    }
}



namespace _42.Crumble.Playground.Examples;

[Crumbler]
public class ExampleCrumbler
{
    public List<InputData> GetInputData()
    {
        return new List<InputData>()
        {
            new InputData { Key = "Name", value = "World" },
            new InputData { Key = "Greeting", value = "Hello" }
        };
    }

    public string GenerateGreeting(List<InputData> inputData)
    {
        var name = inputData.FirstOrDefault(x => x.Key == "Name")?.value ?? "World";
        var greeting = inputData.FirstOrDefault(x => x.Key == "Greeting")?.value ?? "Hello";
        return $"{greeting}, {name}!";
    }

    public async Task Orchestrate(IFlowClient flow)
    {
        var data = await flow.ExecuteCrumbAsync<List<InputData>>(GetInputData);
        await flow.ExecuteCrumbAsync<List<InputData>, string>(GenerateGreeting, data);
    }

    public class InputData
    {
        public string Key { get; set; }
        public string value { get; set; }
    }
}

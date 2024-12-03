using System.Diagnostics;

namespace _42.nHolistic.Examples;

public class ExampleOfMethodTests(ExampleTestSteps steps)
{
    [Test]
    public async Task Test1(IExampleModel model)
    {
        await steps.FirstStep(model);
        await steps.SecondStep(model);
    }

    [Test]
    public void Test2([FromModel(JQuery = "$.Number")]int number)
    {
        Debug.WriteLine($"Test2: {number}");
        AnotherMethodWithSteps();
    }

    [Test]
    public void Test3(string input)
    {
        Debug.WriteLine($"Test3: {input}");
    }

    [Test]
    public void Test4()
    {
        Debug.WriteLine("Test4 with zero parameters.");
    }

    [Test]
    public void Test5([FromContainer]IExampleService service)
    {
        Debug.WriteLine("Test5 with parameter from container.");
    }

    [Test(Labels = ["CustomFixture"])]
    public void Test6([FromFixture]ExampleLabelFixture fixture)
    {
        Debug.WriteLine("Test6 with parameter from fixture.");
    }

    [Test]
    public void Test7(
        [FromContainer]IExampleService service,
        [FromFixture]ExampleLabelFixture fixture,
        IExampleModel model)
    {
        Debug.WriteLine("Test7 with multiple parameters.");
    }

    [Step]
    private void ExampleStep()
    {
        Debug.WriteLine("MessageFromExampleStep");
    }

    [Step]
    private void AnotherStep()
    {
        Debug.WriteLine("MessageFromAnotherStep");
    }

    private void AnotherMethodWithSteps()
    {
        ExampleStep();
        FakeWrapper();
    }

    private void FakeWrapper()
    {
        AnotherStep();
    }
}

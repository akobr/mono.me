using System.Diagnostics;
using _42.tHolistic;
using _42.tHolistic.Examples;

namespace _42.nHolistic.Examples;

[Test(
    ExternalId = "42.2057",
    Priority = 100,
    Labels = [Labels.Automation, "feature1", "app1", "subscriptionKey", "CustomFixture"])]
public class ExampleClassTest(
    [FromModel]IExampleModel model,
    [FromFixture]ExampleLabelFixture customFixture,
    [FromContainer]ITestRunContext context)
{
    public void Send_2()
    {
        Debug.WriteLine("Send");
    }

    public void Validate3()
    {
        Debug.WriteLine($"Validate: {model.Text}");
    }

    public void _1Fill()
    {
        Debug.WriteLine("Fill");
    }
}

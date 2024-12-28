using System.Diagnostics;
using Xunit;

namespace tHolistic.Tests;

public class TheoryTests
{
    [InlineData("text")]
    [InlineData("hello")]
    [Theory]
    public void Test1(string text)
    {
        Debug.WriteLine(text);
    }

    [MemberData(nameof(DataField), DisableDiscoveryEnumeration = false)]
    [Theory]
    public void Test2(Model model)
    {
        Debug.WriteLine(model.Text);
    }

    public static object[][] DataField = new object[][]
    {
        new object[] { new Model { Text = "text" } },
        new object[] { new Model { Text = "hello" } },
    };
}

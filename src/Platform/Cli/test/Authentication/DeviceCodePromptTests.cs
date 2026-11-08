using System;
using System.Text.Json;
using _42.Platform.Cli.Authentication;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Authentication;

// The JSON line of SFORM_DEVICE_CODE_OUTPUT=json: exactly these four camelCase names, in this order, on one line.
public class DeviceCodePromptTests
{
    [Fact]
    public void ToJsonLine_HasTheFourNames()
    {
        var prompt = new DeviceCodePrompt(
            "BCDF-GHJK",
            new Uri("https://auth.example.com/device"),
            new Uri("https://auth.example.com/device?user_code=BCDF-GHJK&x=1+2"),
            TimeSpan.FromSeconds(299.6));

        prompt.ToJsonLine().ShouldBe(
            """{"userCode":"BCDF-GHJK","verificationUri":"https://auth.example.com/device","verificationUriComplete":"https://auth.example.com/device?user_code=BCDF-GHJK&x=1+2","expiresIn":300}""");
    }

    [Fact]
    public void ToJsonLine_WithoutTheCompleteAddress_HasNull()
    {
        var prompt = new DeviceCodePrompt("ABC", new Uri("https://microsoft.com/devicelogin"), null, TimeSpan.FromMinutes(15));

        using var document = JsonDocument.Parse(prompt.ToJsonLine());

        document.RootElement.GetProperty("verificationUriComplete").ValueKind.ShouldBe(JsonValueKind.Null);
        document.RootElement.GetProperty("expiresIn").GetInt32().ShouldBe(900);
    }

    [Fact]
    public void ToJsonLine_OfAnExpiredCode_IsZeroSeconds()
    {
        var prompt = new DeviceCodePrompt("ABC", new Uri("https://auth.example.com/device"), null, TimeSpan.FromSeconds(-3));

        prompt.ToJsonLine().ShouldEndWith("\"expiresIn\":0}");
    }
}

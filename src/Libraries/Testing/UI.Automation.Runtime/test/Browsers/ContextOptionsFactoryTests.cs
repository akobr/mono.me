using _42.Testing.UI.Automation.Runtime.Browsers;
using _42.Testing.UI.Automation.Runtime.Settings;
using Microsoft.Playwright;

namespace _42.Testing.UI.Automation.Runtime.UnitTests.Browsers;

public sealed class ContextOptionsFactoryTests
{
    private static readonly DeviceCatalog Devices = new(new Dictionary<string, BrowserNewContextOptions>
    {
        ["Pixel 7"] = new()
        {
            UserAgent = "Mozilla/5.0 (Linux; Android 14; Pixel 7)",
            ViewportSize = new ViewportSize { Width = 412, Height = 839 },
            DeviceScaleFactor = 2.625f,
            IsMobile = true,
            HasTouch = true,
        },
        ["Pixel 7 landscape"] = new() { ViewportSize = new ViewportSize { Width = 863, Height = 360 }, IsMobile = true, HasTouch = true },
        ["Desktop Chrome"] = new() { ViewportSize = new ViewportSize { Width = 1280, Height = 720 } },
    });

    [Fact]
    public void Create_ForADesktop_UsesTheViewportAndTheTarget()
    {
        var settings = Settings("https://admin.example.test/app");
        settings.Browser.Viewport = new ViewportSettings { Width = 1600, Height = 1000 };
        settings.Browser.Locale = "en-GB";
        settings.Browser.TimeZone = "Europe/Prague";
        settings.Browser.ColorScheme = ColorScheme.Dark;

        var factory = new ContextOptionsFactory(settings, Devices);
        var options = factory.Create();

        options.ViewportSize.ShouldNotBeNull().Width.ShouldBe(1600);
        options.ViewportSize.Height.ShouldBe(1000);
        options.IsMobile.ShouldBeNull();
        options.BaseURL.ShouldBe("https://admin.example.test/app/");
        options.Locale.ShouldBe("en-GB");
        options.TimezoneId.ShouldBe("Europe/Prague");
        options.ColorScheme.ShouldBe(ColorScheme.Dark);
        options.IgnoreHTTPSErrors.ShouldBeNull();
        options.StorageState.ShouldBeNull();
        options.RecordVideoDir.ShouldBeNull();
        factory.Device.ShouldBeNull();
        factory.IsMobile.ShouldBeFalse();
    }

    [Fact]
    public void Create_ForADevice_UsesTheDescriptor_OverTheViewport()
    {
        var settings = Settings("https://admin.example.test/");
        settings.Browser.Device = "pixel 7";

        var factory = new ContextOptionsFactory(settings, Devices);
        var options = factory.Create();

        options.ViewportSize.ShouldNotBeNull().Width.ShouldBe(412);
        options.UserAgent.ShouldBe("Mozilla/5.0 (Linux; Android 14; Pixel 7)");
        options.IsMobile.ShouldBe(true);
        options.HasTouch.ShouldBe(true);
        options.DeviceScaleFactor.ShouldBe(2.625f);
        options.BaseURL.ShouldBe("https://admin.example.test/");
        options.Locale.ShouldBe("en-US");
        options.TimezoneId.ShouldBe("UTC");
        factory.Device.ShouldBe("pixel 7");
        factory.IsMobile.ShouldBeTrue();
        factory.Viewport.ShouldNotBeNull().Width.ShouldBe(412);
    }

    [Fact]
    public void Create_DoesNotChangeTheDescriptorOfTheCatalog()
    {
        var settings = Settings("https://admin.example.test");
        settings.Browser.Device = "Pixel 7";
        settings.Browser.Engine = BrowserEngine.Firefox;

        _ = new ContextOptionsFactory(settings, Devices).Create();

        Devices.TryGet("Pixel 7", out var descriptor).ShouldBeTrue();
        descriptor!.IsMobile.ShouldBe(true);
        descriptor.Locale.ShouldBeNull();
    }

    [Fact]
    public void Create_InFirefox_DropsIsMobile_WithAWarning()
    {
        var settings = Settings("https://admin.example.test");
        settings.Browser.Device = "Pixel 7";
        settings.Browser.Engine = BrowserEngine.Firefox;

        var factory = new ContextOptionsFactory(settings, Devices);
        var options = factory.Create();

        options.IsMobile.ShouldBeNull();
        options.HasTouch.ShouldBe(true);
        factory.Warnings.ShouldHaveSingleItem().ShouldStartWith("Firefox does not emulate isMobile;");
    }

    [Fact]
    public void Constructor_RejectsAnUnknownDevice_NamingSimilarOnes()
    {
        var settings = Settings("https://admin.example.test");
        settings.Browser.Device = "Pixel 99";

        var exception = Should.Throw<UiAutomationSettingsException>(() => new ContextOptionsFactory(settings, Devices));

        exception.Message.ShouldBe("Browser:Device 'Pixel 99' is not a Playwright device descriptor; similar: Pixel 7, Pixel 7 landscape.");
    }

    [Theory]
    [InlineData("http://localhost:5174", true)]
    [InlineData("https://127.0.0.1:8443", true)]
    [InlineData("https://[::1]:8443", true)]
    [InlineData("https://admin.localhost", true)]
    [InlineData("https://admin.example.test", false)]
    public void Create_IgnoresHttpsErrors_ForLocalhostOnly(string baseUrl, bool ignored)
    {
        var options = new ContextOptionsFactory(Settings(baseUrl), Devices).Create();

        options.IgnoreHTTPSErrors.ShouldBe(ignored ? true : null);
    }

    [Fact]
    public void Create_AddsTheStorageStateAndTheVideo_PerContext()
    {
        var factory = new ContextOptionsFactory(Settings("https://admin.example.test"), Devices);

        var signedIn = factory.Create("{\"cookies\":[],\"origins\":[]}", "artifacts/.video");
        var plain = factory.Create();

        signedIn.StorageState.ShouldBe("{\"cookies\":[],\"origins\":[]}");
        signedIn.RecordVideoDir.ShouldBe("artifacts/.video");
        signedIn.RecordVideoSize.ShouldNotBeNull().Width.ShouldBe(1440);
        plain.StorageState.ShouldBeNull();
        plain.RecordVideoDir.ShouldBeNull();
    }

    [Fact]
    public void Create_KeepsTheBrowsersOwnSettings_WhenTheyAreUnset()
    {
        var settings = Settings("https://admin.example.test");
        settings.Browser.Locale = null;
        settings.Browser.TimeZone = null;
        settings.Browser.ColorScheme = null;

        var options = new ContextOptionsFactory(settings, Devices).Create();

        options.Locale.ShouldBeNull();
        options.TimezoneId.ShouldBeNull();
        options.ColorScheme.ShouldBeNull();
    }

    private static UiAutomationSettings Settings(string baseUrl)
    {
        return new UiAutomationSettings { Target = new TargetSettings { BaseUrl = baseUrl } };
    }
}

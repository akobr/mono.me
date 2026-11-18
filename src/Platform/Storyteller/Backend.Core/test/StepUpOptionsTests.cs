using _42.Platform.Storyteller.Accessing;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Backend.Core.UnitTests;

// Auth:StepUp (424-264): the window of a recent sign-in, on by default where tokens carry auth_time.
public class StepUpOptionsTests
{
    [Fact]
    public void AddUserAuthenticationOptions_WithoutTheSection_KeepsTheDefaults()
    {
        using var provider = Build(new Dictionary<string, string?>());
        var options = provider.GetRequiredService<IOptions<StepUpOptions>>().Value;

        options.Enabled.ShouldBeNull();
        options.MaxAge.ShouldBe(TimeSpan.FromMinutes(10));
        provider.GetRequiredService<TimeProvider>().ShouldBe(TimeProvider.System);
    }

    [Fact]
    public void AddUserAuthenticationOptions_BindsTheSection()
    {
        using var provider = Build(new Dictionary<string, string?>
        {
            ["Auth:StepUp:Enabled"] = "false",
            ["Auth:StepUp:MaxAge"] = "00:30:00",
        });
        var options = provider.GetRequiredService<IOptions<StepUpOptions>>().Value;

        options.Enabled.ShouldBe(false);
        options.MaxAge.ShouldBe(TimeSpan.FromMinutes(30));
    }

    // On by default only under AuthKit: Entra ID access tokens carry no auth_time, and no client of the platform can ask
    // Keycloak for a new sign-in yet; Debug builds skip it too.
    [Theory]
    [InlineData(IdentityProviderKind.AuthKit, false, true)]
    [InlineData(IdentityProviderKind.Keycloak, false, false)]
    [InlineData(IdentityProviderKind.EntraId, false, false)]
    [InlineData(IdentityProviderKind.AuthKit, true, false)]
    [InlineData(IdentityProviderKind.Keycloak, true, false)]
    public void IsEnabledFor_ByDefault_FollowsTheProviderAndTheBuild(IdentityProviderKind provider, bool developmentAuthentication, bool expected)
    {
        new StepUpOptions().IsEnabledFor(provider, developmentAuthentication).ShouldBe(expected);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IsEnabledFor_WhenSet_DecidesForEveryProviderAndBuild(bool enabled)
    {
        var options = new StepUpOptions { Enabled = enabled };

        foreach (var provider in Enum.GetValues<IdentityProviderKind>())
        {
            options.IsEnabledFor(provider, developmentAuthentication: false).ShouldBe(enabled);
            options.IsEnabledFor(provider, developmentAuthentication: true).ShouldBe(enabled);
        }
    }

    [Theory]
    [InlineData("00:01:00")]
    [InlineData("00:10:00")]
    [InlineData("1.00:00:00")]
    public void Validate_AWindowInTheRange_Succeeds(string maxAge)
    {
        new StepUpOptionsValidator().Validate(null, new StepUpOptions { MaxAge = TimeSpan.Parse(maxAge, System.Globalization.CultureInfo.InvariantCulture) }).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("00:00:00")]
    [InlineData("00:00:59")]
    [InlineData("1.00:00:01")]
    [InlineData("-00:10:00")]
    [InlineData("00:10:00.5")]
    public void Validate_AWindowOutOfTheRangeOrWithAFraction_Fails(string maxAge)
    {
        var result = new StepUpOptionsValidator().Validate(null, new StepUpOptions { MaxAge = TimeSpan.Parse(maxAge, System.Globalization.CultureInfo.InvariantCulture) });

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Auth:StepUp:MaxAge");
    }

    [Fact]
    public async Task AddUserAuthenticationOptions_AnInvalidWindow_FailsWhenTheHostStarts()
    {
        // No defaults: an Auth__StepUp__* variable of the machine must not decide what this test sees.
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true, EnvironmentName = Environments.Production });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:Provider"] = "EntraId",
            ["Auth:ClientId"] = "6c0f1a2e-1f43-4b0e-9a4d-2f5e8f0c9b11",
            ["Auth:TenantId"] = "8a7b6c5d-4e3f-4a1b-8c9d-0e1f2a3b4c5d",
            ["Auth:StepUp:MaxAge"] = "00:00:10",
        });
        builder.Services.AddUserAuthenticationOptions(builder.Configuration);
        using var host = builder.Build();

        var exception = await Should.ThrowAsync<OptionsValidationException>(() => host.StartAsync(TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("Auth:StepUp:MaxAge");
        exception.Message.ShouldContain("[d.]hh:mm:ss (00:10:00 for 10 minutes, 1.00:00:00 for 24 hours");
    }

    [Theory]
    [InlineData(600, "10 minutes")]
    [InlineData(60, "1 minute")]
    [InlineData(3600, "1 hour")]
    [InlineData(7200, "2 hours")]
    [InlineData(90, "90 seconds")]
    [InlineData(1, "1 second")]
    public void StepUpRequired_WritesTheWindowOut(int seconds, string expected)
    {
        var exception = new StepUpRequiredException(TimeSpan.FromSeconds(seconds));

        StepUpRequiredException.Describe(TimeSpan.FromSeconds(seconds)).ShouldBe(expected);
        exception.ErrorCode.ShouldBe(ErrorCodes.StepUpRequired);
        exception.MaxAgeSeconds.ShouldBe(seconds);
        exception.Message.ShouldBe($"This operation needs a recent sign-in: sign in again, then repeat it. A sign-in counts for {expected}.");
    }

    private static ServiceProvider Build(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment());
        services.AddUserAuthenticationOptions(configuration);
        return services.BuildServiceProvider();
    }
}

using System.Text;
using _42.Platform.Storyteller.Playground.DotNet.SimpleConfiguration;
using _42.Platform.Storyteller.Sdk.Configuring;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// A document holds any text; the default encoding of a Windows console or a redirected output would garble it.
Console.OutputEncoding = new UTF8Encoding(false);

var builder = Host.CreateApplicationBuilder(args);

try
{
    // STORYTELLER_ENDPOINT, STORYTELLER_KEY (@{organization}.{project}.{view}.rst.{responsibility}) and the variables of
    // the machine's credential come from the environment: from the shell, or from an AppHost such as DotNet.AspireHost.
    builder.Configuration.AddStoryteller();
}
catch (StorytellerConfigurationException exception)
{
    // Missing settings, a wrong key or a refused credential. The message names what to fix, and never a value.
    Console.Error.WriteLine(exception.Message);
    return 1;
}

// The provider's logging, and IStorytellerConfigurationRefresher in the container.
builder.Services.AddStorytellerConfiguration();

// The usual binding: IOptionsMonitor<PlaygroundOptions> follows every document the provider applies.
builder.Services.Configure<PlaygroundOptions>(builder.Configuration);

builder.Services.AddSingleton<TextWriter>(Console.Out);
builder.Services.AddHostedService<OptionsRenderer>();

await builder.Build().RunAsync();
return 0;

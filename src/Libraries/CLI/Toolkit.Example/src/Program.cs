using _42.CLI.Toolkit;
using _42.CLI.Toolkit.Example;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Hosting;

var host1 = new HostBuilder()
    .UseEnvironment(Environments.Development)
    .UseCommandLineApplication<RootCommand>(args)
    .UseStartup<Startup>() // use startup class to configure services
    .Build();

// build application host
var host = new HostBuilder()
    .UseEnvironment(Environments.Development)
    .UseCommandLineApplication<RootCommand>(args, application =>
    {
        // custom configuration of the console application
        application.AllowArgumentSeparator = true;
        application.MakeSuggestionsInErrorMessage = true;
        application.ShortVersionGetter = null;
        application.SetLongVersionGetter(); // prints the version on '--version' option
    })
    .UseStartup<Startup>() // use startup class to configure services
    .Build();

try
{
    // run the application
    return await host.RunCommandLineApplicationAsync();
}
catch (UnrecognizedCommandParsingException parsingException)
{
    parsingException.WriteOutput(); // print possible commands on input error
    return 1;
}

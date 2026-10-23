using _42.Platform.Storyteller.Sdk.Credentials;
using Azure.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace _42.Platform.Storyteller.Sdk.Configuring.KeyVault.UnitTests;

// The Program.cs of the issue (424-85, "Shape in Program.cs"), with its configuration lines unchanged; the
// Services.AddStoryteller() of the out-of-scope Hosting package is left out. It only has to compile.
public class IssueProgramTests
{
    [Fact]
    public void TheIssuesProgram_Compiles()
    {
        Action<string[]> program = IssueProgram.Run;

        program.ShouldNotBeNull();
    }

    private static class IssueProgram
    {
#pragma warning disable SA1025 // The comments keep the alignment of the issue.
        public static void Run(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Configuration.AddStoryteller(options =>
            {
                options.Key = "@house.main.default.exe.northwind.invoicing.production"; // or STORYTELLER_KEY
                options.Mode = StorytellerReadMode.Resolved;
                options.RefreshInterval = TimeSpan.FromSeconds(30);
                options.Optional = builder.Environment.IsDevelopment();
                options.Credential = StorytellerCredential.FromEnvironment();          // API key, certificate, client credentials, token file
                options.ReferenceResolver = new KeyVaultReferenceResolver(new DefaultAzureCredential());
                options.Placement = SourcePlacement.BeforeEnvironmentVariables;        // appsettings.json < Storyteller < env < command line
            });

            builder.Services.Configure<InvoicingOptions>(builder.Configuration);      // IOptionsMonitor<InvoicingOptions> follows refreshes
        }
#pragma warning restore SA1025
    }

    private sealed class InvoicingOptions
    {
        public string? Currency { get; set; }
    }
}

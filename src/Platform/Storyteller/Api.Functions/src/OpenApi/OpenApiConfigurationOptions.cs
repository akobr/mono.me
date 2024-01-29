using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Configurations;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Enums;
using Microsoft.OpenApi.Models;

namespace _42.Platform.Storyteller.Api.OpenApi;

public class OpenApiConfigurationOptions : DefaultOpenApiConfigurationOptions
{
    public override OpenApiInfo Info { get; set; } = new()
    {
        Version = ThisAssembly.AssemblyFileVersion,
        Title = "2S-API",
        Description = "The 2S-API is a RESTful API for interacting with the 2S Platform.",
        License = new OpenApiLicense
        {
            Name = "MIT License",
            Url = new Uri("https://opensource.org/licenses/MIT"),
        },
        Contact = new OpenApiContact
        {
            Name = "Ales Kobr",
            Email = "kobr.ales@outlook.com",
            Url = new Uri("https://42for.net/"),
        },
    };

    public override OpenApiVersionType OpenApiVersion { get; set; } = OpenApiVersionType.V3;
}

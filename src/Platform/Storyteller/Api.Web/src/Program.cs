using _42.Platform.Storyteller;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Api.Security;
using Asp.Versioning;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Logging;
using NSwag.Generation.Processors.Security;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddSingleton<ICosmosClientProvider, CosmosClientProvider>();
builder.Services.AddSingleton<IContainerRepositoryProvider, ContainerRepositoryProvider>();
builder.Services.AddSingleton<IAnnotationService, CosmosAnnotationService>();

//builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
//.AddMicrosoftIdentityWebApi(
//    builder.Configuration.GetSection("AzureAd"),
//    subscribeToJwtBearerMiddlewareDiagnosticsEvents: true);

var section = builder.Configuration.GetSection("AzureAd");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
.AddMicrosoftIdentityWebApi(
    tokenOptions =>
    {
        IdentityModelEventSource.ShowPII = true;
        IdentityModelEventSource.LogCompleteSecurityArtifact = true;

        section.Bind(tokenOptions);
        tokenOptions.Events = new JwtBearerEvents()
        {
            OnAuthenticationFailed = context =>
            {

                return Task.CompletedTask;
            },
            OnForbidden = context =>
            {
                return Task.CompletedTask;
            },
            OnTokenValidated = context =>
            {
                return Task.CompletedTask;
            },
        };
    },
    identityOptions =>
    {
        section.Bind(identityOptions);
        identityOptions.Events = new Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectEvents()
        {
            OnAccessDenied = context =>
            {
                return Task.CompletedTask;
            },
            OnAuthenticationFailed = context =>
            {
                return Task.CompletedTask;
            },
        };
    },
    subscribeToJwtBearerMiddlewareDiagnosticsEvents: true);

builder.Services.AddApiVersioning(options =>
{
    options.ApiVersionReader = new MediaTypeApiVersionReader();
    options.ApiVersionSelector = new CurrentImplementationApiVersionSelector(options);
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
});

builder.Services.AddControllers();

// Learn more about configuring Swagger/OpenAPI at
// https://learn.microsoft.com/en-us/aspnet/core/tutorials/getting-started-with-nswag?view=aspnetcore-7.0&tabs=visual-studio
builder.Services.AddOpenApiDocument(settings =>
{
    settings.PostProcess = document =>
    {
        document.Info.Title = "2S-API";
        document.Info.Version = "v1";
        document.Info.Description = "The 2S-API is a RESTful API for interacting with the 2S Platform.";
        document.Info.TermsOfService = "https://42for.net";

        document.Info.License = new NSwag.OpenApiLicense
        {
            Name = "MIT License",
            Url = "https://opensource.org/licenses/MIT",
        };

        document.Info.Contact = new NSwag.OpenApiContact
        {
            Name = "Founder - Ales Kobr",
            Email = "kobr.ales@outlook.com",
            Url = "https://www.linkedin.com/in/kobra/",
        };
    };

    var schema = new NSwag.OpenApiSecurityScheme()
    {
        Type = NSwag.OpenApiSecuritySchemeType.OAuth2,
        In = NSwag.OpenApiSecurityApiKeyLocation.Header,
        Flows = new NSwag.OpenApiOAuthFlows()
        {
            Implicit = new NSwag.OpenApiOAuthFlow()
            {
                AuthorizationUrl = "https://login.microsoftonline.com/common/oauth2/v2.0/authorize",
                TokenUrl = "https://login.microsoftonline.com/common/oauth2/v2.0/token",
                Scopes = new Dictionary<string, string>()
                {
                    { $"api://e1e9d06f-7c67-4acf-8b05-f4413697950f/{Scopes.Annotation.Read}", "Allows this app to read annotations" },
                    { $"api://e1e9d06f-7c67-4acf-8b05-f4413697950f/{Scopes.Annotation.Write}", "Allows this app to modify or create annotations" },
                    { "api://e1e9d06f-7c67-4acf-8b05-f4413697950f/access_as_user", "Allows this app to access the web API on your behalf" },
                },
            },
        },
    };

    settings.AddSecurity(JwtBearerDefaults.AuthenticationScheme, schema);
    settings.OperationProcessors.Add(new AspNetCoreOperationSecurityScopeProcessor(JwtBearerDefaults.AuthenticationScheme));
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    // Add OpenAPI 3.0 document serving middleware
    // Available at: http://localhost:<port>/swagger/v1/swagger.json
    app.UseOpenApi();

    // Add web UIs to interact with the document
    // Available at: http://localhost:<port>/swagger
    app.UseSwaggerUi(settings =>
    {
        settings.OAuth2Client = new NSwag.AspNetCore.OAuth2ClientSettings()
        {
            AppName = "Swagger Client",
            ClientId = "e1e9d06f-7c67-4acf-8b05-f4413697950f",
            ClientSecret = "kwy8Q~aLtJr6ZvSV6UEy4FcOy3edGlnooUsQqaIM",
            UsePkceWithAuthorizationCodeGrant = true,
        };
    });
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

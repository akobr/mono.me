using _42.Platform.Storyteller.Mcp.Hosting;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller.Mcp.Authentication;

// JWT bearer with explicit, pinned parameters: the issuers of the provider (one Entra tenant, the AuthKit domain or the
// Keycloak realm), the MCP resource as audience, RS256 only. Challenges and forbids answer with the resource metadata
// URL of the options, as RFC 9728 and the MCP authorization specification ask. The inbound lifetime is at least as
// strict as the API's exchange (McpTokenRules.CheckLifetime): a token the exchange would refuse is a 401 here, and the
// client gets a new one.
public static class McpAuthentication
{
    public static IServiceCollection AddMcpAuthentication(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddSingleton(provider => McpAuthSettings.From(provider.GetRequiredService<IOptions<McpHostOptions>>().Value));

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<McpAuthSettings, IHostEnvironment>(Configure);

        services.AddAuthorization();
        services.AddOptions<AuthorizationOptions>()
            .Configure<McpAuthSettings>((options, settings) => options.AddPolicy(McpPolicies.User, policy => policy
                .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .RequireAssertion(context => McpTokenRules.HasScope(context.User, settings.Provider, settings.Scope))));

        return services;
    }

    public static void Configure(JwtBearerOptions options, McpAuthSettings settings, IHostEnvironment environment)
    {
        options.MetadataAddress = settings.MetadataAddress;

        // A local Keycloak serves its keys over plain HTTP; outside Development the issuer is HTTPS (the options validator).
        options.RequireHttpsMetadata = !environment.IsDevelopment();

        options.MapInboundClaims = false;
        options.SaveToken = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuers = settings.ValidIssuers,
            ValidAudiences = settings.ValidAudiences,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,

            // Tolerant of nbf; OnTokenValidated holds exp to the exchange's needs.
            ClockSkew = McpTokenRules.ClockSkew,
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            NameClaimType = "name",
            RoleClaimType = "roles",
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                var now = (context.HttpContext.RequestServices.GetService<TimeProvider>() ?? TimeProvider.System).GetUtcNow();
                var failure = McpTokenRules.CheckLifetime(context.SecurityToken, now) ?? McpTokenRules.Check(context.Principal, settings);

                if (failure is not null)
                {
                    context.Fail(failure);
                }

                return Task.CompletedTask;
            },
            OnChallenge = context =>
            {
                context.HandleResponse();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;

                var presented = context.AuthenticateFailure is not null || !string.IsNullOrEmpty(context.Error);
                context.Response.Headers.WWWAuthenticate = presented
                    ? $"Bearer error=\"invalid_token\", resource_metadata=\"{settings.ProtectedResourceMetadataUrl}\""
                    : $"Bearer resource_metadata=\"{settings.ProtectedResourceMetadataUrl}\"";

                return Task.CompletedTask;
            },
            OnForbidden = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.Headers.WWWAuthenticate =
                    $"Bearer error=\"insufficient_scope\", scope=\"{settings.FullScope}\", resource_metadata=\"{settings.ProtectedResourceMetadataUrl}\"";

                return Task.CompletedTask;
            },
        };
    }
}

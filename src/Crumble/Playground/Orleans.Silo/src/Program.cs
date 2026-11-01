using _42.Crumble;
using _42.Crumble.Playground.Examples;
using Orleans.Dashboard;

var builder = WebApplication.CreateBuilder(args);

builder.AddOrleansServiceDefaults();

builder.AddKeyedRedisClient("redis");
builder.UseOrleans(siloBuilder =>
{
    siloBuilder.AddActivityPropagation();
    siloBuilder.AddDashboard();
});

/*builder.Services.AddSingleton<IIncomingGrainCallFilter, TelemetryIncomingGrainCallFilter>();
builder.Services.AddTransient<IChainedCrumbsExecutor, ChainedCrumbsExecutor>();*/

builder.Services.AddCrumble().AddCrumbs();

var app = builder.Build();

app.MapOrleansDashboard(routePrefix: "/dashboard");
app.UseHttpsRedirection();
app.MapDefaultEndpoints();

await app.RunAsync();

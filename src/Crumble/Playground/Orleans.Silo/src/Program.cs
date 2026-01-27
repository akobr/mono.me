using _42.Crumble;
using _42.Crumble.Playground.Examples;

var builder = WebApplication.CreateBuilder(args);

builder.AddOrleansServiceDefaults();

builder.AddKeyedRedisClient("redis");
builder.UseOrleans(siloBuilder =>
{
    siloBuilder.AddActivityPropagation();
    siloBuilder.UseDashboard(options => { options.HostSelf = false; });
});

/*builder.Services.AddSingleton<IIncomingGrainCallFilter, TelemetryIncomingGrainCallFilter>();
builder.Services.AddTransient<IChainedCrumbsExecutor, ChainedCrumbsExecutor>();*/

builder.Services.AddCrumble().AddCrumbs();

var app = builder.Build();

app.Map("/dashboard", x => x.UseOrleansDashboard());
app.UseHttpsRedirection();
app.MapDefaultEndpoints();

await app.RunAsync();

using _42.Crumble;
using _42.Crumble.Playground.Examples;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddKeyedRedisClient("redis");
builder.UseOrleansClient();

builder.Services.AddSingleton<IOutgoingGrainCallFilter, TelemetryOutgoingGrainCallFilter>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

// Create any development endpoints here
app.MapGet("/dev", () =>
{
    var factory = app.Services.GetRequiredService<IGrainFactory>();
    var grain = factory.GetGrain<IChainedCrumbsFirstGrain>("default");
    return grain.ExecuteCrumb();
});

await app.RunAsync();

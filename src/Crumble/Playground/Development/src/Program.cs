using _42.Crumble.Playground.Examples.Implementation;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddKeyedRedisClient("redis");
builder.UseOrleansClient();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

// Create any development endpoints here
app.MapGet("/dev", () =>
{
    var factory = app.Services.GetRequiredService<IGrainFactory>();
    var grain = factory.GetGrain<IHelloWorldWithOutputGrain>("_42.Crumble.Playground.Examples.FirstCrumbs.HelloWorldWithOutput");
    return grain.ExecuteCrumb();
});

await app.RunAsync();

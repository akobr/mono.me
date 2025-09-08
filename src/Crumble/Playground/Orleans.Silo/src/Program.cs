var builder = WebApplication.CreateBuilder(args);

builder.AddOrleansServiceDefaults();

builder.AddKeyedRedisClient("redis");
builder.UseOrleans(siloBuilder =>
{
    siloBuilder.UseDashboard(options => { options.HostSelf = false; });
});

var app = builder.Build();

app.Map("/dashboard", x => x.UseOrleansDashboard());
app.UseHttpsRedirection();
app.MapDefaultEndpoints();

await app.RunAsync();

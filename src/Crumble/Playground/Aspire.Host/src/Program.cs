using _42.Crumble.Playground.Aspire.Host;

var builder = DistributedApplication.CreateBuilder(args);

var redis = builder
    .AddRedis("redis")
    .WithRedisInsight(b => b
        .WithLifetime(ContainerLifetime.Persistent)
        .WithHostPort(60719));

var orleans = builder
    .AddOrleans("orleans")
    .WithClustering(redis)
    .WithGrainStorage("Default", redis)
    .WithGrainStorage("PubSubStore", redis)
    .WithMemoryStreaming("Default");

var silos = builder
    .AddProject<Projects.Orleans_Silo>("silo")
    .WithReference(orleans)
    .WaitFor(redis)
    .WithReplicas(3) // additional instances of silos, can be commented out
    .WithOpenOrleansDashboardCommand()
    .WithExternalHttpEndpoints();

var testing = builder
    .AddProject<Projects.Development>("development")
    .ExcludeFromManifest()
    .WithReference(orleans.AsClient())
    .WaitFor(redis)
    .WithExternalHttpEndpoints();

await builder.Build().RunAsync();

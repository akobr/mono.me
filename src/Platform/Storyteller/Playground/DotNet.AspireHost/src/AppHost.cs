using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

// What the resources work on, from appsettings.json: by default the data of the playground (Playground/data) in the
// local platform of src/Platform/Aspire.Host, which runs apart from this host. Start it first.
var playground = builder.Configuration.GetRequiredSection("Playground");
var responsibility = playground["Responsibility"]!;
var subject = playground["Subject"]!;
var context = playground["Context"]!;

// The endpoint is in appsettings.json, and the API key of the machine in the user secrets of this project, as
// Parameters:storyteller-api-key. Without it, the dashboard asks for it.
var storyteller = builder.AddStoryteller("storyteller", builder.AddParameter("storyteller-endpoint"))
    .WithView(playground["Organization"]!, playground["Project"]!, playground["View"]!)
    .WithApiKey(builder.AddParameter("storyteller-api-key", secret: true));

// The configuration of the responsibility as IConfiguration. It starts without the platform while that is unreachable,
// and shows the document as added keys when the platform answers.
builder.AddProject<Projects.DotNet_SimpleConfiguration>("simple-configuration")
    .WithReference(storyteller)
    .WithStorytellerKey($"rst.{responsibility}")
    .WithEnvironment("STORYTELLER_OPTIONAL", "true");

// Every subject of the responsibility in the context, and every change of them from then on.
builder.AddProject<Projects.DotNet_SimpleExecutor>("simple-executor")
    .WithReference(storyteller)
    .WithStorytellerKey($"rst.{responsibility}")
    .WithArgs("--context", context, "--watch");

// The point of view of one subject, once, started from the dashboard: who am I with the ancestors of an execution.
builder.AddProject<Projects.DotNet_SimpleExecutor>("simple-executor-of-subject")
    .WithReference(storyteller)
    .WithExecution(subject, responsibility, context)
    .WithExplicitStart();

builder.Build().Run();

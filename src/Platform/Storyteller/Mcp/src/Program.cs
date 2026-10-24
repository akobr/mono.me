using _42.Platform.Storyteller.Mcp.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.AddStorytellerMcp();

var app = builder.Build();
app.UseStorytellerMcp();
app.Run();

// WebApplicationFactory<Program> in the tests.
public partial class Program;

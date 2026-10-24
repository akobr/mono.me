using _42.Platform.Storyteller.Mcp.Hosting;

// First, before anything uses a Regex: NJsonSchema checks a schema's pattern keyword with the process default match
// timeout, infinite unless it is set here, and Regex reads that default only once.
AppContext.SetData("REGEX_DEFAULT_MATCH_TIMEOUT", TimeSpan.FromMilliseconds(200));

var builder = WebApplication.CreateBuilder(args);
builder.AddStorytellerMcp();

var app = builder.Build();
app.UseStorytellerMcp();
app.Run();

// WebApplicationFactory<Program> in the tests.
public partial class Program;

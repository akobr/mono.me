namespace _42.Platform.Storyteller.Mcp.Understanding;

// A schema referred to a URL or a file; the host never fetches or reads one.
public sealed class ExternalSchemaReferenceException()
    : Exception("The schema refers to an external document (a URL or a file); the MCP host does not resolve external references.");

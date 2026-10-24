namespace _42.Platform.Storyteller.Mcp.Tools;

// An input a tool refuses before it calls the API. The message is shown to the model as it is, so it says what is
// wrong and which call comes next.
public sealed class ToolInputException(string message) : Exception(message);

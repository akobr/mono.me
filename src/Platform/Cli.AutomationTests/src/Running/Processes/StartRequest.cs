namespace _42.Platform.Cli.AutomationTests.Running.Processes;

// What a ChildProcess starts: the program and its arguments as a list, the current directory, the whole environment
// (nothing is inherited), and the size of a pseudo terminal.
internal sealed record StartRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string> Environment,
    int Columns,
    int Rows);

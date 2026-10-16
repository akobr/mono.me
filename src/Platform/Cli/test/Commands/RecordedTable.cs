using System.Collections.Generic;

namespace _42.Platform.Cli.UnitTests.Commands;

// A table that a command wrote to the RecordingConsole: the header texts and the text of every cell, row by row.
internal sealed record class RecordedTable(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows);

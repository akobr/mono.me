using System.Collections.Generic;

namespace _42.Monorepo.Cli.Configuration;

public interface IItemFullOption : IBasicOptions
{
    string Path { get; }

    string? Name { get; }

    string? Description { get; }

    string Type { get; }

    IReadOnlyCollection<string> Dependencies { get; }
}

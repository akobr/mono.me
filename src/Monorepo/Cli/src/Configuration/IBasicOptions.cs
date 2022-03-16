using System.Collections.Generic;

namespace _42.Monorepo.Cli.Configuration;

public interface IBasicOptions
{
    IReadOnlyDictionary<string, string> Custom { get; }

    IReadOnlyCollection<string> Exclude { get; }

    IReadOnlyDictionary<string, string> Scripts { get; }
}

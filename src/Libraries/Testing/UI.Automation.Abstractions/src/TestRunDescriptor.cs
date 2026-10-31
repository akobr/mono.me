namespace _42.Testing.UI.Automation;

// The run every reporter creates in its system. Properties hold the environment, without secrets: profile, provider,
// engine, device, target, seed, branch, sha, build url; the keys are in TestRunProperties.
public sealed record TestRunDescriptor(
    string RunId,
    string Name,
    string? Description,
    DateTimeOffset StartedAt,
    IReadOnlyDictionary<string, string> Properties);

using Newtonsoft.Json.Linq;

namespace _42.Platform.Cli.Services;

/// <summary>
/// The stored content of a configuration and the version that content belongs to.
/// </summary>
/// <param name="Content">The stored content, without inherited values and templates.</param>
/// <param name="Version">The current version number.</param>
public readonly record struct StoredConfiguration(JObject Content, long Version);

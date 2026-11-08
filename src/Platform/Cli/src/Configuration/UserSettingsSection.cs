using Newtonsoft.Json.Linq;

namespace _42.Platform.Cli.Configuration;

// A section as the configuration binds it, normalised, with the file it came from.
public sealed record UserSettingsSection(string Name, JObject Values, UserSettingsLayer Layer);

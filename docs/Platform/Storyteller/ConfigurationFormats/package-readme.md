# Storyteller configuration formats for .NET

Renders a configuration of the 2S Platform Storyteller in four flat formats, with the flattening rules that the configuration provider applies: `flat` (a JSON object of .NET `IConfiguration` keys), `properties` (Spring Boot), `env` (`.env` files and environment variables) and `yaml`. One canonical reading of the document makes the output the same on the server and in a client.

## Install

```powershell
dotnet add package 42.Platform.Storyteller.ConfigurationFormats --prerelease
```

## Use

```csharp
using _42.Platform.Storyteller.ConfigurationFormats;

// content: the Content of a configuration, a JToken.
var body = ConfigurationFormatter.Format(content, ConfigurationFormat.Properties);

// The complete text with LF line ends; write it as UTF-8 without a byte order mark.
await File.WriteAllTextAsync("application.properties", body.Text, new UTF8Encoding(false));

// The env names and raw values, for example to set the variables of a process.
foreach (var variable in ConfigurationFormatter.ToEnvironmentVariables(content, environmentPrefix: "APP_"))
{
    startInfo.Environment[variable.Name] = variable.Value;
}
```

- A document that a format cannot represent throws `InvalidDocumentException` with the `Rule` (for example `colon-in-name`, `case-collision`, `bracket-in-name`, `array-gap`, `invalid-env-name`) and the JSON `Path`. The message names the path, never a value, and nothing is returned.
- `ConfigurationFormatNames.TryParse` reads `flat`, `properties`, `env` and `yaml` in any ASCII casing; `GetMediaType` is the `Content-Type` of a body.
- `DocumentFlattener.Flatten` returns the `IConfiguration` keys, and `DocumentFlattener.Read` every value with its path.
- The only dependency is `Newtonsoft.Json`.

## Documentation

The formats, their rules and escaping, the readers each one serves and their limits, and the test vectors: [Configuration Formats](https://github.com/akobr/mono.me/blob/main/docs/Platform/Storyteller/configuration-formats.md).

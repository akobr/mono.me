# Configuration formats

**`42.Platform.Storyteller.ConfigurationFormats`** turns one configuration document into the flat shapes the rest of the world eats: .NET `IConfiguration` keys, Spring Boot `.properties`, `.env` files and YAML.

> One document, four costumes. Same values in every one of them.

You rarely install it yourself: it comes with the [configuration provider](configuration), and the API and `sform` use the very same library. So the keys your .NET service sees, the `.env` your container gets and the YAML in your Helm chart can never disagree.

## The four costumes

The document:

```json
{ "db": { "hosts": ["a", "b"], "port": 5432 }, "features": ["vat"] }
```

:::tabs
== flat
```json
{
  "db:hosts:0": "a",
  "db:hosts:1": "b",
  "db:port": "5432",
  "features:0": "vat"
}
```
== properties
```properties
db.hosts[0]=a
db.hosts[1]=b
db.port=5432
features[0]=vat
```
== env
```sh
DB__HOSTS__0=a
DB__HOSTS__1=b
DB__PORT=5432
FEATURES__0=vat
```
== yaml
```yaml
db:
  hosts:
    - "a"
    - "b"
  port: 5432
features:
  - "vat"
```
:::

| Format | For |
| --- | --- |
| `flat` | .NET `IConfiguration`, any JSON reader |
| `properties` | Spring Boot |
| `env` | `.env` files, Docker Compose, the environment of any process |
| `yaml` | Helm values, Spring's `application.yml`, any YAML 1.2 reader |

## Three ways to get one

**From the API**, for any language with an HTTP client:

```text
GET /v1/house/main/default/configuration/exe.northwind.invoicing.production/resolved?format=env
```

Every format has its own `ETag`, so polling a format costs a `304` while nothing changes.

**From `sform`**, for scripts and pipelines:

```bash
sform story config exe.northwind.invoicing.production -r -f env --content-only > invoicing.env
sform story config exe.northwind.invoicing.production -r -f yaml -e values.yaml
```

**From .NET**, when you already hold the document:

```csharp
using System.Text;
using _42.Platform.Storyteller.ConfigurationFormats;

var body = ConfigurationFormatter.Format(content, ConfigurationFormat.Env);   // or environmentPrefix: "APP_"

await File.WriteAllTextAsync(".env", body.Text, new UTF8Encoding(false));
```

## When a document doesn't fit the costume

Some names can't be written in some formats: a space in an `env` name, `[` in a `properties` key, or two names that differ only in case (which `IConfiguration` would mix up). You get a clear `422` with the rule and the **JSON path** of the culprit, never its value:

```json
{
  "Message": "The name of the property at $.db['pass word'] cannot be part of an environment variable name: …",
  "ErrorCode": "format.invalid-env-name"
}
```

No half-written files: the formatter builds the whole text or nothing.

## Good to know

- **Export the resolved document.** The effective one still holds `@…` bindings, and `env` refuses the `$` of object bindings.
- **`env` upper-cases names** and joins them with `__`, as .NET's `AddEnvironmentVariables` expects. A prefix (`APP_`) goes in front of every name.
- **Don't `source` the `.env`.** It's a dotenv file, not a shell script: `$` is never escaped.
- **Deterministic.** LF line ends, no byte order mark, document order. The same document always gives the same bytes, on the server and in your process.

## Reference

[Configuration Formats](https://github.com/akobr/mono.me/blob/main/docs/Platform/Storyteller/configuration-formats.md) on GitHub: every rule, every escape, and the conformance vectors other languages can test against.

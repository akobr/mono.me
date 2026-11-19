# Cheat sheet

Everything you'll look up twice, on one page. Print it, pin it, tattoo it (we don't judge).

## The STORYTELLER_* variables

The configuration provider, Hosting, the C# SDK, `sform`, the GitHub Action and the Aspire integration all read the same names.

**Who and where**

| Variable | Meaning |
| --- | --- |
| `STORYTELLER_ENDPOINT` | The API, `/api` included: `https://api.2s.42for.net/api`. `http` only to `localhost` |
| `STORYTELLER_KEY` | The full key of the process: `@house.main.default.exe.northwind.invoicing.production` |

**One credential, pick a kind**

| Kind | Variables |
| --- | --- |
| API key | `STORYTELLER_API_KEY` |
| Certificate (mTLS) | `STORYTELLER_CLIENT_CERTIFICATE` (a `.pfx`) and `STORYTELLER_CLIENT_CERTIFICATE_PASSWORD`; alone or with an API key |
| Client credentials | `STORYTELLER_CLIENT_ID`, `STORYTELLER_CLIENT_SECRET`, usually `STORYTELLER_TOKEN_URL` and `STORYTELLER_TOKEN_SCOPE` |
| Federation | `STORYTELLER_CLIENT_ID` and `STORYTELLER_TOKEN_FILE`, or just `STORYTELLER_CLIENT_ID` in a GitHub Actions job with `id-token: write` |
| A bearer token in a file | `STORYTELLER_TOKEN_FILE` alone |

Two kinds at once is an error that names both. `sform account machine create` prints the right set for you.

**The provider's knobs**

| Variable | Default |
| --- | --- |
| `STORYTELLER_MODE` | `resolved` (or `effective`) |
| `STORYTELLER_REFRESH_INTERVAL` | `30s`; `0` turns polling off |
| `STORYTELLER_OPTIONAL` | `false` |
| `STORYTELLER_CACHE_DIR` | none |

`sform` also reads `STORYTELLER_PROJECT` (`house.main`) and `STORYTELLER_VIEW`; the provider doesn't need them, because the key says it all.

## Keys

| Code | Type | Key |
| --- | --- | --- |
| `sbt` | Subject | `sbt.{subject}` |
| `rst` | Responsibility | `rst.{responsibility}` |
| `cnt` | Context | `cnt.{subject}.{context}` |
| `unt` | Unit | `unt.{responsibility}.{unit}` |
| `usg` | Usage | `usg.{subject}.{responsibility}` |
| `exe` | Execution | `exe.{subject}.{responsibility}.{context}` |
| `uxe` | Unit of execution | `uxe.{subject}.{responsibility}.{context}.{unit}` |

A full key puts `@{organization}.{project}.{view}.` in front. The configuration of an execution merges, from general to specific: responsibility → subject → usage → context → execution.

## Roles of people

`Reader` < `Contributor` < `ContributorWithSecrets` < `Administrator` < `Owner`

| You want to | You need |
| --- | --- |
| Read the catalog and configurations | `Reader` |
| Write annotations and configurations, create machines | `Contributor` |
| Read or write secrets, create a machine with secret scopes | `ContributorWithSecrets` |
| Members, policies, export and import, machines with management scopes | `Administrator` |

## Scopes of machines

Machines have no role; their scopes decide.

| Scope | Lets it | `sform account machine create` |
| --- | --- | --- |
| `Annotation.Read`, `Configuration.Read` | Read annotations and configurations | the default (`-r`) |
| `Annotation.Write`, `Configuration.Write` | Write them | `-w` |
| `Secrets.Read` | Receive secrets in resolved documents | `-r --scope secrets-read` |
| `Secrets.Write` | Write secret pairs | `--scope secrets-write` |
| Management scopes | Bulk and structural work: transfers, schemas, templates, deletes | `-m configuration-transfer,schema-write,…` |

`-a <key>` restricts a machine to one annotation and everything under it. Give every service the narrowest one you can.

## When things go sideways

| You see | It means | Try |
| --- | --- | --- |
| `StorytellerConfigurationException` at startup naming a variable | A setting is missing or malformed | Set the variable it names; the message never prints the value |
| `401` | No credential, or a wrong one | Check the credential variables; one kind only |
| `403` with `AccessDenied` | The machine lacks a scope, or your role is too low | The message names the scope; create a machine with it (machines can't be edited) |
| `403` with `AnnotationRestricted` | The machine is restricted to another annotation | Restrict it to the key you read, or to an ancestor of it |
| `404` | The key doesn't exist, or it's in another project or view | Check `STORYTELLER_KEY`, organization, project and view included |
| `X-2S-Secrets-Withheld: mtls-required` | The machine may read secrets, but the project delivers them only over mTLS | Give it its certificate (`STORYTELLER_CLIENT_CERTIFICATE`) |
| Values that still start with `@` | The caller may not read secrets, so secret bindings stay unresolved | Create a machine with `-r --scope secrets-read`, or store a reference that [your app resolves](key-vault) instead |
| `Status.IsDegraded` and `Origin` is `Cache` | It started from the last-known-good copy while the platform was away | Nothing: it catches up on its own |
| `OptionsValidationException` for a customer in a worker | That customer has no subscription right now | Catch it per customer; the `Removed` event follows |
| `422` with `format.…` | The document can't be written in that format | The message names the JSON path; rename the property |

Still stuck? The reference documentation lives in [docs/Platform/Storyteller](https://github.com/akobr/mono.me/tree/main/docs/Platform/Storyteller) on GitHub, and `sform <command> --help` knows more than it lets on.

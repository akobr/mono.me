# sform

::: danger
The platform is under development, see [the road map](/platform/road-map).
:::

A simple CLI interface for developers, dev ops engineers, and your infrastructure specialists to interact with the **2S** platform, consisting of a storyteller, supervisor, and scheduler, no matter where they are hosted and how.

## Sign in

Sign in with your account. The tool prints an address and a code, and waits while you confirm them in a browser. `--browser` opens the address for you.

``` powershell
sform account
```

Every other command uses the stored session and refreshes it silently. `sform account logout` signs you out.

## Sign in from a pipeline

In a CI/CD pipeline or a GitHub Action nobody can type a code, so `sform` signs in as a machine instead. Create the machine once, then give the job the variables that `sform account machine create` prints. With any of them set, `sform` never asks anyone to sign in, and it stores no credential or token.

| Machine | Variables |
|---|---|
| API key | `STORYTELLER_API_KEY` |
| Certificate | `STORYTELLER_CERTIFICATE_FILE`, `STORYTELLER_CERTIFICATE_PASSWORD` |
| Certificate and API key | all three of the above |
| Client credentials | `STORYTELLER_CLIENT_ID`, `STORYTELLER_CLIENT_SECRET`, `STORYTELLER_TOKEN_URL`, `STORYTELLER_TOKEN_SCOPE` |
| Federated | `STORYTELLER_CLIENT_ID`, the machine id, and `STORYTELLER_SUBJECT_TOKEN_FILE`, a file with a token of your platform; in a GitHub Actions job with `permissions: id-token: write`, the machine id alone |
| Any bearer token | `STORYTELLER_TOKEN_FILE`, a file the token is read from before every request |

Every machine also needs `STORYTELLER_API_URL`, the address of the API. An API key or a certificate already names its project. For the other kinds, set `STORYTELLER_PROJECT=<organization>.<project>` or pass `-p` to each command.

A federated machine has no secret. `sform` exchanges the platform token for a short-lived Storyteller token at the token endpoint of the project, `<STORYTELLER_API_URL>/v1/<organization>/<project>/access/token`, and exchanges again before that token expires. In GitHub Actions it asks the runner for the platform token itself, for the audience of that address, so the audience of the machine's GitHub rule must be exactly that address, in the same case: use the `TokenEndpoint` and the variables that `sform account machine create` prints. A token from `STORYTELLER_SUBJECT_TOKEN_FILE` is sent as it is, so its audience is whatever your platform put in it, and the rule's audience must match that instead. The `TokenEndpoint` is a good choice where you pick the audience yourself, as in the Kubernetes example below.

`STORYTELLER_TOKEN_FILE` and `STORYTELLER_SUBJECT_TOKEN_FILE` differ: the first holds a token the API accepts and is sent as it is, the second holds a token of your platform, which `sform` exchanges.

``` yaml
# GitHub Actions
- run: dotnet tool install 42.Platform.Cli --global --prerelease
- run: sform story config set sbt.northwind -i configuration/northwind.json
  env:
    STORYTELLER_API_URL: https://storyteller.example.com/api
    STORYTELLER_API_KEY: ${{ secrets.STORYTELLER_API_KEY }}
```

``` yaml
# GitHub Actions job, federated machine: no secret, no file
runs-on: ubuntu-latest
permissions:
  id-token: write
  contents: read
env:
  STORYTELLER_API_URL: https://storyteller.example.com/api
  STORYTELLER_PROJECT: acme.billing
  STORYTELLER_CLIENT_ID: 3f1c2a9e-5b7d-4c11-9e0a-2d4f6b8c1a07
steps:
  - uses: actions/checkout@v4
  - run: dotnet tool install 42.Platform.Cli --global --prerelease
  - run: sform story config set sbt.northwind -i configuration/northwind.json
```

``` yaml
# Kubernetes, federated machine: a projected token for the token endpoint
containers:
  - name: sync
    env:
      - { name: STORYTELLER_API_URL, value: https://storyteller.example.com/api }
      - { name: STORYTELLER_PROJECT, value: acme.billing }
      - { name: STORYTELLER_CLIENT_ID, value: 3f1c2a9e-5b7d-4c11-9e0a-2d4f6b8c1a07 }
      - { name: STORYTELLER_SUBJECT_TOKEN_FILE, value: /var/run/secrets/storyteller/token }
    volumeMounts:
      - { name: storyteller-token, mountPath: /var/run/secrets/storyteller, readOnly: true }
volumes:
  - name: storyteller-token
    projected:
      sources:
        - serviceAccountToken:
            path: token
            audience: https://storyteller.example.com/api/v1/acme/billing/access/token
```

`sform account` checks the variables without calling the account API, which is a good first step of a job. For client credentials and a federated machine it also gets a token; a federated machine needs `STORYTELLER_PROJECT` for that, as `sform account` has no `-p`. A wrong setting or a missing platform token file stops the job with exit code `2`, a token endpoint or GitHub request that fails with `3`, rejected credentials or a refused exchange with `44`.

::: warning
Commands print what they read or write. A step that reads a resolved configuration with secrets puts those values into the job log, so prefer steps that write configuration, or keep such output out of the log.
:::

::: tip
The details, such as the token URL rules, the certificate header for a local API, how a federated machine gets and renews its token, and every exit code, are in [the technical documentation](https://github.com/akobr/mono.me/blob/main/docs/Platform/Storyteller/authentication.md#machine-sign-in-for-pipelines).
:::

## Telemetry

`sform` sends anonymous usage data to PostHog in the EU cloud: which commands run, how they end and how long they take. It sends one event per run, after the command has finished. The output and the exit code stay the same, the run waits for the event about two seconds at most, and an event that cannot be sent is dropped.

| Sent | Example |
|---|---|
| The command, by its full name, without arguments or option values | `sform storyteller kv get` |
| The outcome and the exit code | `success`, `0` |
| How long the run took, in milliseconds | `840` |
| The type of a failure, the reason of a failed sign-in, the HTTP status of a refused API call; never a message | `ApiException`, `403` |
| The version of `sform` and whether it is a release or a development build | `0.8.85-alpha+0cab16204f`, `Production` |
| The operating system, its architecture and the .NET version | `linux`, `X64`, `10.0.8` |
| Whether the run is in a CI pipeline, and whether it signed in as a machine | `true`, `false` |
| A random installation id | `3f1c2a9e5b7d4c119e0a2d4f6b8c1a07` |

Never sent: arguments, option values, error messages, names of organizations, projects, views or keys, addresses, file paths, your name or e-mail, the name of the machine, and tokens. The events create no person profile, and PostHog does not look up a location for them.

The installation id is a random number, not derived from the machine or your account. It is kept in `telemetry.json`, next to the stored session: in `%LOCALAPPDATA%\.42for.net` on Windows and in `~/.42for.net` elsewhere, so it survives tool updates. On the first run `sform` creates it and prints a notice to standard error. When `sform` reports a failure to Sentry, the report carries the same id.

Set `DO_NOT_TRACK` or `SFORM_TELEMETRY_OPTOUT` to `1`, `true`, `yes` or `on` to turn telemetry off. `DO_NOT_TRACK` is honoured by other tools too, `SFORM_TELEMETRY_OPTOUT` only by `sform`. With telemetry off, `sform` sends nothing and creates no id.

``` powershell
# This session only
$env:DO_NOT_TRACK = "1"

# Every new session of your user
[Environment]::SetEnvironmentVariable("SFORM_TELEMETRY_OPTOUT", "1", "User")
```

In a pipeline, set the variable in the `env` of the job like the sign-in variables above.

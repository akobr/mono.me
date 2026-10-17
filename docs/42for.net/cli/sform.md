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
| Any bearer token | `STORYTELLER_TOKEN_FILE`, a file the token is read from before every request |

Every machine also needs `STORYTELLER_API_URL`, the address of the API. An API key or a certificate already names its project. For the other kinds, set `STORYTELLER_PROJECT=<organization>.<project>`.

``` yaml
# GitHub Actions
- run: dotnet tool install 42.Platform.Cli --global --prerelease
- run: sform story config set sbt.northwind -i configuration/northwind.json
  env:
    STORYTELLER_API_URL: https://storyteller.example.com/api
    STORYTELLER_API_KEY: ${{ secrets.STORYTELLER_API_KEY }}
```

`sform account` checks the variables without calling the account API, which is a good first step of a job. A wrong setting stops the job with exit code `2`, rejected credentials with `44`.

::: warning
Commands print what they read or write. A step that reads a resolved configuration with secrets puts those values into the job log, so prefer steps that write configuration, or keep such output out of the log.
:::

::: tip
The details, such as the token URL rules, the certificate header for a local API, and every exit code, are in [the technical documentation](https://github.com/akobr/mono.me/blob/main/docs/Platform/Storyteller/authentication.md#machine-sign-in-for-pipelines).
:::

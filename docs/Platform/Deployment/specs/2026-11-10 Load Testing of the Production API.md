# Load Testing of the Production API

## Problem

The production environment is sized for the free tiers (Flex Consumption with 512 MB instances and at most 10 of them, Cosmos DB with 1000 RU/s for the whole account, Log Analytics capped at 0.15 GB a day), but nobody knows how far that goes. We don't know:

- how many requests a second the API serves before latency grows, Cosmos DB throttles (`429`) or Flex runs out of instances;
- what a cold start costs a caller, and what one always-ready instance changes;
- what a given amount of traffic costs: Flex executions and GB-seconds, Cosmos DB request units, log volume;
- whether 512 MB or 2048 MB instances are the better price for the API.

Until 7 December 2026 the free account's credit pays for the experiment ([runbook, "The credit period"](../runbook-azure-production.md)), so this is the time to measure once, with real Azure resources, and write the numbers down.

## Current State

- **API:** `prod-2s-api-fa`, Flex Consumption, .NET 10 isolated, 512 MB, `maximumInstanceCount` 10, one always-ready instance of the `http` group during the credit period (`functionAlwaysReadyInstances = 1`). Its public base is `https://api.2s.42for.net/api`.
- **Data:** Cosmos DB `prod-2s-42fornet-cdb`, database `42.Platform.2S` with shared autoscale up to 1000 RU/s, containers `core` and `organizations`. The account limit (`cosmosTotalThroughputLimit`) is 1000 after DbCreator, so the database can't be scaled up for a test without changing the parameter.
- **Machine access:** API-key machines send `Authorization: ApiKey <key>` (`Api.Functions/src/Security/MachineAuthenticationMiddleware.cs`). Users send AuthKit bearer tokens, which expire, so machines are the practical caller for a load test.
- **Endpoints worth measuring** (`Api.Functions/src/Definitions.cs`):
  - anonymous: `GET v1/auth/configuration`;
  - the typical machine read: `GET v1/{organization}/{project}/{view}/configuration/{key}/resolved`;
  - annotation reads: `GET v1/{organization}/{project}/{view}/annotations/{key}`, `.../annotations` (list), `.../annotations/{key}/{descendants}`;
  - writes: `POST|PUT v1/{organization}/{project}/{view}/annotations/{key}`.
- **Monitoring:** Application Insights `prod-2s-api-appi` (requests, dependencies, Cosmos DB charges as dependency data), Flex metrics on the site, Cosmos DB metrics (normalized RU consumption, `429`s), Cost Management with the tags `product=2s-platform`, `environment=prod`.
- **Rules:** tests only in test organizations, never a customer's ([decisions](../decisions.md), decision 10). Production data never leaves production (decision 15).
- No load test, tool or test script exists in the repository.

## Proposed Changes

### 1. Tool and resource

- **[Azure Load Testing](https://learn.microsoft.com/azure/load-testing/overview-what-is-azure-load-testing)** with **Locust** scripts (Python; readable, versioned in the repository, also runnable locally with `locust` for a dry run). JMeter is the alternative if Locust support turns out limited in the region.
- One resource **`prod-2s-api-lt`** in `prod-2s-eusec-rg`, created by hand with the Azure CLI (`az load create`), **not** in `main.bicep`: it exists only for the credit period and is deleted on 7 December. Region Sweden Central if Azure Load Testing is offered there (check `az provider show -n Microsoft.LoadTestService --query "resourceTypes[?resourceType=='loadTests'].locations"`), else North Europe: the engines then load the API from a neighbouring region, which adds a few milliseconds and is noted in the results.
- Tags as the rest (`product=2s-platform`, `environment=prod`, `managedBy=manual`).
- Cost: a monthly resource fee and a price per virtual user hour beyond the included ones (check the [pricing page](https://azure.microsoft.com/pricing/details/load-testing/)); the plan below stays within a few dozen virtual user hours.

### 2. Test data (in a test organization only)

- A test organization `loadtest` with a project `perf` and a view `main`, created through the admin UI or `sform` by the owner, and deleted at the end (section 8).
- Seeded with `sform story import` from a generated data set under `src/Platform/Deployment/load-tests/data/`: about 50 responsibilities and subjects, 500 annotations, 50 configurations of realistic size (a few KB, nested, a few key/value references to Standard secrets), so reads hit real documents and the resolver does real work.
- One **API-key machine** `loadtest-reader` with read scopes on the project, and one `loadtest-writer` with write scopes for the write scenario. Their keys go into the vault as secrets `loadtest-reader-key` and `loadtest-writer-key`; the load test reads them as **secret parameters** referencing the vault, with the load testing resource's managed identity given **Key Vault Secrets User on those two secrets only**. No key in the repository or in a script argument.

### 3. Scenarios (Locust user classes)

| # | Scenario | Requests | Why |
| --- | --- | --- | --- |
| S1 | Discovery | `GET v1/auth/configuration`, anonymous | the cheapest path: Flex and network only, no Cosmos DB |
| S2 | Configuration read | `GET …/configuration/{key}/resolved` with `ApiKey`, keys drawn at random | the main machine workload (apps and pipelines reading their configuration) |
| S3 | Annotation reads | 70 % `GET …/annotations/{key}`, 20 % list, 10 % descendants | the admin UI and SDK pattern |
| S4 | Mixed with writes | S3 plus 10 % `PUT …/annotations/{key}` on a set of load-test annotations | write charges and audit history |
| S5 | Cold start | one request after 20 minutes of no traffic, repeated 5 times, with always-ready 1 and then 0 | what an idle API costs its first caller |

Every request carries a header `X-Load-Test: <run id>`, so the runs can be found and excluded in Application Insights.

### 4. Load profiles

| Profile | Shape | Scenarios |
| --- | --- | --- |
| Baseline | 5 users, 10 minutes | S1, S2, S3 |
| Step | 10 → 25 → 50 → 100 requests a second, 5 minutes each | S2, S3, S4 |
| Spike | 0 → 100 requests a second in 30 seconds, held 3 minutes | S2 |
| Soak | 10 requests a second, 60 minutes | S2 + S3 |

The step profile stops early when the error rate passes 5 % or Cosmos DB throttles for more than a minute.

### 5. Configurations compared

Each change is a parameter of `prod.bicepparam` and a run of `main.bicep` (no rebuild), set back afterwards:

| Run set | `functionInstanceMemoryMB` | `functionAlwaysReadyInstances` | `functionMaximumInstanceCount` |
| --- | --- | --- | --- |
| A (production) | 512 | 1 | 10 |
| B (bigger instances) | 2048 | 1 | 10 |
| C (no always ready) | 512 | 0 | 10 (only S5) |
| D (more instances) | 512 | 1 | 20 (only the step profile, to see whether instances or Cosmos DB is the limit) |

Cosmos DB stays at 1000 RU/s: the free tier is the production limit, so the test shows where it ends instead of moving it.

### 6. Measurements and criteria

- **From Azure Load Testing:** requests a second, p50/p95/p99 latency, error rate, per scenario and step.
- **Server side** (added to the test as app components, so they appear in the same report): Flex instance count and execution units of `prod-2s-api-fa`; Cosmos DB normalized RU consumption and `429`s; Application Insights request duration and dependency duration (Cosmos DB, Key Vault).
- **Criteria for "good enough" at a load step:** p95 under 500 ms for S2 and S3, under 1 s for S4; error rate under 1 %; no sustained `429`. The result is the highest step that meets them, per configuration.
- **Cost:** for each configuration, the Cost Management charges of the test day per resource, scaled to cost per million requests of S2 and S3, plus the monthly baseline of one always-ready instance. These are the numbers for later pricing.

### 7. Files

```
src/Platform/Deployment/load-tests/
├── README.md               # how to run: locally (locust) and in Azure Load Testing
├── locustfile.py           # the user classes of S1–S4 and the run header
├── coldstart.py            # S5: timed single requests after idle periods
├── config/
│   ├── baseline.yaml       # Azure Load Testing test configurations (engines, duration, secrets, app components)
│   ├── step.yaml
│   ├── spike.yaml
│   └── soak.yaml
└── data/                   # the generated test organization for sform story import (no customer data)
```

The results go into `docs/Platform/Deployment/load-test-results-2026-12.md`: the table per configuration and profile, the limits found, and the cost per million requests.

### 8. Safety and cleanup

- Only after the runbook's phase 11 (the API, DbCreator and the admin UI deployed) and the check of phase 12; never against a customer's organization.
- Outside the hours people use the admin UI; the S4 writes touch only the `loadtest` organization.
- The budget (50 during the credit period) alerts on the way; stop the tests if the actual cost passes 30 USD.
- **Cleanup, at the latest on 7 December** (the reminder of that day lists it): delete the `loadtest` organization, the two machines and their vault secrets, the role assignment of the load testing identity, and the `prod-2s-api-lt` resource; set the configuration parameters of section 5 back to 512, 0 (after the credit period) and 10.

### 9. Out of scope

- Load tests in a pipeline (they need the development environment, not production).
- User (AuthKit) token traffic: tokens expire within minutes, and the API's work behind a user token is the same as behind a machine key.
- Raising Cosmos DB above the free tier, and the MCP server.

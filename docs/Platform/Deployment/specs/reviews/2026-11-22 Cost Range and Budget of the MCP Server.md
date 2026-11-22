# Review: Cost Range and Budget of the MCP Server

## Overview

This review covers the implementation of the spec [Cost Range and Budget of the MCP Server](../2026-11-22%20Cost%20Range%20and%20Budget%20of%20the%20MCP%20Server.md) ([424-305](https://linear.app/424fun/issue/424-305/arch-the-0-usd-a-month-mcp-server-sits-on-a-public-unrestricted)). The MCP server was documented at "about 0 USD a month", but every request wakes a replica, scanners' requests too, and nothing watched the free grant. Now the docs state the cost as a range that depends on how often requests come. With `mcpEnabled`, `main.bicep` creates a budget of the subscription's Container Apps cost, `prod-2s-mcp-budget`, which warns once the grant is used up. The Container App's cool-down is a parameter, `mcpCooldownSeconds`, which the runbook's new phase 11 names as the first lever. Nothing was deployed: production runs the server only from phase 4 of its runbook, and `mcpEnabled` is still `false`.

## What Was Done

### 1. The cost model, stated as a range

The range of the spec, recomputed from the Sweden Central list prices (active vCPU 0.000024 USD/s, memory 0.000003 USD/GiB-s, a 30-day month), is in `docs/Platform/Deployment/decisions.md` ("The MCP server"). It runs from 0 below about 200 replica-hours a month, through about 7.60 USD at a request every 5 minutes and about 14 USD for one replica warm all month, to about 53 USD of compute with three replicas. Each warm hour past the grant costs 0.027 USD, and requests that come on average every 17 minutes or more often use up the grant. The cookbook's step 18, the runbook's phase 11, `mcp.md` and the comments of `modules/mcp.bicep` state the same. Every doc of the server now gives the cold start as 10 to 30 seconds, from Microsoft's MCP troubleshooting page, where it used to say "a few seconds".

Added after the review (section 8): 53 USD is the ceiling of the compute only. Requests past the grant's 2 million a month cost 0.40 USD per million, and no setting caps them. A steady 40 requests a second, enough to hold three replicas, would add about 41 USD.

### 2. The budget `prod-2s-mcp-budget`

- `src/Platform/Deployment/main.bicep`: `resource mcpBudget 'Microsoft.Consumption/budgets@2026-06-01' = if (mcpEnabled)`. It is the subscription's budget filtered on `MeterCategory` `In` `Azure Container Apps`, `amount: mcpBudgetAmount`, `Monthly`, starting `mcpBudgetStartDateChecked`. Its notifications are `actual50`, `actual100` and `forecasted100` (`GreaterThanOrEqualTo`, as in the subscription's budget) to `ownerEmail`. A comment says why it covers the whole subscription's meter category: the grant is per subscription, and the environment's own meters are counted too.
- New parameters after `mcpMaxReplicas`: `mcpBudgetStartDate string = ''` and `mcpBudgetAmount int = 1` (`@minValue(1)`).
- A new top-level check, `mcpBudgetStartDateChecked`, sits next to the other MCP checks. With `mcpEnabled`, it `fail()`s unless the date is 10 characters and ends in `-01`.
- `naming.bicep`: `mcpBudget: '${environment}-${product}-mcp-budget'` in the type and in `resourceNames`.
- `parameters/prod.bicepparam`: `param mcpBudgetStartDate = ''`, with a comment that phase 4 sets it, that it stays fixed while the budget exists, and that the what-if refuses `mcpEnabled` without it.

### 3. The cool-down as a parameter

- `modules/mcp.bicep`: `param cooldownSeconds int` (`@minValue(60)`, `@maxValue(1800)`) and `cooldownPeriod: cooldownSeconds` in `template.scale`. The scale comment now says that the cool-down governs only the last replica, that the others go after the 300-second scale-down window, and that with `minReplicas` 0 a running replica is billed at the active rate. The ingress comment says that a `401` still wakes a replica.
- `main.bicep`: `param mcpCooldownSeconds int = 300` with the same bounds, passed as `cooldownSeconds`. The default is the platform's own, so the first creation writes the same value the platform would assume.

### 4. What is not done

As the spec says. The metric alert, cheaper answers in the server, rule-based routing, `minReplicas` 1, a lower `mcpMaxReplicas` and anomaly alerts are not built. `decisions.md` names rule-based routing and `minReplicas` 1 (about 4 to 6 USD a month) as the later options, and says why answering scanners cheaply saves nothing.

### 5. Documentation

| File | Change |
| --- | --- |
| `docs/Platform/Deployment/decisions.md` | The intro names the new spec. The first entry gets the cold start. New entries: the cost as a range (a table), a budget of its own instead of an alert rule, and the cool-down kept at 300 seconds as a parameter, with the later options |
| `docs/Platform/Deployment/runbook-mcp-server.md` | Phase 11 in the phase table. Phase 4 sets `mcpBudgetStartDate` and expects the budget and `cooldownPeriod` 300 in the what-if. Phase 4's check gives the cold start of 10 to 30 seconds. Phase 9 reads the budget through `az rest` with the template's API version and checks the meter category in Cost analysis. Phase 10 lists the budget among what stays, and its deletion with the reset of the start date. New **phase 11, "The cost of the MCP server"**: what the emails mean, a check without an email, the month's replica-minutes, peak and minutes at two or more replicas from the `Replicas` metric, the last day's requests by status class from the `Requests` metric, and what to change in each case |
| `docs/Platform/Deployment/cookbook-azure-production.md` | Step 17a: the cool-down in the Container App's row, a row for the budget, and the portal steps for it. `az containerapp create` can set `cooldownPeriod` only through `--yaml`. Step 18: the range in the Container Apps row, a warm MCP replica in "What raises it", and the MCP budget's first warning at 0.50 USD |
| `docs/Platform/Storyteller/mcp.md` | "Limits of the hosting": the cool-down, the cold start, the grant and the budget |
| `src/Platform/Deployment/README.md` | "What is where": the second budget in `main.bicep`, and the cool-down in `modules/mcp.bicep`. A parameter row for `mcpBudgetStartDate`, `mcpBudgetAmount` and `mcpCooldownSeconds` |
| `CLAUDE.md` | The Deployment row names `mcpCooldownSeconds` and `prod-2s-mcp-budget` |

The existing specs and reviews are unchanged.

### 6. Validation

- `az bicep build` of `main.bicep` and `domains.bicep`, and `az bicep build-params` of both parameter files (Bicep 0.49.2, the dummy owner values of `Platform.Deployment.yml`): no error and no warning.
- The compiled `main.json` has the budget with `condition: [parameters('mcpEnabled')]`, the filter, the three notifications, and `cooldownPeriod: [parameters('cooldownSeconds')]`. The check compiles to `[if(and(parameters('mcpEnabled'), or(not(equals(length(...), 10)), not(endsWith(..., '-01')))), fail('mcpBudgetStartDate is required …'), parameters('mcpBudgetStartDate'))]`.
- **Deviation from the spec's section 6:** a build doesn't run the `fail()` check. As with the other MCP checks, `fail()` is an ARM expression that runs at template validation: the what-if, `validate` or `create`, before anything is written. A review agent confirmed it. `az bicep build-params` of a parameter file with `mcpEnabled = true` and an empty or malformed `mcpBudgetStartDate` exits 0, while a decorator such as `@minValue(60)` of `mcpCooldownSeconds` is refused at build time (`BCP328`). So `Platform.Deployment.yml` doesn't catch a missing date, and phase 4's what-if is where it is refused (acceptance criterion 2 holds). The `prod.bicepparam` comment says so.
- The PowerShell of phase 11 was checked with the PowerShell parser and run against a mocked `az`. That caught a `[datetime]::new` overload that took `'Utc'` as milliseconds; it is `[DateTimeKind]::Utc` now. Dates are passed in the culture-invariant round-trip format `'o'`, which the CLI's own argument parser (run locally, without Azure) accepts.
- No Azure call was made.

### 7. Acceptance criteria

1. Met: `prod-2s-mcp-budget` with `mcpEnabled`, 1 by default, the filter, the three alerts; nothing new without it.
2. Met at deployment: the what-if or create fails before anything is written and names the parameter. A build does not (section 6).
3. Met: `cooldownPeriod` from `mcpCooldownSeconds`, default 300, 60 to 1800.
4. Met: no doc of the deployment or the MCP server claims a flat 0 USD or a cold start of a few seconds. The one "few seconds" left in the cookbook (step 8) is about the API's Function App.
5. Met: phase 11 counts minutes from the per-minute maximum, where a minute without data counts as none, so the count is right either way.

### 8. Review of the change

The branch was reviewed through four lenses: the Bicep and its deployment behaviour, the facts and numbers of the docs, the operator's commands, and the repository's conventions. A second, skeptical reviewer checked each lens's findings. All ten findings were confirmed; they come down to eight distinct issues, and all eight were fixed:

| Finding | Fix |
| --- | --- |
| Phase 11, step 2 passed `--start-time` without `--end-time`, so `az monitor metrics list` read one hour (its `--offset` defaults to `1h`), not the day the text promised | `--end-time $now.ToString('o')` |
| The priced-free-usage case set `mcpBudgetAmount = 7` and said its first email then means past the grant. Its 50 % (3.50 USD) comes at about 130 warm hours, inside the grant | `mcpBudgetAmount = 11`: its 50 % is 5.50 USD, just past the grant's 5.40 USD (decisions, runbook) |
| "At most about 53 USD" ignored the requests, which cost 0.40 USD per million past 2 million and which no setting caps | 53 USD is called the ceiling of the compute, with the request charge and an example (decisions, cookbook, runbook) |
| A budget deleted in phase 10 can't be created again with its old start month, but the docs said never to change the date | Phase 10 resets `mcpBudgetStartDate` after a deletion; "never" became "while the budget exists" in phase 4, the README and `prod.bicepparam` |
| Step 3's "two or three replicas" case needed per-minute maxima that step 1 didn't show | Step 1 also prints the peak and the minutes with two or more replicas |
| Phase 9 expected `0` and would stop on the few cents of the priced-free-usage case | Phase 9 points such cents to phase 11's case |
| Phase 9 accepted "no row yet", so the meter category might never be confirmed | Phase 11 has a check without an email: more than 12,000 replica-minutes in a month and no email means the filter doesn't match |
| Section 6 of the spec expected a failing build | Recorded in section 6 above; the spec stays as written |

### 9. Deviations from the spec

| Spec | Done | Why |
| --- | --- | --- |
| `mcpBudgetAmount = 7` when Cost Management prices free usage | `11` | The 50 % email has to come past the grant (section 8) |
| "About 53 USD" as the worst case | The ceiling of the compute, with requests on top | Requests are a separate, uncapped meter (section 8) |
| The `fail()` check verified by a failing build | Verified in the compiled template; it fires at the what-if | ARM evaluates `fail()` at deployment time (section 6) |
| Phase 9 checks the meter category once | Also phase 11's check without an email | Free usage may not be listed, so one check may not settle it |

### 10. Risks left for the deployment

- **The meter category** `Azure Container Apps` is the serviceName of the Retail Prices API and the value of Microsoft's own budget samples, but no Microsoft doc states it for cost data. A wrong value makes the budget see nothing. Phases 9 and 11 check it; the subscription's budget would still notice at 2.50 USD.
- **How Cost Management shows free usage during the month** isn't documented. If it prices the free usage, the 50 % email comes early in the month; phase 11 tells the cases apart and gives `mcpBudgetAmount = 11`.
- **A lower cool-down for the HTTP rule** is accepted by the resource provider (60 was seen read back), but only raising it is documented. Phase 11 says to time the scale to zero once after lowering it.
- **The warning's delay**: cost data of a pay-as-you-go subscription can be up to 72 hours late, and budgets are evaluated daily, so the first email may come about four days after the grant ran out. At most that is about 2.60 USD with one replica warm, about 7.80 USD with three.
- **The budget's start month**: the run that creates it has to be in that month or a later one. Phase 4 sets the month of the run.

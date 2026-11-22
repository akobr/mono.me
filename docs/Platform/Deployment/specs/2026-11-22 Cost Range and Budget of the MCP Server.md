# Cost Range and Budget of the MCP Server

Linear: [424-305](https://linear.app/424fun/issue/424-305/arch-the-0-usd-a-month-mcp-server-sits-on-a-public-unrestricted), found in the review of PR #210 ([424-287](https://linear.app/424fun/issue/424-287/deploy-the-mcp-server-to-production-on-azure-container-apps)).

## Problem

The [decisions](../decisions.md#the-mcp-server) promise "about **0 USD a month**" for the MCP server on Azure Container Apps. That holds only while the app is idle most of the month, and nothing checks it:

- The ingress is public with no IP restriction, on purpose: MCP clients run anywhere.
- The managed certificate puts `mcp.2s.42for.net` into the Certificate Transparency logs, which scanners watch.
- Every request wakes a replica, a `401` too, and the last replica stays up for the cool-down after it. With `minReplicas` 0, the whole time is billed at the active rate.

A steady trickle of scanner requests can therefore keep one 0.25 vCPU / 0.5 GiB replica warm most of the month. That costs more than the free grant covers, and `maxReplicas` 3 is the only bound. No alert and no budget of its own watches it. The subscription's budget (`prod-2s-budget`) would notice only once the whole subscription passes 50 % of its amount, without saying why.

The docs also say a cold start takes "a few seconds". Microsoft's own troubleshooting page for MCP servers on Container Apps gives 10 to 30 seconds.

## Current State

- `src/Platform/Deployment/modules/mcp.bicep`: external ingress with no IP restriction (line 166), `minReplicas: 0`, `maxReplicas: maxReplicas`, one HTTP rule at 20 concurrent requests (lines 225–239), no `cooldownPeriod`, so the platform's default of 300 seconds applies. Its comments say "inside the monthly free grant" (line 3) and "a cold start of a few seconds" (line 225). The diagnostic setting sends only the console and system logs to `prod-2s-log` (lines 87–106). Platform metrics (`Replicas`, `Requests`) are kept by Azure Monitor anyway, at no cost.
- `src/Platform/Deployment/main.bicep`: one budget, `prod-2s-budget` (line 203), for the whole subscription, with four of its five possible notifications in use (50, 80 and 100 % actual, 100 % forecast). `mcpMaxReplicas` (line 165, default 3) is the only cost parameter of the server.
- `src/Platform/Deployment/naming.bicep`: no name for a second budget.
- `src/Platform/Deployment/parameters/prod.bicepparam`: `mcpEnabled = false`. The MCP server isn't deployed yet; its [runbook](../runbook-mcp-server.md) turns it on in phase 4.
- Docs with the flat cost and the cold start: `docs/Platform/Deployment/decisions.md` ("The MCP server"), `cookbook-azure-production.md` (step 17a, step 18 "Costs to expect"), `runbook-mcp-server.md` (phase 4, phase 9 "Cost ... 0 USD"), `docs/Platform/Storyteller/mcp.md` ("Limits of the hosting"), `src/Platform/Deployment/README.md`.
- The server runs stateless HTTP sessions (`Hosting/McpHostExtensions.cs:47`), so scaling to zero loses nothing but time.

### Facts this spec relies on

Researched on 2026-11-22 from Microsoft Learn (the `MicrosoftDocs/azure-docs` sources), the REST API specs (`Azure/azure-rest-api-specs`), the Azure Retail Prices API and the issue tracker of `microsoft/azure-container-apps`. Each was checked again by a second, independent pass.

| # | Fact | Source |
| --- | --- | --- |
| F1 | The Consumption plan's free grant is 180,000 vCPU-seconds, 360,000 GiB-seconds and 2 million requests per subscription and calendar month. "Free usage doesn't appear on your bill." | [billing](https://learn.microsoft.com/azure/container-apps/billing), [pricing](https://azure.microsoft.com/pricing/details/container-apps/) |
| F2 | Sweden Central, pay-as-you-go: active vCPU 0.000024 USD/s, memory 0.000003 USD/GiB-s (the same idle or active), idle vCPU 0.000003 USD/s, requests 0.40 USD per million. serviceName and productName are `Azure Container Apps`. | Retail Prices API, `serviceName eq 'Azure Container Apps' and armRegionName eq 'swedencentral'` |
| F3 | The idle rate applies only in a revision whose minimum replica count is above 0 and that runs at that minimum. With `minReplicas` 0, every running replica is billed at the active rate, the cold start and the cool-down included. | billing |
| F4 | Every request routed to an app that is scaled to zero starts a replica, whatever the app answers. Probes run inside a running replica, so they can't wake one; TCP probes don't hold one up either (an HTTP readiness probe did, in a reported case). | scale-app, microsoft/azure-container-apps#536, Q&A on HTTP probes |
| F5 | `template.scale.cooldownPeriod` (default 300 s) and `pollingInterval` (default 30 s) are in every stable API version from 2025-01-01 on, 2026-07-01 included. "The cool down period only takes effect when scaling in from the final replica to 0." The polling interval doesn't apply to HTTP rules. No minimum or maximum is documented; the resource provider accepts 60. | scale-app (ms.date 2026-05-19), REST spec `models.tsp`, azure-cli-extensions test recordings |
| F6 | The HTTP rule counts concurrent requests every 15 seconds. A measured isolated request kept its replica about 320 seconds. | scale-app, microsoft/azure-container-apps#997 |
| F7 | A cold start takes 10 to 30 seconds after a period of inactivity (Microsoft measured about 10 s at P50 and 15 s at P95 for its quickstart image). | [MCP troubleshooting](https://learn.microsoft.com/azure/container-apps/mcp-troubleshooting), microsoft/azure-container-apps#997 |
| F8 | Budgets (`Microsoft.Consumption/budgets@2026-06-01`) are free. `filter.dimensions` takes a Cost Management dimension with the operator `In`; Microsoft's own quickstart template filters on `MeterCategory`. A budget has at most five notifications, `Actual` or `Forecasted`, thresholds in percent (0.01 to 1000). A new monthly budget starts in the current month or a later one. | Consumption REST spec 2026-06-01, [budget quickstart](https://learn.microsoft.com/azure/cost-management-billing/costs/quick-create-budget-bicep), [budget tutorial](https://learn.microsoft.com/azure/cost-management-billing/costs/tutorial-acm-create-budgets) |
| F9 | Budgets are evaluated every 24 hours. Cost data of a pay-as-you-go subscription can be up to 72 hours late. Cost Management shows costs before credits. | budget tutorial, [understand cost data](https://learn.microsoft.com/azure/cost-management-billing/costs/understand-cost-mgt-data) |
| F10 | Metric alert rules cost 0.10 USD per monitored time series a month, with the first 10 free. An action group's email receiver must confirm a one-time passcode. Whether the `Replicas` metric reports 0 or no data while the app is scaled to zero is not documented. | [Azure Monitor pricing](https://azure.microsoft.com/pricing/details/monitor/), action groups, [Container Apps metrics](https://learn.microsoft.com/azure/container-apps/metrics) |
| F11 | Environment-level rule-based routing (`managedEnvironments/httpRouteConfigs`) is GA since November 2025. A path that matches no rule gets a `404`. It needs an A record to the environment and the domain bound to the route instead of the app, and it has open bugs (#1785, #1574). No source confirms that an unmatched path doesn't wake a replica. | [rule-based routing](https://learn.microsoft.com/azure/container-apps/rule-based-routing), issues #1574, #1785 |

Open points: the meter category of Container Apps in this subscription's cost data (expected `Azure Container Apps`, the value of F2), and whether Cost Management prices free usage during the open month and subtracts the grant only on the invoice (F1 says free usage isn't billed, but Cost Management's open-month estimates ignore tiered pricing). Section 2 is built to work either way, and the runbook checks both.

## Proposed Changes

### 1. The cost model, stated as a range

At 0.25 vCPU and 0.5 GiB, the free grant covers **720,000 replica-seconds, about 200 replica-hours a month**. The vCPU and memory grants run out at the same time. Every warm hour beyond the grant costs 0.027 USD (F2).

| Case | Warm time a month | Cost a month (Sweden Central list prices, November 2026) |
| --- | --- | --- |
| Idle, or a few people's sessions | under 200 replica-hours | **0** |
| Requests at random times, on average one every 30 minutes, all month (scanners) | about 120 hours | 0 |
| On average one every 15 minutes | about 220 hours | about 0.60 USD |
| On average one every 5 minutes | about 480 hours | about 7.60 USD |
| One replica warm all month | 720 hours | about 14 USD |
| Three replicas (`mcpMaxReplicas`) warm all month | 2,160 hours | about 53 USD |

The scanner rows assume about 5.5 minutes of warm time per isolated request: the 300-second cool-down, the 15-second sampling of the HTTP rule and the cold start (F5, F6, F7). The share of the month that is warm is then `1 - e^(-rate x 330 s)`. Requests that come on average more often than every 17 minutes, all month long, use up the grant.

These numbers go into the decisions, the cookbook's step 18 and the runbook instead of the flat "0 USD". The cold start becomes "10 to 30 seconds" (F7) wherever the docs say "a few seconds" about the MCP server.

### 2. A budget of the MCP server's own: `prod-2s-mcp-budget`

`main.bicep` creates a second budget at subscription scope with `mcpEnabled`:

```bicep
resource mcpBudget 'Microsoft.Consumption/budgets@2026-06-01' = if (mcpEnabled) {
  name: names.mcpBudget
  properties: {
    category: 'Cost'
    amount: mcpBudgetAmount
    timeGrain: 'Monthly'
    timePeriod: {
      startDate: mcpBudgetStartDateChecked
    }
    filter: {
      dimensions: {
        name: 'MeterCategory'
        operator: 'In'
        values: [
          'Azure Container Apps'
        ]
      }
    }
    notifications: {
      actual50: { enabled: true, operator: 'GreaterThanOrEqualTo', threshold: 50, thresholdType: 'Actual', contactEmails: [ownerEmail] }
      actual100: { … threshold: 100, thresholdType: 'Actual' … }
      forecasted100: { … threshold: 100, thresholdType: 'Forecasted' … }
    }
  }
}
```

- **Why the meter category of the subscription.** The free grant is per subscription (F1), so "the grant is used up" is a fact about all of the subscription's Container Apps usage. The filter also catches the environment meters (Environment Management Hour, Private Endpoint, Planned Maintenance at 0.10 USD an hour), which a change of the environment could bring in. If another product later runs Container Apps in this subscription, it shares the grant and this budget.
- **Why amount 1 and these thresholds.** In Microsoft's billing model (F1) the Container Apps cost stays 0 inside the grant, so any cost means the grant is used up. 50 % (0.50 USD, about 19 warm hours past the grant) is the first warning; the forecast warns when the month heads past 1 USD. If Cost Management turns out to price the free usage during the month (the open point above), the same thresholds work with `mcpBudgetAmount = 7`: the grant is worth 5.40 USD at Sweden Central's prices. The runbook says how to tell the two apart.
- **Why a separate budget.** The subscription's budget has four of its five notifications in use (F8), can't be filtered without losing the rest of the subscription, and warns only at 2.50 USD for everything together.
- **Latency.** The warning comes up to about four days after the cost: data up to 72 hours late plus the daily evaluation (F9). That is fine for a monthly grant that a warm replica uses up in about eight days.

New parameters of `main.bicep`, after `mcpMaxReplicas`:

| Parameter | Type and default | What |
| --- | --- | --- |
| `mcpBudgetStartDate` | `string = ''` | the first day of the budget's first month (`yyyy-MM-01`): the month of the run that creates it, or a later one (F8). Required with `mcpEnabled` (a `fail()` check like the other MCP parameters, which also refuses anything that isn't 10 characters ending in `-01`); fixed once the budget exists, like `budgetStartDate` |
| `mcpBudgetAmount` | `int = 1`, `@minValue(1)` | the monthly amount in the billing currency |

`naming.bicep` gets `mcpBudget: '${environment}-${product}-mcp-budget'` in `resourceNamesType` and `resourceNames`. `parameters/prod.bicepparam` gets `param mcpBudgetStartDate = ''` with a comment that phase 4 of the runbook sets it, next to the other MCP parameters.

### 3. The cool-down as a parameter: `mcpCooldownSeconds`

`modules/mcp.bicep` gets `param cooldownSeconds int` (`@minValue(60)`, `@maxValue(1800)`) and sets `template.scale.cooldownPeriod: cooldownSeconds` (F5). `main.bicep` passes `mcpCooldownSeconds` (default **300**, the platform's own default, so nothing changes).

- **Why a parameter and not a lower value now.** A lower cool-down cuts the warm time per scanner request: at 60 seconds, a request every 5 minutes stays inside the grant (about 190 hours instead of 480). But every pause of a real session longer than the cool-down then costs the person a cold start of 10 to 30 seconds (F7), and assistant sessions pause for minutes while the person reads. How much scanner traffic the host name gets is unknown until it runs. So the default stays 300, and the runbook makes the parameter the first lever once the budget shows that scanners are the cost.
- **Bounds.** No limit is documented (F5). 60 is the lowest value seen accepted, and below it the 15-second sampling of the HTTP rule dominates. 1800 stops a typo from keeping a replica up for hours.
- The cool-down only governs the last replica (F5). Replicas above one go after the platform's 300-second scale-down window; `mcpMaxReplicas` stays the bound for them.
- `az containerapp update --image` of the workflow patches the containers only, and the CLI's Container Apps client has used an API version with `cooldownPeriod` since 2025, so a deployment of an image keeps the value.

### 4. What is not done, and why

- **An Azure Monitor alert on `Replicas`.** It would warn within hours and would cost nothing (one time series, F10). But it isn't documented whether the metric reports 0 or nothing while the app is at zero, and every candidate rule (an average over a day, a minimum over hours) means something different in each case. It also needs an action group whose email receiver confirms a one-time passcode. The budget measures the money itself and needs no new resource type. The metrics stay the diagnosis tool of the runbook, read in a way that works in both cases (the maximum per minute, a minute without data counting as no replica).
- **Answering non-MCP paths cheaply in the server.** By the time the server answers, the replica is up and billed for the cool-down (F3, F4). A cheaper answer saves nothing.
- **Rule-based routing** of only `/mcp` and `/.well-known/*` to the app (F11). It moves the custom domain from the app to the environment, changes its DNS record from a CNAME to an A record, leaves the app's default host name as a way to wake it unless the app's ingress becomes internal, and has open bugs. It is the next step if the budget shows that scanners cost more than a shorter cool-down saves. It is written down in the decisions as that option.
- **`minReplicas` 1.** It removes cold starts and bills the replica at the idle rate between requests (F3): about 4 to 6 USD a month, whether scanners come or not. It is the better deal only if scanners keep the replica warm more than about half of the month. It is written down as an option, not built.
- **A lower `mcpMaxReplicas`.** A second replica starts only above 20 concurrent requests and a third above 40, and they go away after the 300-second scale-down window. The cap stays 3; the decisions name it as the worst case.
- **A Cost Management anomaly alert.** It looks at the subscription's daily total, not a meter, and nothing documents whether it would notice 0.65 USD a day.

### 5. Documentation

- `docs/Platform/Deployment/decisions.md`, "The MCP server": the cost as the range of section 1, the cold start of 10 to 30 seconds, and two new entries: the budget instead of an alert rule (section 2, section 4) and the cool-down kept at 300 seconds as a parameter, with rule-based routing and `minReplicas` 1 as the later options.
- `docs/Platform/Deployment/runbook-mcp-server.md`: phase 4 sets `mcpBudgetStartDate` and expects the new budget in the what-if; phase 9's cost check reads the budget and the meter category; phase 10 lists the budget among what stays; a new phase 11 **"The cost of the MCP server"** says what the budget's emails mean, how to see where the warm time goes (replica-minutes of the month from the `Replicas` metric, requests by status class from the `Requests` metric), and what to change: `mcpCooldownSeconds`, `mcpBudgetAmount`, `mcpMaxReplicas`, and the ingress off as the last resort.
- `docs/Platform/Deployment/cookbook-azure-production.md`: step 17a gets the budget (portal steps, as for the budget of step 2) and the cool-down; step 18's row for Container Apps gets the range and "What raises it" gets a warm MCP replica.
- `docs/Platform/Storyteller/mcp.md`, "Limits of the hosting": the cool-down and the cold start of 10 to 30 seconds.
- `src/Platform/Deployment/README.md`: the parameters `mcpBudgetStartDate`, `mcpBudgetAmount` and `mcpCooldownSeconds`, and the second budget in "What is where".
- `CLAUDE.md`: the Deployment row names the MCP server's budget.
- The comments of `modules/mcp.bicep` (the header, the ingress, the scale block) say the same.

The existing specs and reviews stay as they are.

### 6. Validation

- `az bicep build` of `main.bicep` and `domains.bicep` and `az bicep build-params` of both parameter files, as `.github/workflows/Platform.Deployment.yml` runs them: no new warning.
- The `fail()` checks: a build of a parameter file with `mcpEnabled = true` and no `mcpBudgetStartDate` must fail with the message, and one with a valid date must build.
- No Azure deployment: production runs the MCP server only from its runbook's phase 4.

### 7. Acceptance criteria

1. With `mcpEnabled`, `main.bicep` creates `prod-2s-mcp-budget`: 1 USD a month by default, filtered on the meter category `Azure Container Apps`, alerts to the owner at 50 % and 100 % of the actual cost and at 100 % of the forecast. Without it, nothing new is created.
2. A run with `mcpEnabled` and no valid `mcpBudgetStartDate` fails before anything is written, and names the parameter.
3. The Container App's `cooldownPeriod` comes from `mcpCooldownSeconds` (default 300, 60 to 1800).
4. No doc of the deployment or of the MCP server claims a flat 0 USD or a cold start of a few seconds: each states the range of section 1 and names the budget.
5. The runbook says what to do when the budget warns, with commands that work whether the `Replicas` metric reports zeros or nothing.

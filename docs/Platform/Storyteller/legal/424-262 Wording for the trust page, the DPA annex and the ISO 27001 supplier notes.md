# Wording for the trust page, the DPA annex and the ISO 27001 supplier notes

**Date:** 2026-11-17

Linear: [424-262](https://linear.app/424fun/issue/424-262/refuse-workos-impersonation-tokens-in-the-api-and-lock-down-who-can). Source: [decisions.md](../../Deployment/decisions.md#who-can-sign-in-as-a-user-the-workos-trust-boundary), "Who can sign in as a user (the WorkOS trust boundary)". Keep these texts in step with that entry.

---

## 1. Trust page: section "Sign-in and identity"

### Who verifies who you are

Sign-in to the 2S Platform is provided by **WorkOS, Inc.** (USA). WorkOS verifies who a person is, and a valid WorkOS sign-in lets that person into the 2S Platform. **What a person may do with any data is decided only by the roles that the 2S Platform stores in the European Union.** WorkOS has no part in that decision.

### How we limit who can sign in as you

- **No impersonation.** WorkOS's user impersonation feature is turned off. The 2S API also refuses every impersonated session, so it can't be used even if the feature were turned on.
- **A locked-down dashboard.** Only named members can use the WorkOS dashboard, and each of them uses multi-factor authentication.
- **Email changes are logged.** When your account is read (the admin application does it when it opens, the CLI in its account commands, and any client can call it directly) or you sign in through the MCP server with an email other than the one the 2S Platform has stored for you, the 2S API logs it. This needs the sign-in to carry your email; other calls of the API are not compared. Changes on administrator and owner accounts are logged separately.

### The risk that remains

WorkOS itself could issue a valid sign-in for any person, for example if its signing keys were compromised or under a legal order. The 2S Platform can't tell such a sign-in from a real one. This risk is accepted and disclosed here.

### If you don't accept WorkOS

We can run a dedicated deployment that signs people in with your own identity provider instead:

- a **Keycloak** server, which can be hosted in the European Union;
- your own **Microsoft Entra ID** tenant.

A deployment uses one identity provider. In a dedicated deployment the admin web application is not available; administration works through the API.

---

## 2. DPA annex: sub-processor entry and safeguards

### Sub-processor

| | |
|---|---|
| **Sub-processor** | WorkOS, Inc., United States |
| **Service** | Authentication of users (sign-in, sessions, user directory) |
| **Personal data** | Email address, name, authentication factors, sign-in metadata (IP address, user agent, time) |
| **Data subjects** | Users of the customer who sign in to the 2S Platform |
| **Location** | United States |
| **Transfer mechanism** | *[to be filled in by the DPA owner: e.g. EU–US Data Privacy Framework, or Standard Contractual Clauses with WorkOS's DPA]* |
| **Authorization** | WorkOS has no part in authorization. Roles and all customer data stay with the 2S Platform in the European Union. |

### Technical and organizational measures for this sub-processor

1. WorkOS's user impersonation is turned off in every environment. The 2S API refuses impersonated sessions, which carry an `act` claim, with HTTP 401. It logs each refusal of a token that is otherwise valid; a token already refused for another reason, such as an expired one, is not logged as an impersonation.
2. Access to the WorkOS dashboard is limited to named members, each with multi-factor authentication. Where SSO is used instead, its identity provider must enforce MFA. The membership is reviewed at each production verification.
3. When the email in a sign-in differs from the one stored on the account, the 2S API logs it. It does this only when the account is read directly (`GET v1/access/account`: the admin application when it opens, the CLI's account commands, or any direct call) and at MCP sign-in, and only when the sign-in carries the email and the account has a stored email of its own (not for an account a seed created before its person signed in, nor for a delegated request). Changes on administrator and owner accounts are logged separately.
4. These logs, including the email addresses involved, are kept in Azure Application Insights in the EU for 30 days. They are not reported as events to the error-reporting service (Sentry, EU). They can reach it as breadcrumbs of an error event of the same request, with email addresses removed before they are sent.

### Residual risk

WorkOS itself could issue a valid sign-in for any user, through a compromise of its signing keys or under a legal order, and the 2S Platform cannot tell such a sign-in from a real one. Customers who do not accept this can use a dedicated deployment with their own identity provider: Keycloak, which can be hosted in the EU, or their own Microsoft Entra ID tenant. Such a deployment uses one identity provider, and the admin web application is not available in it.

---

## 3. ISO 27001 supplier notes: WorkOS entry

| | |
|---|---|
| **Supplier** | WorkOS, Inc. (USA). AuthKit: user authentication for the 2S Platform. |
| **Criticality** | High. Path 3 (the residual risk) is accepted: see `decisions.md`. |
| **Controls** | Impersonation off in every environment. Named dashboard members with MFA. The API refuses impersonation tokens (PR #199). The API logs changed user names. |
| **Review cadence** | At each production verification (runbook step 12) |

### Review record

| Date | Reviewer | Check | Result | Method |
|---|---|---|---|---|
| 2026-11-15 | WorkOS Admin | Impersonation, Production | Off (`isImpersonationEnabled: false`) | WorkOS API, read only |
| 2026-11-15 | WorkOS Admin | Impersonation, Staging | Off | WorkOS API, read only |
| 2026-11-15 | WorkOS Admin | Dashboard members | Named members only, each with MFA, not managed by a directory | WorkOS API, read only |
| 2026-11-17 | WorkOS Admin | Impersonation, Production | Off | WorkOS API, read only |
| 2026-11-17 | WorkOS Admin | Impersonation, Staging | Off | WorkOS API, read only |
| 2026-11-17 | WorkOS Admin | Dashboard members | Named members only, each with MFA, not managed by a directory | WorkOS API, read only |
| 2026-11-17 | WorkOS Admin | End-user MFA, Production | Required (`mfaEnabled: Required`; it was Off on 2026-11-15) | WorkOS API, read only |

**Open item:** keep at least two named dashboard members, so the account doesn't depend on one person.

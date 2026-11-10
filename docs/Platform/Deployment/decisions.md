# Decisions That Cannot Be Changed Later

The production environment of the 2S Platform is created once and lives on (see the spec [Azure Production Environment of the 2S Platform](specs/2026-11-09%20Azure%20Production%20Environment%20of%20the%202S%20Platform.md)). Most of its settings can change at any time. The ones below cannot, or only at a high price: a migration of the data, new tokens and certificates for every machine and user, or a new resource. Fix them before the first deployment.

Each entry says what to decide, why it is final, a recommendation, and what was decided (2026-11-09).

## Decided on 2026-11-09

| # | Decision |
| --- | --- |
| 1 | API domain `api.2s.42for.net` (`MachineAuth:Federation:PublicBaseUrl` = `https://api.2s.42for.net/api`) |
| 2 | Trust domain `2s.42for.net` |
| 3 | AuthKit in WorkOS production from day one |
| 4 | Custom AuthKit domain `auth.42for.net` and **one WorkOS production environment for every product of 42for.net**: one login and one account everywhere, one WorkOS Application per product (see below) |
| 5 | Sweden Central |
| 6 | One Cosmos DB account, the free tier on |
| 7 | `prod-2s-42fornet-cdb` |
| 8 | One Premium vault `prod-2s-42fornet-kv`, purge protection on, RBAC mode, a delete lock |
| 9 | One shared container `organizations` for every organization, with the hierarchical partition key `/Organization`, `/PartitionKey`, instead of a container per organization; confirmed by the emulator probe first ([cookbook](../Storyteller/cookbooks/hierarchical-partition-key-probe.md)) |
| 10 | Test organizations only, never a customer's organization for tests |
| 11 | The owner's WorkOS production account exists before the first run of DbCreator, as `baseAccount:id` |
| 12 | One BYOK application, `2S Platform Customer Keys` |
| 13 | Admin UI domain `app.2s.42for.net` |
| 14 | Subscription `prod-42fornet-sub` |
| 15 | Production data is never copied to a development machine |

Naming: `{environment}-{product}-{name}-{type}`, the resource group with the region (`prod-2s-eusec-rg`), `42fornet` in the globally unique names, and the storage account without hyphens (`prod2s42fornetsa`).

Open points from these answers:

- **One subscription for the products of 42for.net.** A subscription has one free-tier Cosmos DB account and one Flex Consumption free grant. 2S takes both in `prod-42fornet-sub`; another product there gets a paid Cosmos DB account, or the same account with its own database.
- **One WorkOS environment for every product** (decided 2026-11-10, the plan below).

### One WorkOS environment for the products of 42for.net

**Goal:** one login and one account for everything of 42for.net. Users and organizations belong to a WorkOS environment, so separate environments would give separate accounts: every product signs users in through the one production environment and `auth.42for.net`.

**What one environment shares.** One issuer (`https://auth.42for.net`) and one JWT template, so one `aud` for every token the environment issues. The 2S API accepts a token by its issuer, `aud` and signature (`Auth:AuthKit:Issuer`, `Audience`), and gives every signed-in user `User.Impersonation`. A token that another 42for.net product gets for a user is therefore also a full token of that user for the 2S API: if that product leaks or hands its tokens on (a compromised front end, an assistant or MCP server, a log), the leak reaches 2S too.

**The plan.**

1. **One environment, one custom domain** `auth.42for.net` (decisions 3 and 4), shared by every product.
2. **One WorkOS Application per product** ([Applications](https://workos.com/docs/authkit/applications)): each has its own client id, redirect URIs, session settings and branding, while users and organizations stay shared. Password resets, sign-in flows and invitation links route to the application they were started from; invitations sent from the dashboard use the **default application**, which is the 2S Platform (its admin UI and `sform`).
3. **`aud` stays `https://2s.42for.net`** in the one JWT template while 2S is the only product (it must equal `Auth__AuthKit__Audience`). The template is per environment, so it can't give each product its own audience.
4. **Before a second product signs users in**, the 2S API also checks the token's `client_id` claim (the application the user signed in to) against an allow-list of the 2S applications, e.g. a new setting `Auth:AuthKit:AllowedClientIds`. Tokens of other products are then refused, while the user still signs in once. That setting doesn't exist yet: a Storyteller change for the second product, not for the launch of 2S.
5. **Check with the second product** that a session of one application signs the user in to the others without a new password prompt; the WorkOS docs describe "a unified login experience" but don't say it outright.

Accepted exposure:

- **The Key Vault is reachable from the public internet** (`publicNetworkAccess` on, network rules `Allow`). Every request still needs an Entra token with an RBAC role on the vault; the Flex Consumption API has no fixed outbound address to allow, and a virtual network is not part of this phase. When one comes (the NAT gateway for BYOK, spec section 13), restrict the vault to it with an IP firewall or a private endpoint: a setting, not a decision that can't change.

## Must be decided before the first deployment

| # | Decision | Why it can't change later | Recommendation |
| --- | --- | --- | --- |
| 1 | **The API domain** (`api.<domain>`) | It is `MachineAuth:Federation:PublicBaseUrl`: every federated machine's rule pins the `TokenEndpoint` built from it as its audience, and clients, pipelines (`STORYTELLER_ENDPOINT`) and SDK settings of customers hold it. A new domain breaks every federation rule and every customer configuration | A domain you will own for good, for the product rather than a person: e.g. `api.2s.42for.net` or a product domain bought now. Never the `*.azurewebsites.net` name |
| 2 | **The trust domain** (`MachineAuth:TrustDomain`) | It derives the issuer (`urn:storyteller:{trust domain}`) and the audience of every Storyteller token, machine and user. Changing it rejects every token already issued and every token configuration of customers | A stable, production-only value, e.g. `2s.42for.net`; never the default `2s.platform` (used locally) and never the value of a future development environment |
| 3 | **The identity provider and its production environment** (AuthKit, WorkOS production) | Account ids are the provider's user ids (`sub`). Members, roles, invitations, audit stamps and the base account are keyed by them. Moving users to another provider or another WorkOS environment gives them new ids, and every membership has to be migrated | AuthKit in **WorkOS production** from day one; staging stays for local work. Testers sign up in production too |
| 4 | **The custom AuthKit domain** (`auth.<domain>`) | It is the issuer the API checks (`Auth:AuthKit:Issuer`), and the domain users see when they sign in; changing the issuer invalidates every session and needs a coordinated change of API and UI | A subdomain of the same domain as #1, set up in WorkOS before the first sign-in |
| 5 | **The region** (Sweden Central) | Cosmos DB, the vault and the storage account can't move between regions. A move means new resources and a migration of the data and of every key | Sweden Central (data residency in Sweden) |
| 6 | **The Cosmos DB free tier** | It is chosen when the account is created and can't be added later; there is one per subscription | On, in the production subscription's only Cosmos account. Don't create any other Cosmos account in this subscription first, or the free tier is taken |
| 7 | **The Cosmos account name** | Account names are global and final; the endpoint is in the connection string | `cosmos-2s-prod-sdc-<suffix>` |
| 8 | **The Key Vault, and purge protection** | Every project's key-encryption key lives in it, and the envelopes in Cosmos name its keys by URI: a new vault means re-wrapping every secret. Purge protection can't be turned off once on, which is the point: a deleted key would lose every secret of its project | One Premium vault, purge protection **on**, RBAC mode, a delete lock; its name fixed in the parameter file |
| 9 | **The database layout** (one database `42.Platform.2S`, shared throughput) | Changing it means moving every item, and a hierarchical partition key can only be set when a container is created. A container per organization stops at 25 containers in a shared database and at 500 per account (a limit that can't be raised) | One shared container `organizations` with the key `/Organization`, `/PartitionKey`, before the first deployment |
| 10 | **The organization and project names you create** | Organization and project names are part of every key, container name (`org.{organization}`), configuration path, Key Vault key name (`kek-` + a hash of `{organization}/{project}`) and the HYOK `2s` header; there is no rename | Pick the final names of the first customers' organizations; never use a customer's organization for tests |
| 11 | **The base account** (`baseAccount:id` of DbCreator) | It becomes the owner of the platform's first organization and project; it is the WorkOS production user id of the owner | Your own WorkOS production user, created before the first run of DbCreator |

## Expensive to change later

| # | Decision | Why | Recommendation |
| --- | --- | --- | --- |
| 12 | **The names of the BYOK application and its publisher** | Customers consent to the multi-tenant app and grant its service principal a role on their key. A new application means every BYOK customer consents and grants again | One app, `2S Platform Customer Keys`, in your tenant; start publisher verification early |
| 13 | **The admin domain** (`admin.<domain>`) | It is the AuthKit redirect URI, the CORS origin, and the address users bookmark; it can change, but in WorkOS, the API and the UI together | Same parent domain as #1 |
| 14 | **The subscription and tenant** | Moving resources between subscriptions is possible, but the free tier, role assignments and the managed identity's federated credential (BYOK) are bound to them | The upgraded free-account subscription as `sub-2s-prod`, in your own Entra tenant |
| 15 | **The signing key and certificate authority stay in production** | They are created in the `core` container on first use. Whoever reads it can mint tokens, so it must never have been copied to a development machine or shared with a test environment | Never copy production data to a local emulator; development gets its own account later |

## Can change any time (no need to decide now)

Function App plan and instance size, always-ready instances, Static Web App plan, the Log Analytics cap, Sentry and PostHog projects, which machine credential kinds are turned off (`MachineAuth:DisabledCredentialKinds`), the projects' key/value policies, adding the development subscription, the MCP server, the NAT gateway and mTLS.

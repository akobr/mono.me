# 2S Platform: critical evaluation of the usage study (PR #98)

*Prepared 16 Oct 2026 for Ales Kobr. Subject: `docs/Platform/study-usage.md` (1,678 lines) at commit `6ff7ebd` (akobr/mono.me PR #98, docs-only, opened 16 Oct 2026 ~15:52 CEST). The commit carries an AI co-author trailer. All 21 sections were read; code claims were verified against a shallow clone of `6ff7ebd`.*

Labels used throughout: **[Study]** = what the study says · **[Code]** = verified in the repository at `6ff7ebd` (path given) · **[Market]** = public fact with link · **[Opinion]/[Estimate]** = my judgement or assumption.

---

## 0. Verdict (short)

[Opinion] The core is genuinely differentiated: a typed catalog (subject × responsibility × context) that carries inherited, schema-checked, versioned configuration, with views, validity windows and HSM/BYOK/HYOK secrets. No single competitor bundles that. But it is not a product yet: pre-1.0 ("40% done"), no configuration provider in any language, no change signal, no self-host artifacts, no LICENSE despite an "open-source" claim, machine scoping not enforced (G7, confirmed in code), Azure-only, solo founder. The study is an excellent **engineering/integration** study with real positioning and technical-risk sections, but it contains **no business case**. Realistic outcome: a profitable niche (low six figures ARR by year 3); ~$1M ARR only with a narrow wedge executed very well.

---

## 1. What the study claims

**Positioning [Study §1, §17].** A "hub that interoperates through standards", not a vault, flag platform or developer portal. §17 states that no product combines, on one graph: typed relations, configuration attached to relation records, a deterministic layered merge with per-owner schemas, validity windows and time zones, schedules bound to executions, and (once Supervisor exists) health supervision. Each adjacent category covers two or three of these. §17 has a category table (cloud config services, secret managers, feature flags, developer portals, CMDBs, SaaS control planes, schedulers, config languages) with "what they do best / what 2S adds / how to interoperate", plus lessons to borrow: per-property provenance, lockable values, immutable snapshots, two read permissions, CDN distribution, and "stay small" (HCP Vault Secrets EOL Jul 2026, Hypertune shut down Aug 2026: "narrow configuration SaaS products are fragile").

**Use cases [Study §16].** Grouped by who asks:
- Product/customer ops: multi-tenant SaaS control plane (prior art AWS SBT, Google App Lifecycle Manager), entitlements (Stripe Entitlements, Stigg, Schematic), progressive delivery via views/rings, support consoles, licensing and air-gapped installs (signed JWS bundles; Replicated), brands/markets/localization, business rules and pricing (JSON Logic).
- Engineering/ops: system map via typed links between responsibilities (not modelled today), Backstage/Port feeds ("feed the portals; do not build one"), observability and alert routing, CODEOWNERS generation, CI/CD matrices and promotion, a job catalog, data/ML pipelines, an MCP server for AI agents.
- Governance: DORA register of information, NIS2 asset inventory, GDPR Art. 30, data residency, CRA SBOM links ("whether an export is legally sufficient needs legal review"), FinOps tags/FOCUS.
- Beyond software: device twins, stores and edge sites, game live-ops.
- §16.5 "where the platform is the wrong tool": sessions, high-churn values, coordination, leased dynamic secrets, large artifacts, values above 24,576 B per key/value or 2 MB per Cosmos item.

**Integration patterns [Study §3–15].** A shared contract (`STORYTELLER_*` env vars, flattening rules, precedence, refresh, resilience, secrets, catalog/informer, CloudEvents for units, OTel attributes, schema-generated types, a conformance suite); P1 in-process providers (.NET reference, then Spring, Node, Python, Rust, Go); P2 local agent; P3 protocols (OFREP, Spring Cloud Config, Argo CD plugin generator, Pulumi ESC adapter, ESO webhook); P4 projection into native stores (modelled on Infisical syncs / Vault Secrets Sync); P5 render in CI; P6 CloudEvents. A Kubernetes operator, Vercel integration, Terraform provider, and keyless federation (Storyteller as OIDC issuer).

**Capabilities [Study §18].** C1–C24 with S/M/L effort (S = days, M = 1–3 weeks, L = more): e.g. C2 invalidation fix (S), C7 key-scoped machines (M), C8 federation (M–L), C13 projection engine (L), C18 Scheduler MVP (L), C19 Supervisor MVP (L), C23 self-host/multi-region (L).

**Roadmap [Study §19].** Phase A "Consumable" (0.9.x: C2, C5, C6, C9, C10, C24); B "Native in .NET" (1.0: C1, C4, **C7**, C8, .NET provider, agent, conformance); C "Events and jobs" (1.1: C3, C18, C11, K8s operator); D "Every ecosystem" (1.2: C12, C13, C21, five languages, cloud projections, Terraform); E "Understanding the system" (1.3: C15, C16, C19, C20, C14).

**Risks [Study §20].** Platform on the critical path (cold starts, one region, Cosmos partitions); blast radius of a central store (G7, hash can't prove integrity); surprising merge semantics (arrays only grow, null never removes); two sources of truth after projection; **scope creep** ("portal, flag platform, vault, scheduler and observability backend are each a product category with strong incumbents"); polling cost; naming limits. Open questions: evaluate flags or only serve them? OTLP for Supervisor? Initiator executes or projects? Infra as separate project? **"Is the SaaS offering expected to host machines' reads at scale … or is the main target self-hosted?"**

**Self-reported defects [Study §2.4].** G1–G13 and C24 (see §2 below).

**What the study does not contain [Study, all sections].** No pricing, packaging, ICP/persona, buyer, market sizing, go-to-market, competitor pricing, revenue model, cost-to-serve figures, team/capacity plan, license/open-core decision, certification plan, or liability model.

## 2. Verification against code

| Claim | Status | Evidence |
|---|---|---|
| **G7** machine `annotationKey` never enforced | **[Code] Confirmed, with a nuance** | `Api.Functions/src/Security/MachineAuthenticationMiddleware.cs:231` adds `new Claim("annotationKey", annotationKey)`. Repo-wide grep finds **no reader** of that claim in Api.Functions, Backend.* or Access.*; the only `FindFirst…annotationKey` hit is in the legacy `Api.Web/src/Controllers/AnnotationsController.cs` (no configuration endpoints). Nuance: the claim is only populated for **machine certificates** (`certResult.AnnotationKey`, lines 128/161); API-key-only and shared-cert+API-key logins never carry it at all, even though `AnnotationKey` is stored by every machine-access service (ApiKeys, AuthKit, AzureAd, Keycloak, Certificates). Authorization is project + role scope only (`MachineScopeClaims.GetRoleClaimsForScope`). |
| **G4** first write to an ancestor doesn't invalidate descendants | **[Code] Confirmed** | `Backend.CosmosDb/src/Configuring/CosmosConfigurationService.cs`: `WriteConfigurationAsync` create branch (lines 355–385, `existingConfiguration is null`) does `CreateItemAsync` + audit and returns; `InvalidateConfigurationsAsync` is called only on the update path (line 451) and other paths (502, 604, 618). |
| **G3** hash ignores bound values | **[Code] Confirmed** | Same file: `GetResolvedConfigurationInternalAsync` (line 806) loads the raw/effective config, resolves bindings **in place** into `config.Content`, and returns the same object, so `Hash` stays `CalculatedContentHash` = MurmurHash3 of the merge (lines 951–955, 975–992; `Mapping.cs:17`). |
| Storyteller core (annotations, inheritance, schemas, views, transfer) | **[Code] Exists, substantial** | `src/Platform/Storyteller/` (Backend.Core, Backend.CosmosDb, Api.Functions, Binding.*, DbCreator); docs in `docs/Platform/Storyteller/`. |
| HSM/BYOK via Azure Key Vault; HYOK client-side | **[Code] Present** | `KeyValues.Azure.KeyVault/src/` (CustomerKeyProvider.cs, KeyVaultKeyProvider.cs) with LowkeyVault tests; `KeyValues.ClientEncryption`. |
| Supervisor is scaffolding | **[Code] Confirmed** | `Supervisor/Api.Functions/src/SimpleFunctions.cs` returns `"Hello, World from azure function!"`. |
| Scheduler is an empty host | **[Code] Confirmed** | `Scheduler/Api.Functions/src/` has only Program.cs + host files. |
| SDKs | **[Code] Generated API clients, not config providers** | Sdk.NSwag, sdk.typescript (unpublished), plus unused Sdk.Kiota / RestSharp leftovers (§2.2). G1 stands. |
| Binding compute safety (after PR #57) | **[Code/Docs] Hardened** | `docs/Platform/Storyteller/binding.md`: MaxSteps 1M, 64 MiB, 500 ms, depth 512, envelope depth 32, 256 envelope evaluations; vendored JsonE.Net fork (`Binding.JsonE`). |
| "Open-source, deploy on-premise" (`docs/42for.net/platform/introduction.md`) | **[Code] Contradicted** | No LICENSE file at repo root; GitHub license = null. No current self-host artifacts (G12). |
| Team | **[GitHub]** | Contributors API: akobr 755 contributions; only other contributor is CodeRabbit (bot). |

PR #57 ("object-level binding", merged 9 Oct 2026, 155 files, +17,345) **[GitHub]**: an automated reviewer showed exponential envelope fan-out (n=16 → 15 s, 6.6 MB; fixed in 9cebf30 with a 256-evaluation budget) and unbounded single envelopes (`len(range(0,20000000))` ~4 GB; `$map` over 5M ~10 GB; 26 nested `$let` 69 s / ~127 GB; JSON Logic `reduce`/`cat` OOM), and that "JSON-e bounds its own loops" was false. [Opinion] Fixes look right, but evaluating user-supplied expressions in a shared multi-tenant worker needs CI fuzzing and per-tenant quotas long-term.

## 3. Market and differentiation

| Product | Category | Pricing (verified) | Strength vs 2S | Where 2S differs |
|---|---|---|---|---|
| Azure App Configuration | Hyperscaler config | Free 1k req/day; Standard ~$1.20/store/day (~$36/mo), 200k req/day incl., $0.06/10k overage. [pricing](https://azure.microsoft.com/en-us/pricing/details/app-configuration/), [GA note](https://github.com/Azure/AppConfiguration-Announcements/issues/5), [analysis](https://c3x.dev/blog/azure-app-configuration-cost/) | Native .NET provider, KV refs, flags, snapshots, SLA | No catalog, inheritance graph, per-owner schemas, validity windows, HYOK |
| AWS AppConfig / Parameter Store | Hyperscaler config | AppConfig $0.0000002/request + $0.0008/config received; Parameter Store standard free, advanced $0.05/param/mo. [pricing](https://aws.amazon.com/systems-manager/pricing/) | Near-free, safe deployments | As above; 2S is Azure-only in practice |
| LaunchDarkly | Feature flags | Developer $0; Foundation $10/service connection/mo + $8.33/1k client MAU. [pricing](https://launchdarkly.com/pricing/); >$200M ARR ([Yahoo](https://finance.yahoo.com/technology/ai/articles/launchdarkly-surpasses-200-million-arr-120000198.html)) | Experiments, SDKs everywhere, trust | Don't compete; OFREP |
| ConfigCat | Feature flags | Free; Pro $110/mo; Smart $325; Enterprise $900; Dedicated $4,500; unlimited seats; ISO 27001. [pricing](https://configcat.com/pricing/) | EU, simple, certified | Pricing/positioning template |
| Flagsmith / Unleash | OSS flags | Flagsmith Start-Up $40–45/mo, Scale-Up $250–300 ([pricing](https://www.flagsmith.com/pricing)); Unleash $75/seat/mo, OSS free, SOC 2 II ([pricing](https://www.getunleash.io/pricing)) | Open-core playbook | 2S claims open source but has no license |
| HashiCorp Vault (HCP) | Secrets | Dedicated Essentials Small $1.58/h (~$1,152/mo) + ~$72.92/client/mo; HCP Vault Secrets EOL 1 Jul 2026. [IBM](https://www.ibm.com/support/pages/hcp-vault-secrets-end-life), [Infisical analysis](https://infisical.com/blog/hashicorp-vault-pricing) | Dynamic secrets | 2S references vaults; its own KV partly competes |
| Doppler | Secrets + config | Developer free 3 users then $8/user; Team $21/user/mo (inheritance, SAML, syncs). [pricing](https://www.doppler.com/pricing). ~$10M ARR claim unverified | **Closest analogue** | 2S adds schemas, catalog graph, views, HYOK |
| Infisical | OSS secrets | MIT core; free 5 identities; Pro $20/identity/mo; Advanced $40. [pricing](https://infisical.com/pricing); $16M Series A ([Fortune](https://fortune.com/2025/06/06/infisical-raises-16-million-series-a-led-by-elad-gil-to-safeguard-secrets/)) | Free self-host, funded, syncs, K8s operator | Study's P4 copies Infisical's sync design |
| Azure Key Vault | Secrets | $0.03/10k ops; HSM keys $1–5/mo; Managed HSM ~$3.20/h. [analysis](https://infisical.com/blog/azure-key-vault-pricing) | Trust, cheap | 2S uses it underneath (complement) |
| Pulumi ESC | Config + secrets environments | 25 secrets free then $0.50–1.00/secret/mo; plaintext config free; $0.10/10k API calls; platform $40/$400/$2,000/mo; SOC 2 II. [pricing](https://www.pulumi.com/pricing/) | **Closest conceptual analogue** | Richer domain model in 2S; Pulumi has distribution |
| Spring Cloud Config / Consul / etcd | OSS infra | Free | Built-in | 2S adds UI, schemas, audit; SCC adapter is a bridge |
| CUE / Pkl / Jsonnet / Kustomize | Config languages | Free | Build-time, no runtime dep | P5 competes directly |
| Port / Backstage / Spotify Portal | IDPs | Port free ≤15 seats, $30–40/seat ([pricing](https://www.port.io/pricing)); Spotify Portal $35k/yr/unit ([AWS Marketplace](https://aws.amazon.com/marketplace/pp/prodview-62g4qdx6bifx2)) | Own the catalog story | 2S's catalog is executable; study rightly says "feed the portals" |

**Market size [Market].** Secrets management $4.22B (2025) → $8.05B (2030), 13.8% CAGR ([Mordor](https://www.mordorintelligence.com/industry-reports/secrets-management-solutions-market)); key & secrets management $1.34B (2024) → $4.87B (2029) ([Frost & Sullivan](https://store.frost.com/key-secrets-management-global-2025-2029.html)); feature management $349M (2024) → $590M (2031) ([Valuates](https://reports.valuates.com/market-reports/QYRE-Auto-19J5790/global-feature-management-software)). [Opinion] Category TAM is irrelevant at this stage; what matters is whether one segment's pain is sharp enough to pay a newcomer.

**Real differentiation [Opinion].** The study's §17 claim holds up against the table: nobody combines the relation graph + merge + schemas + validity + secrets. But each piece alone loses to the specialist, so the pitch must be "system of record for what runs where, for whom, configured how", not "another config store".

## 4. Could it succeed? Conditions [Opinion]

A profitable niche/lifestyle SaaS: yes, plausibly. Category leader without funding and a team: no. Conditions:
1. **Trust first.** No tenant with real secrets until G7/G4/G3 are fixed, a pentest is done, and SOC 2 Type I (or ISO 27001 for an EU wedge) is under way. SOC 2 year one ≈ $25–50k; Type II takes 8–12 months ([Patotski](https://patotski.com/blog/soc-2-cost-for-startup-saas/), [Drata](https://drata.com/learn/soc-2/cost)).
2. **One wedge, one stack.** .NET/Azure `IConfigurationProvider` + change signal, in weeks.
3. **License and self-host.** Answer the study's own open question 5 (SaaS vs self-host) as a business decision, not only a technical one.
4. **Design partners before breadth.** Phases C–E are each multi-month for one person.
5. **Flat per-org/project pricing**, not per-request/per-secret, to avoid fighting hyperscaler near-zero unit prices.

## 5. Customers, ranked by fit [Opinion]

| Rank | Segment | Size | Buyer | User | Why 2S | Main objection |
|---|---|---|---|---|---|---|
| 1 | .NET/Azure B2B SaaS with per-tenant config and entitlements | 20–500 staff | CTO / VP Eng | Backend and platform engineers | Tenant × module × stamp maps directly; study §16.1 control-plane use case | "App Config + Key Vault is good enough" |
| 2 | EU regulated mid-market (fintech, insurance, health, energy) | 200–2,000 | CISO / head of platform | Platform, SRE, compliance | HYOK, audit, catalog as DORA/NIS2 register (§16.3) | No certification, solo vendor, legal sufficiency unproven |
| 3 | Agencies / ISVs with white-label or multi-brand estates | 10–200 | Founder / CTO | Delivery engineers | Templates and inheritance across many estates (§13, §16.1 brands) | Price sensitivity |
| 4 | Device / edge fleets | varies | Head of engineering | Embedded/ops | Units, validity windows, twins (§16.4) | Needs agent and offline bundles (not built) |
| 5 | AI-agent configuration / MCP | early | — | — | Fashionable (§16.2) | No budget line yet; distraction |

## 6. Revenue scenarios, years 1–3 [Estimate]

All figures are **my assumptions**, benchmarked on public prices. Year 1 starts at v1.0 GA with billing. Ending ARR, US$.

Assumed price points: **Team** $300/mo = $3.6k ARR (between ConfigCat Smart $325 and Pulumi Pro $400); **Business** $1,000/mo = $12k ARR (SSO, audit export, HYOK; near ConfigCat Enterprise $900); **Enterprise/self-host** $40k ARR. Free→paid conversion 3–5% (dev-tool median ~5%, [Lenny's](https://www.lennysnewsletter.com/p/what-is-a-good-free-to-paid-conversion)); 4% used for the funnel column.

| Scenario | Year | Team | Business | Enterprise | Ending ARR | Free orgs implied @4% |
|---|---|---|---|---|---|---|
| Pessimistic | 1 | 3 | 0 | 0 | ~$11k | ~75 |
| | 2 | 10 | 0 | 0 | ~$36k | ~250 |
| | 3 | 20 | 0 | 0 | ~$72k | ~500 |
| Base | 1 | 6 | 2 | 0 | ~$46k | ~200 |
| | 2 | 18 | 6 | 1 | ~$177k | ~625 |
| | 3 | 35 | 12 | 3 | ~$390k | ~1,250 |
| Optimistic | 1 | 10 | 4 | 1 | ~$124k | ~375 |
| | 2 | 32 | 14 | 4 | ~$443k | ~1,250 |
| | 3 | 70 | 30 | 10 | ~$1.01M | ~2,750 |

Key assumption per scenario: **Pessimistic**: only Phase A ships, no certification, organic discovery. **Base**: Phase A + B ship (incl. .NET provider, agent, C7), self-host package, one wedge, steady content. **Optimistic**: SOC 2 in year 2, Azure Marketplace listing, 1–2 SI partners.

Not modelled: churn (assume 10–20% logo churn for small teams), SOC 2 ($25–50k), Azure hosting cost-to-serve (the study notes polling load in §20.6 but gives no numbers), and a possible json-everything Open Source Maintenance Fee once revenue ≥ $10k/yr (verify current terms). Sanity check: Doppler, funded with a team, is estimated at ~$3–10M ARR after several years (unverified), so $1M ARR solo in 3 years would be a strong result.

## 7. Gaps in the study [Opinion]

Updated after reading §16–21. The study **does** cover positioning (§17), technical risks and scope creep (§20), a phased roadmap with rough efforts (§18–19), and where the platform is the wrong tool (§16.5). What it still lacks:

1. **No business case.** No ICP, persona, buyer, pricing, packaging, market size, GTM, or success metrics. §17 positions against categories, never against competitor *prices*.
2. **Security priority is one phase late.** C2 (G4) is in Phase A, but **C7 (G7) is in Phase B / v1.0**, after "Consumable" Phase A, which explicitly invites teams to start rendering configuration in CI. Any second machine in a project can read everything. G7 should be Phase A, and is arguably a pre-requisite for any external tenant. G3 (C1) is also Phase B.
3. **No capacity reality check.** §18 sizes items S/M/L, but there is no mapping to one person's calendar. Phases C–E contain at least five L items (C3/C13/C18/C19/C23) plus five language providers, an operator and a Terraform provider: realistically years of solo work. §20.5 names scope creep as a risk yet the roadmap still covers every ecosystem.
4. **License / open-core is never decided**, although introduction.md promises open source and on-premise and the study's open question 5 (SaaS vs self-host) hinges on it.
5. **No trust plan**: certification, pentest, incident response, SLA, escrow, data residency of the SaaS itself (multi-region appears only as a technical item in C23).
6. **No liability model** for holding customer secrets (terms, insurance, how HYOK/references limit exposure), although §20.2 describes the blast radius well.
7. **No cost-to-serve numbers**; §20.6 says polling is "a steady load" without estimating Functions/Cosmos cost per customer.
8. **Compliance claims need legal validation**, which §16.3 itself concedes; this is a selling point that cannot be used until reviewed.
9. **No customer-discovery evidence** (interviews, LOIs, design partners).
10. **Dependency/licensing risk not analysed**: vendored JSON-e fork, json-everything OSMF EULA, Cosmos DB lock-in.

## 8. Failure risks [Opinion, grounded in the facts above]

| Risk | Severity | Why | Mitigation |
|---|---|---|---|
| Solo-founder bandwidth | Critical | One human contributor; Supervisor and Scheduler are stubs; roadmap spans 5 phases and 20+ integrations | Pause Supervisor/Scheduler; one stack; find a GTM co-founder or contractor |
| Scope creep | High | Study's own §20.5; 11+ use-case families in §16 | Pick one wedge and write down what you won't build |
| Secret-storage liability | Critical | G7 confirmed in code: claim emitted, never checked, and absent for API-key machines | Fix C7 now; pentest; default to references and HYOK; liability caps in terms |
| Azure lock-in | High | Functions + Cosmos only; HSM via Azure Key Vault; mTLS via `X-ARR-ClientCert` (G11) | Accept for the wedge; container + alternative backend later (C23) |
| JSON-e / JSON Logic (PR #57) | Medium-high | Proven DoS vectors; vendored fork to maintain | Fuzzing in CI, per-tenant quotas, consider making envelopes opt-in |
| Hyperscaler free tiers and OSS | High | Parameter Store free, App Config ~$36/mo, Infisical MIT | Sell catalog + governance, not storage |
| Trust / certification | High | No SOC 2/ISO, solo vendor, the study itself cites vendor fragility (§17 "stay small") | SOC 2 Type I path, escrow, self-host |
| Licensing ambiguity | Medium | No LICENSE; OSMF EULA | Decide license now; budget the fee |

## 9. Top 5 recommendations

1. **Make it safe to trust (weeks 0–4).** Move C7 into Phase A and fix it together with C2 (G4), C1/G3, C24 and C10. Make the `annotationKey` scope apply to API-key machines too. External pentest; fuzz the binding engine.
2. **Decide license and open-core boundary (week 1)** and answer open question 5 (SaaS vs self-host) as a business decision. Add LICENSE; align introduction.md.
3. **Ship the .NET/Azure wedge (weeks 2–10).** Exactly the study's Phase A + the .NET half of Phase B: provider with ETag/polling, bulk and flat reads, non-interactive `sform`, published SDKs, one reference architecture ("multi-tenant SaaS config on Azure").
4. **Add the missing business section to the study and validate it** before Phases C–E: 10 design-partner interviews in segments 1–2, 3 LOIs with price points, a one-page pricing/packaging hypothesis and a cost-to-serve estimate.
5. **Package for trust and distribution (months 3–9).** Container/Helm or Bicep self-host (C23 subset), Azure Marketplace, SOC 2 Type I (or ISO 27001 if the EU wedge wins), flat ConfigCat-style per-org tiers.

## Sources

- Study and code: https://github.com/akobr/mono.me/pull/98 (commit `6ff7ebd`); PR #57: https://github.com/akobr/mono.me/pull/57
- https://azure.microsoft.com/en-us/pricing/details/app-configuration/ · https://github.com/Azure/AppConfiguration-Announcements/issues/5 · https://c3x.dev/blog/azure-app-configuration-cost/
- https://aws.amazon.com/systems-manager/pricing/ · https://www.pulumi.com/pricing/ · https://www.doppler.com/pricing · https://infisical.com/pricing
- https://fortune.com/2025/06/06/infisical-raises-16-million-series-a-led-by-elad-gil-to-safeguard-secrets/
- https://infisical.com/blog/hashicorp-vault-pricing · https://www.ibm.com/support/pages/hcp-vault-secrets-end-life · https://infisical.com/blog/azure-key-vault-pricing
- https://configcat.com/pricing/ · https://launchdarkly.com/pricing/ · https://finance.yahoo.com/technology/ai/articles/launchdarkly-surpasses-200-million-arr-120000198.html
- https://www.flagsmith.com/pricing · https://www.getunleash.io/pricing · https://www.reddit.com/r/vercel/comments/1u1pu2z/hypertune_shutting_down/
- https://www.port.io/pricing · https://aws.amazon.com/marketplace/pp/prodview-62g4qdx6bifx2
- https://www.mordorintelligence.com/industry-reports/secrets-management-solutions-market · https://store.frost.com/key-secrets-management-global-2025-2029.html · https://reports.valuates.com/market-reports/QYRE-Auto-19J5790/global-feature-management-software
- https://patotski.com/blog/soc-2-cost-for-startup-saas/ · https://drata.com/learn/soc-2/cost · https://www.lennysnewsletter.com/p/what-is-a-good-free-to-paid-conversion

import type {
  FederatedIssuer,
  FederatedIssuerKeySource,
  FederationRule,
  FederationRuleClaims,
  MachineAccess,
} from '@42for.net/2splatform.sdk'

// Workload identity federation (424-46): trusted issuers of the project (Administrators) and the federation rules of
// Federated machines (Contributors). The API validates both and answers 400 with a message that names the field; the
// UI checks only that the fields are complete, offers templates per platform and explains the claims to pin.

/** One claim pin as an editable row. Rows with the same claim are alternatives; different claims must all match. */
export interface ClaimRow {
  claim: string
  value: string
}

/** The rows of a claim map (`{ claim: [values] }`), one per accepted value. */
export function claimRows(claims?: FederationRuleClaims | null): ClaimRow[] {
  return Object.entries(claims ?? {}).flatMap(([claim, values]) => (values ?? []).map((value) => ({ claim, value })))
}

/** The claim map of the rows: trimmed, empty rows skipped, values grouped by claim; undefined without any. */
export function claimMap(rows: ClaimRow[]): FederationRuleClaims | undefined {
  const map: Record<string, string[]> = {}
  for (const row of rows) {
    const claim = row.claim.trim()
    const value = row.value.trim()
    if (!claim || !value) {
      continue
    }
    const values = (map[claim] ??= [])
    if (!values.includes(value)) {
      values.push(value)
    }
  }
  return Object.keys(map).length ? map : undefined
}

/** Why a row cannot be saved: a claim without a value or a value without a claim. A blank row is ignored. */
export function claimRowError(row: ClaimRow): string | undefined {
  const claim = row.claim.trim()
  const value = row.value.trim()
  if (claim && !value) {
    return `Enter the accepted value of ${claim}.`
  }
  if (!claim && value) {
    return 'Enter the claim name or a JSON pointer.'
  }
  return undefined
}

/** A `<placeholder>` of a template that still has to be replaced, for example `<tenant>`. */
export function placeholderIn(value?: string | null): string | undefined {
  return value?.match(/<[^<>]+>/)?.[0]
}

/** A federation rule while it is edited. */
export interface RuleDraft {
  issuer: string
  audience: string
  subject: string
  claims: ClaimRow[]
}

export const maxRulesPerMachine = 10

export function ruleDraft(rule?: FederationRule, audience = ''): RuleDraft {
  return {
    issuer: rule?.Issuer ?? '',
    audience: rule?.Audience ?? audience,
    subject: rule?.Subject ?? '',
    claims: claimRows(rule?.Claims),
  }
}

/** A rule with a trusted issuer, an audience and a subject, and no half-filled claim row. */
export function isRuleComplete(rule: RuleDraft): boolean {
  return (
    !!rule.issuer &&
    !!rule.audience.trim() &&
    !!rule.subject.trim() &&
    !placeholderIn(rule.subject) &&
    rule.claims.every((row) => !claimRowError(row))
  )
}

export function toFederationRule(rule: RuleDraft): FederationRule {
  return {
    Issuer: rule.issuer,
    Audience: rule.audience.trim(),
    Subject: rule.subject.trim(),
    Claims: claimMap(rule.claims),
  }
}

/** The project's token endpoint (RFC 8693), where Federated machines exchange their platform tokens. */
export function projectTokenEndpoint(baseUrl: string, org: string, project: string): string {
  return `${baseUrl.replace(/\/+$/, '')}/v1/${encodeURIComponent(org)}/${encodeURIComponent(project)}/access/token`
}

/** The platforms the templates and hints know. */
export type FederationPlatform = 'github' | 'kubernetes' | 'vercel' | 'azure' | 'aws' | 'google' | 'custom'

/** The host of an issuer in lower case and ASCII, also of one without a scheme (Google may issue `accounts.google.com`). */
export function issuerHost(issuer?: string | null): string | undefined {
  if (!issuer?.trim()) {
    return undefined
  }
  for (const candidate of [issuer, `https://${issuer}`]) {
    try {
      const host = new URL(candidate).hostname
      if (host) {
        return host.replace(/\.$/, '').toLowerCase()
      }
    } catch {
      // not a URL; try it as a host
    }
  }
  return undefined
}

/** The platform of a trusted issuer, by the host of its issuer as the API classifies it; Custom when unknown. */
export function issuerPlatform(issuer?: Pick<FederatedIssuer, 'Issuer' | 'KeySource'> | null): FederationPlatform {
  const host = issuerHost(issuer?.Issuer)
  if (
    host === 'token.actions.githubusercontent.com' ||
    /^token\.actions\.[^.]+(\.[^.]+)*\.ghe\.com$/.test(host ?? '')
  ) {
    return 'github'
  }
  if (host === 'oidc.vercel.com') {
    return 'vercel'
  }
  if (host === 'accounts.google.com') {
    return 'google'
  }
  if (host === 'login.microsoftonline.com' || host === 'sts.windows.net') {
    return 'azure'
  }
  if (host?.endsWith('.tokens.sts.global.api.aws')) {
    return 'aws'
  }
  if (
    host?.endsWith('.oic.prod-aks.azure.com') ||
    /^oidc\.eks\.[a-z0-9-]+\.amazonaws\.com$/.test(host ?? '') ||
    host === 'container.googleapis.com' ||
    (!!issuer?.KeySource && issuer.KeySource !== 'Discovery')
  ) {
    return 'kubernetes'
  }
  return 'custom'
}

/** The platform of the trusted issuer a rule names; undefined while the issuers are unknown, or when it was deleted. */
export function rulePlatform(
  rule: Pick<FederationRule, 'Issuer'>,
  issuers?: FederatedIssuer[] | null,
): FederationPlatform | undefined {
  const issuer = issuers?.find((entry) => entry.Name === rule.Issuer)
  return issuer ? issuerPlatform(issuer) : undefined
}

/**
 * Whether a rule's audience is not the machine's token endpoint although its platform requests tokens for the token
 * endpoint (every platform but Azure, whose audience is the app registration). The UI presets the audience from the
 * address it calls the API at; where the API names its token endpoint differently (its public base URL, a proxy),
 * a platform token requested for the token endpoint is refused by such a rule. A rule of an unknown issuer is not flagged.
 */
export function audienceDiffers(
  rule: Pick<FederationRule, 'Issuer' | 'Audience'>,
  issuers?: FederatedIssuer[] | null,
  tokenEndpoint?: string | null,
): boolean {
  const platform = rulePlatform(rule, issuers)
  return !!platform && platform !== 'azure' && !!tokenEndpoint && rule.Audience !== tokenEndpoint
}

/**
 * The audience a GitHub Actions workflow requests its token with: that of the first rule of a GitHub issuer, since the
 * API checks a token only against the audiences of its issuer's rules. Undefined when the issuers are known and no rule
 * names a GitHub issuer: the machine has no GitHub Actions setup. Without the issuers (loading, or not readable), the
 * first rule whose audience is the token endpoint (what GitHub rules use), else the first rule.
 */
export function githubAudience(
  machine: Pick<MachineAccess, 'FederationRules' | 'TokenEndpoint'>,
  issuers?: FederatedIssuer[] | null,
): string | undefined {
  const rules = machine.FederationRules ?? []
  if (issuers) {
    return rules.find((rule) => rulePlatform(rule, issuers) === 'github')?.Audience ?? undefined
  }
  const rule = rules.find((entry) => !!entry.Audience && entry.Audience === machine.TokenEndpoint) ?? rules[0]
  return rule?.Audience ?? machine.TokenEndpoint ?? undefined
}

/**
 * The audience a new rule of an existing machine starts with: the audience of its first rule whose issuer is not Azure,
 * so the rules of one machine never mix two spellings of the token endpoint, else the token endpoint.
 */
export function newRuleAudience(
  rules: FederationRule[] | null | undefined,
  issuers: FederatedIssuer[] | null | undefined,
  tokenEndpoint: string,
): string {
  const rule = (rules ?? []).find((entry) => {
    const platform = rulePlatform(entry, issuers)
    return !!entry.Audience && !!platform && platform !== 'azure'
  })
  return rule?.Audience ?? tokenEndpoint
}

export const keySourceDescriptions: Record<FederatedIssuerKeySource, string> = {
  Discovery: 'The issuer’s discovery document (/.well-known/openid-configuration) names its keys.',
  JwksUri:
    'The keys are public at a JWKS URI, the discovery document is not (a cluster with --service-account-jwks-uri).',
  Jwks: 'The keys are pasted here and never fetched (a self-managed cluster). Paste them again after a key rotation.',
}

export const keySourceLabels: Record<FederatedIssuerKeySource, string> = {
  Discovery: 'Discovery',
  JwksUri: 'JWKS URI',
  Jwks: 'Pasted JWKS',
}

/** The issuer settings a platform starts with, and what to fill in. */
export interface IssuerTemplate {
  platform: FederationPlatform
  label: string
  icon: string
  /** A name suggestion for a new trusted issuer. */
  name: string
  issuer: string
  issuerPlaceholder?: string
  keySource: FederatedIssuerKeySource
  maxTokenLifetimeSeconds: number | null
  requiredClaims: ClaimRow[]
  help: string
}

export const issuerTemplates: IssuerTemplate[] = [
  {
    platform: 'github',
    label: 'GitHub Actions',
    icon: 'i-lucide-github',
    name: 'github',
    issuer: 'https://token.actions.githubusercontent.com',
    keySource: 'Discovery',
    maxTokenLifetimeSeconds: null,
    requiredClaims: [{ claim: 'repository_owner_id', value: '' }],
    help:
      'Every GitHub repository gets its tokens from this issuer, so pin your organization: repository_owner_id is its ' +
      'numeric id (the id of https://api.github.com/orgs/<org>). An enterprise with a unique issuer adds /<enterprise>; ' +
      'GHE.com uses https://token.actions.<subdomain>.ghe.com.',
  },
  {
    platform: 'kubernetes',
    label: 'Kubernetes',
    icon: 'i-lucide-ship-wheel',
    name: 'kubernetes',
    issuer: '',
    issuerPlaceholder: 'https://westeurope.oic.prod-aks.azure.com/<tenant>/<id>/',
    keySource: 'Discovery',
    maxTokenLifetimeSeconds: null,
    requiredClaims: [],
    help:
      'The iss of the cluster’s service account tokens. AKS: oidcIssuerProfile.issuerUrl, copied verbatim with its ' +
      'trailing /. EKS: cluster.identity.oidc.issuer. A self-managed cluster: pick Pasted JWKS and paste the output of ' +
      'kubectl get --raw /openid/v1/jwks. Rules pin /kubernetes.io/namespace. Raise the lifetime to the expirationSeconds ' +
      'of the projected token when it is longer than an hour.',
  },
  {
    platform: 'vercel',
    label: 'Vercel',
    icon: 'i-lucide-triangle',
    name: 'vercel',
    issuer: 'https://oidc.vercel.com/<team-slug>',
    keySource: 'Discovery',
    maxTokenLifetimeSeconds: 7200,
    requiredClaims: [{ claim: 'owner_id', value: '' }],
    help:
      'The team issuer; renaming the team changes it. owner_id is the team id (team_…). Function tokens live 2 hours; ' +
      'raise the lifetime to 43,200 when development tokens are exchanged too.',
  },
  {
    platform: 'azure',
    label: 'Azure managed identity',
    icon: 'i-lucide-cloud',
    name: 'azure',
    issuer: 'https://login.microsoftonline.com/<tenant>/v2.0',
    keySource: 'Discovery',
    maxTokenLifetimeSeconds: 86_400,
    requiredClaims: [],
    help:
      'For an app registration that stands for Storyteller and requests v2 tokens (requestedAccessTokenVersion 2); with ' +
      'v1 tokens the issuer is https://sts.windows.net/<tenant>/. Managed identity tokens are cached for 24 hours. In the ' +
      'rules, the subject is the identity’s object id and the audience the app registration.',
  },
  {
    platform: 'aws',
    label: 'AWS',
    icon: 'i-lucide-cloud-cog',
    name: 'aws',
    issuer: 'https://<id>.tokens.sts.global.api.aws',
    keySource: 'Discovery',
    maxTokenLifetimeSeconds: null,
    requiredClaims: [],
    help:
      'aws iam get-outbound-web-identity-federation-info prints the issuer once aws iam ' +
      'enable-outbound-web-identity-federation ran. In the rules, the subject is the role ARN; pin ' +
      '/https:~1~1sts.amazonaws.com~1/aws_account, never request_tags.',
  },
  {
    platform: 'google',
    label: 'Google Cloud',
    icon: 'i-lucide-cloudy',
    name: 'google',
    issuer: 'https://accounts.google.com',
    keySource: 'Discovery',
    maxTokenLifetimeSeconds: null,
    requiredClaims: [],
    help:
      'Tokens of the metadata server (identity?audience=…&format=full). In the rules, the subject is the service ' +
      'account’s numeric unique id, never its email, and it cannot use a wildcard.',
  },
  {
    platform: 'custom',
    label: 'Custom',
    icon: 'i-lucide-settings-2',
    name: '',
    issuer: '',
    issuerPlaceholder: 'https://issuer.example.com',
    keySource: 'Discovery',
    maxTokenLifetimeSeconds: null,
    requiredClaims: [],
    help:
      'Any OIDC issuer: the exact iss of its tokens (case and a trailing / matter), and its keys from the discovery ' +
      'document, a JWKS URI or a pasted JWKS.',
  },
]

/** What a rule of an issuer of this platform pins, shown while the rule is edited. */
export interface RuleHint {
  subject: string
  subjectPlaceholder: string
  claims?: string
  /** Claims offered in the claim rows. */
  claimSuggestions: string[]
  /** Set where the audience is not the token endpoint. */
  audience?: string
}

export const ruleHints: Record<FederationPlatform, RuleHint> = {
  github: {
    subject:
      'repo:<owner>/<repo>:environment:<name>, …:ref:refs/heads/<branch> or …:pull_request, or a prefix like ' +
      'repo:acme/billing:*. Repositories new, renamed or transferred since 2026-07-15 use repo:<owner>@<id>/<repo>@<id>:….',
    subjectPlaceholder: 'repo:acme/billing:environment:production',
    claims:
      'Pin repository_id, and repository_owner_id unless the trusted issuer requires it: names can be recycled, ids cannot.',
    claimSuggestions: ['repository_id', 'repository_owner_id', 'enterprise_id', 'environment', 'ref'],
  },
  kubernetes: {
    subject: 'system:serviceaccount:<namespace>:<name>, or system:serviceaccount:<namespace>:* with a claim pin.',
    subjectPlaceholder: 'system:serviceaccount:billing:billing-sync',
    claims:
      'Pin /kubernetes.io/namespace; /kubernetes.io/serviceaccount/uid binds the rule to one service account object. ' +
      'Project a token with this audience; the default kube-api-access token is refused.',
    claimSuggestions: [
      '/kubernetes.io/namespace',
      '/kubernetes.io/serviceaccount/uid',
      '/kubernetes.io/serviceaccount/name',
    ],
  },
  vercel: {
    subject: 'owner:<team-slug>:project:<project-name>:environment:<environment>.',
    subjectPlaceholder: 'owner:acme:project:billing:environment:production',
    claims: 'Pin project_id (prj_…), and owner_id (team_…) unless the trusted issuer requires it.',
    claimSuggestions: ['project_id', 'owner_id', 'environment'],
  },
  azure: {
    subject:
      'The managed identity’s object (principal) id. No wildcard: every identity of the tenant can get a token for the audience.',
    subjectPlaceholder: '00000000-0000-0000-0000-000000000000',
    claimSuggestions: ['tid'],
    audience:
      'The app registration that stands for Storyteller: <appId> with v2 tokens, api://<appId> with v1 tokens, not the token endpoint.',
  },
  aws: {
    subject: 'The ARN of the IAM role, or a prefix such as arn:aws:iam::123456789012:role/ci/* with a claim pin.',
    subjectPlaceholder: 'arn:aws:iam::123456789012:role/billing-sync',
    claims: 'Pin /https:~1~1sts.amazonaws.com~1/aws_account; never request_tags, the caller chooses them.',
    claimSuggestions: [
      '/https:~1~1sts.amazonaws.com~1/aws_account',
      '/https:~1~1sts.amazonaws.com~1/org_id',
      '/https:~1~1sts.amazonaws.com~1/source_region',
    ],
  },
  google: {
    subject: 'The service account’s numeric unique id, never its email. No wildcard.',
    subjectPlaceholder: '112233445566778899000',
    claimSuggestions: ['/google/compute_engine/project_id'],
  },
  custom: {
    subject: 'The token’s sub exactly, or a prefix ending with :* or /* together with a claim pin.',
    subjectPlaceholder: '',
    claimSuggestions: [],
  },
}

/** A token lifetime in seconds, in the largest whole unit; the API default (3,600 s) when unset. */
export function formatLifetime(seconds?: number | null): string {
  if (seconds == null) {
    return '1 h (default)'
  }
  if (seconds % 3600 === 0) {
    return `${seconds / 3600} h`
  }
  if (seconds % 60 === 0) {
    return `${seconds / 60} min`
  }
  return `${seconds} s`
}

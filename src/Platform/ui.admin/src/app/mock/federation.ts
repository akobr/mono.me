import type { FederatedIssuer, FederationRule, FederationRuleClaims } from '@42for.net/2splatform.sdk'
import { issuerPlatform } from '@/utils/federation'

// The main checks and 400 messages of the API's FederationValidation (424-46) for the mock API: trusted issuers,
// federation rules and the claims both pin. Network rules are simplified (no IP literal notations beyond the obvious).

export const maxIssuersPerProject = 20
export const issuerImmutableMessage = 'The issuer of a trusted issuer cannot change; create a new one.'
export const tooManyIssuersMessage = `A project has at most ${maxIssuersPerProject} trusted issuers.`
export const rulesOnOtherKindMessage = 'Only Federated machines have federation rules.'
export const resetNotSupportedMessage =
  'A Federated machine has no secret to reset. Change its federation rules instead.'
// The same refusal for a Certificate machine (PolicyAwareMachineAccessService, 424-82).
export const certificateResetNotSupportedMessage =
  'A Certificate machine has no secret to reset. Its certificate is renewed in the renewal window; to replace it, create a new machine and delete this one.'

const namePattern = /^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$/
const claimNamePattern = /^[A-Za-z0-9_.:-]{1,128}$/
const reservedClaims = ['iss', 'sub', 'aud', 'exp', 'nbf', 'iat', 'jti']
const keySources = ['Discovery', 'JwksUri', 'Jwks']
const privateJwkMembers = ['d', 'p', 'q', 'dp', 'dq', 'qi', 'k']
const foreignAudiences = [
  'sts.amazonaws.com',
  'pods.eks.amazonaws.com',
  'api://AzureADTokenExchange',
  'https://kubernetes.default.svc',
  'https://kubernetes.default.svc.cluster.local',
  'https://management.azure.com',
  'https://management.core.windows.net',
  'https://graph.microsoft.com',
  'https://vault.azure.net',
  'https://storage.azure.com',
  'https://database.windows.net',
].map((audience) => audience.toLowerCase())
const foreignAudiencePrefixes = [
  '//iam.googleapis.com/',
  'https://iam.googleapis.com/',
  'https://github.com/',
  'https://vercel.com/',
]

const trimSlash = (value: string) => (value.endsWith('/') ? value.slice(0, -1) : value)

/** Why a URL Storyteller would fetch is refused, as a clause; undefined when it passes. */
function urlError(value: string, allowQuery: boolean): string | undefined {
  let url: URL
  try {
    url = new URL(value)
  } catch {
    return 'it must be an absolute URL.'
  }
  if (url.protocol !== 'https:') {
    return 'it must use https.'
  }
  if (url.hash || (!allowQuery && url.search)) {
    return allowQuery ? 'it must not have a fragment.' : 'it must not have a query or a fragment.'
  }
  if (url.username || url.password || /\/\/[^/]*@/.test(value)) {
    return 'it must not carry user information.'
  }
  if (url.port && url.port !== '443') {
    return 'it must use port 443.'
  }
  if (/^\[.*\]$/.test(url.hostname) || /^[\d.]+$/.test(url.hostname) || /^0x/i.test(url.hostname)) {
    return 'its host must be a DNS name, not an IP address.'
  }
  return undefined
}

function jwksError(jwks: string): string | undefined {
  if (jwks.length > 16 * 1024) {
    return 'it can have at most 16 KiB.'
  }
  let set: unknown
  try {
    set = JSON.parse(jwks)
  } catch {
    return 'it is not JSON.'
  }
  const keys = (set as { keys?: unknown } | null)?.keys
  if (!Array.isArray(keys)) {
    return 'it needs a keys array.'
  }
  if (keys.length < 1 || keys.length > 10) {
    return 'it needs 1 to 10 keys.'
  }
  const kids = new Set<string>()
  for (const [index, key] of keys.entries()) {
    if (typeof key !== 'object' || key === null) {
      return `key ${index + 1} is not an object.`
    }
    const members = key as Record<string, unknown>
    const secret = privateJwkMembers.find((member) => member in members)
    if (secret) {
      return `key ${index + 1} has the private member ${secret}; paste public keys only.`
    }
    const kid = members.kid
    if (typeof kid !== 'string' || !kid) {
      return `key ${index + 1} has no kid.`
    }
    if (kids.has(kid)) {
      return `the kid ${kid} appears more than once.`
    }
    kids.add(kid)
    if (members.kty !== 'RSA' && members.kty !== 'EC') {
      return `the key ${kid} is not an RSA or EC key.`
    }
  }
  return undefined
}

/** A claim key: a claim name or an RFC 6901 pointer with ~0 and ~1 as the only escapes, never a reserved claim. */
function claimKeyError(key: string): string | undefined {
  const pointer = key.startsWith('/')
  const valid = pointer
    ? key.length - 1 >= 1 && key.length - 1 <= 256 && !/~(?![01])/.test(key)
    : claimNamePattern.test(key)
  if (!valid) {
    return (
      `the claim key '${key}' is neither a claim name (1 to 128 letters, digits, '_', '.', ':' or '-') ` +
      `nor a JSON pointer ('/' and 1 to 256 characters, with ~0 and ~1 as the only escapes).`
    )
  }
  const name = pointer ? key.slice(1).replaceAll('~1', '/').replaceAll('~0', '~') : key
  if (reservedClaims.includes(name)) {
    return `the claim ${key} cannot be pinned; use Subject and Audience.`
  }
  return undefined
}

function claimsError(claims?: FederationRuleClaims | null): string | undefined {
  const entries = Object.entries(claims ?? {})
  if (entries.length > 10) {
    return 'at most 10 claims can be pinned.'
  }
  for (const [key, values] of entries) {
    const keyError = claimKeyError(key)
    if (keyError) {
      return keyError
    }
    if (!Array.isArray(values) || values.length < 1 || values.length > 10) {
      return `the claim ${key} needs 1 to 10 accepted values.`
    }
    if (values.some((value) => typeof value !== 'string' || !value || value.length > 512)) {
      return `each accepted value of the claim ${key} needs 1 to 512 characters.`
    }
  }
  return undefined
}

/** The 400 message of an issuer PUT; undefined when the issuer can be stored. The route name is in `Name`. */
export function issuerError(issuer: FederatedIssuer): string | undefined {
  const keySource = issuer.KeySource ?? 'Discovery'
  if (!namePattern.test(issuer.Name ?? '')) {
    return `The name '${issuer.Name}' of a trusted issuer is not valid: use 1 to 63 lower-case letters, digits or hyphens, starting and ending with a letter or a digit.`
  }
  if (!keySources.includes(keySource)) {
    return `The key source ${keySource} doesn't exist.`
  }
  if (!issuer.Issuer) {
    return 'The issuer is required.'
  }
  if (issuer.Issuer.length > 512) {
    return 'The issuer can have at most 512 characters.'
  }
  const discoveryError = keySource === 'Discovery' ? urlError(issuer.Issuer, false) : undefined
  if (discoveryError) {
    return `The issuer ${issuer.Issuer} cannot be discovered: ${discoveryError}`
  }
  if (keySource === 'JwksUri') {
    if (!issuer.JwksUri) {
      return 'The JWKS URI is required when the key source is JwksUri.'
    }
    const error = urlError(issuer.JwksUri, true)
    if (error) {
      return `The JWKS URI ${issuer.JwksUri} is not valid: ${error}`
    }
  } else if (issuer.JwksUri) {
    return 'A JWKS URI is allowed only when the key source is JwksUri.'
  }
  if (keySource === 'Jwks') {
    if (!issuer.Jwks) {
      return 'The JWKS is required when the key source is Jwks.'
    }
    const error = jwksError(issuer.Jwks)
    if (error) {
      return `The JWKS is not valid: ${error}`
    }
  } else if (issuer.Jwks) {
    return 'A JWKS is allowed only when the key source is Jwks.'
  }
  const lifetime = issuer.MaxTokenLifetimeSeconds
  if (lifetime != null && (lifetime < 60 || lifetime > 86_400)) {
    return 'The maximum token lifetime must be between 60 and 86400 seconds.'
  }
  const required = claimsError(issuer.RequiredClaims)
  if (required) {
    return `Required claims: ${required}`
  }
  if ((issuer.Description?.length ?? 0) > 256) {
    return 'The description can have at most 256 characters.'
  }
  return undefined
}

const pins = (claims: FederationRuleClaims | undefined, names: string[]) =>
  Object.entries(claims ?? {}).some(
    ([key, values]) => values?.length && names.some((name) => key === name || key === `/${name}`),
  )

/** The issuer-dependent checks of one rule, as a clause. */
function ruleError(rule: FederationRule, issuer: FederatedIssuer): string | undefined {
  const audience = rule.Audience ?? ''
  if (!audience) {
    return 'the audience is required.'
  }
  if (audience.length > 512) {
    return 'the audience can have at most 512 characters.'
  }
  const trimmed = trimSlash(audience).toLowerCase()
  if (
    foreignAudiences.includes(trimmed) ||
    foreignAudiencePrefixes.some((prefix) => audience.toLowerCase().startsWith(prefix))
  ) {
    return `the audience ${audience} belongs to another service.`
  }
  if (trimmed === trimSlash(issuer.Issuer ?? '').toLowerCase()) {
    return `the audience ${audience} is the issuer itself, the default audience of its tokens for other services.`
  }
  const subject = rule.Subject ?? ''
  if (!subject) {
    return 'the subject is required.'
  }
  if (subject.length > 512) {
    return 'the subject can have at most 512 characters.'
  }
  const wildcard = subject.includes('*')
  if (wildcard && !(subject.indexOf('*') === subject.length - 1 && /[:/]\*$/.test(subject))) {
    return 'the subject can contain * only once, as its last character, right after : or /.'
  }
  const claims = claimsError(rule.Claims)
  if (claims) {
    return claims
  }
  // Classified by host, whatever the key source, like the API.
  const platform = issuerPlatform({ Issuer: issuer.Issuer })
  const pinned = (names: string[]) => pins(rule.Claims, names) || pins(issuer.RequiredClaims, names)
  if (platform === 'github' && !pinned(['repository_owner_id', 'repository_id', 'enterprise_id'])) {
    return "a rule of a GitHub Actions issuer must pin repository_owner_id, repository_id or enterprise_id, in its claims or in the issuer's required claims, because names can be recycled and ids cannot."
  }
  if (platform === 'vercel' && !pinned(['owner_id'])) {
    return "a rule of a Vercel issuer must pin owner_id, in its claims or in the issuer's required claims, because team names can be recycled and ids cannot."
  }
  if ((platform === 'google' || platform === 'azure') && wildcard) {
    return "the subject of a Google or Entra issuer cannot use *: sub is the identity's immutable id, so a wildcard would admit every identity of the tenant or of Google."
  }
  const anyPin = Object.keys(rule.Claims ?? {}).length > 0 || Object.keys(issuer.RequiredClaims ?? {}).length > 0
  if (platform !== 'github' && platform !== 'vercel' && wildcard && !anyPin) {
    return "a subject with * needs at least one claim pin, in the rule's claims or in the issuer's required claims."
  }
  return undefined
}

/** The 400 message of the rules of a Federated machine (create, rules PUT); undefined when they can be stored. */
export function rulesError(rules: FederationRule[] | null | undefined, issuers: FederatedIssuer[]): string | undefined {
  if (!Array.isArray(rules) || rules.length < 1) {
    return 'A Federated machine needs at least one federation rule.'
  }
  if (rules.length > 10) {
    return 'A machine has at most 10 federation rules.'
  }
  for (const [index, rule] of rules.entries()) {
    const number = index + 1
    if (!rule) {
      return `Rule ${number} is missing.`
    }
    if (!rule.Issuer) {
      return `Rule ${number}: the trusted issuer is required.`
    }
    const issuer = issuers.find((candidate) => candidate.Name === rule.Issuer)
    if (!issuer) {
      return `Rule ${number}: the trusted issuer '${rule.Issuer}' doesn't exist in the project.`
    }
    const error = ruleError(rule, issuer)
    if (error) {
      return `Rule ${number}: ${error}`
    }
  }
  return undefined
}

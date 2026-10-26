import {
  getStorytellerConfig,
  type AccountRoleName,
  type MachineAccess,
  type MachineAuthenticationPolicy,
  type MachineCredentialKind,
  type MachineScope,
} from '@42for.net/2splatform.sdk'
import type { OneTimeSecret } from '@/components/access/OneTimeSecretDialog.vue'

/** The API root the app calls (configured at start-up from apiBaseUrl), for URLs and snippets shown to the user. */
export function apiBaseUrl(): string {
  try {
    return getStorytellerConfig().baseUrl
  } catch {
    return 'https://<api-host>/api'
  }
}

/**
 * The scope strings of the machine scopes (424-79), as the credentials of a machine carry them and messages name them;
 * the API, and with it the SDK, uses the enum names.
 */
export const scopeLabels: Record<MachineScope, string> = {
  AnnotationRead: 'Annotation.Read',
  AnnotationWrite: 'Annotation.Write',
  ConfigurationRead: 'Configuration.Read',
  ConfigurationWrite: 'Configuration.Write',
  SecretsRead: 'Secrets.Read',
  SecretsWrite: 'Secrets.Write',
  ConfigurationTransfer: 'Configuration.Transfer',
  SchemaWrite: 'Schema.Write',
  TemplateWrite: 'Template.Write',
  AnnotationManage: 'Annotation.Manage',
  KeyValueManage: 'KeyValue.Manage',
}

/** The scope string of a scope; an unknown value (a newer API) as it is. */
export const scopeLabel = (scope: MachineScope) => scopeLabels[scope] ?? scope

/**
 * What each machine scope allows. The runtime scopes read and write single documents, the secrets scopes on top of the
 * configuration scopes, which they do not include; the management scopes (424-57) open bulk and structural operations,
 * each on top of the runtime scope of its operation, which still decides what it may read and write.
 */
export const scopeDescriptions: Record<MachineScope, string> = {
  AnnotationRead: 'Reads annotations.',
  AnnotationWrite: 'Creates and changes annotations, one at a time.',
  ConfigurationRead: 'Reads configurations (resolved, with bindings evaluated), schemas, templates and key/values.',
  ConfigurationWrite: 'Writes configurations and key/values.',
  SecretsRead:
    'Receives the values of key/value secrets (over mTLS when the project requires it). Needs Configuration.Read as well.',
  SecretsWrite: 'Writes key/value secrets. Needs Configuration.Write as well.',
  ConfigurationTransfer:
    'Exports and imports the views and the key/values of the project, with the scopes of the kinds they include: importing annotations, schemas or templates also needs their management scope.',
  SchemaWrite: 'Writes and deletes type, annotation and descendant-type schemas.',
  TemplateWrite: 'Writes and deletes the templates of a view.',
  AnnotationManage: 'Creates annotations in bulk, deletes them with their descendants and restores them.',
  KeyValueManage: 'Moves key/values between protection levels in bulk, and restores them in bulk.',
}

/** Every machine scope, in the order of the API. */
export const machineScopes = Object.keys(scopeLabels) as MachineScope[]

/** The management scopes, in the order of the API: only Administrators give them (424-57). */
export const managementScopes: MachineScope[] = [
  'ConfigurationTransfer',
  'SchemaWrite',
  'TemplateWrite',
  'AnnotationManage',
  'KeyValueManage',
]

export const isManagementScope = (scope: MachineScope) => managementScopes.includes(scope)

/** The secrets scopes: only users with secret rights (ContributorWithSecrets) give them. */
export const isSecretsScope = (scope: MachineScope) => scope === 'SecretsRead' || scope === 'SecretsWrite'

/** The runtime scopes, in the order of the API: a machine needs at least one of them. */
export const runtimeScopes = machineScopes.filter((scope) => !isManagementScope(scope))

/** The read scope each write scope comes with: the API stores X.Read with X.Write. */
export const impliedReads: Partial<Record<MachineScope, MachineScope>> = {
  AnnotationWrite: 'AnnotationRead',
  ConfigurationWrite: 'ConfigurationRead',
  SecretsWrite: 'SecretsRead',
}

/** The scopes distinct and in the order of the API (UCheckboxGroup emits its values in click order). */
export const inApiOrder = (scopes: readonly MachineScope[]) => machineScopes.filter((scope) => scopes.includes(scope))

/** The scopes with the read scope of every write scope, in the order of the API, as the API completes them. */
export const withImpliedReads = (scopes: readonly MachineScope[]) =>
  inApiOrder([...scopes, ...scopes.flatMap((scope) => impliedReads[scope] ?? [])])

export interface ScopePreset {
  name: string
  scopes: MachineScope[]
}

const runtimeReader: MachineScope[] = ['AnnotationRead', 'ConfigurationRead']
const ciWriter: MachineScope[] = ['AnnotationRead', 'AnnotationWrite', 'ConfigurationRead', 'ConfigurationWrite']

/**
 * Typical machines, as a start for the scopes of a new one (424-79). They exist in the UI only: a preset replaces the
 * picked scopes, and the API gets and stores the scopes alone.
 */
export const scopePresets: ScopePreset[] = [
  { name: 'Runtime reader', scopes: runtimeReader },
  { name: 'Runtime with secrets', scopes: [...runtimeReader, 'SecretsRead'] },
  { name: 'CI writer', scopes: ciWriter },
  // it imports every kind, so it has the management scopes the import of schemas, templates and annotations needs
  {
    name: 'Git sync',
    scopes: [...ciWriter, 'ConfigurationTransfer', 'SchemaWrite', 'TemplateWrite', 'AnnotationManage'],
  },
  { name: 'Backup', scopes: [...runtimeReader, 'ConfigurationTransfer'] },
  { name: 'Schema deployer', scopes: ['ConfigurationRead', 'ConfigurationWrite', 'SchemaWrite', 'TemplateWrite'] },
]

export const credentialKindDescriptions: Record<MachineCredentialKind, string> = {
  ApiKey: 'An API key per machine (Authorization: ApiKey …).',
  Certificate: 'A client certificate per machine (mTLS).',
  CertificateAndApiKey:
    'A client certificate (its own, or a shared certificate of the project) and an API key; both must be presented.',
  ClientCredentials: 'Client ID and secret of the identity provider, exchanged for a short-lived token.',
  Federated:
    'No secret: the workload exchanges a token of a trusted issuer of the project (GitHub Actions, Kubernetes, Vercel, a cloud identity) for a short-lived token.',
}

/** Every credential kind, in the order of the API. */
export const credentialKinds = Object.keys(credentialKindDescriptions) as MachineCredentialKind[]

/** Readable names of the kinds for the machine list and detail; the policy and the create dialog keep the API names. */
export const credentialKindLabels: Record<MachineCredentialKind, string> = {
  ApiKey: 'API key',
  Certificate: 'Certificate',
  CertificateAndApiKey: 'Certificate and API key',
  ClientCredentials: 'Client credentials',
  Federated: 'Federated',
}

/** The label of a stored kind; a machine without one is an API key machine, as the API treats it. */
export const credentialKindLabel = (kind?: MachineCredentialKind | null) =>
  credentialKindLabels[kind ?? 'ApiKey'] ?? kind

/**
 * Whether machines of this kind hold a secret that a reset replaces and the API shows once: every kind but Federated,
 * which has no secret (424-46), and Certificate, whose certificate is renewed, not reset (424-82). A reset of a
 * CertificateAndApiKey machine replaces only its API key.
 */
export const hasSecret = (kind?: MachineCredentialKind | null) => kind !== 'Federated' && kind !== 'Certificate'

/**
 * The role that gives these scopes, or the credentials of this machine: its creation, a reset and new federation rules
 * (424-79). The creator of a machine can use its credentials, so an Administrator gives management scopes,
 * ContributorWithSecrets secrets scopes, and a Contributor the rest. Deleting a machine stops whatever runs with its
 * scopes, so it needs the same role.
 */
export function requiredRoleFor(machine?: MachineAccess | readonly MachineScope[] | null): AccountRoleName {
  const scopes = Array.isArray(machine) ? machine : ((machine as MachineAccess | null | undefined)?.Scopes ?? [])
  if (scopes.some(isManagementScope)) {
    return 'Administrator'
  }
  return scopes.some(isSecretsScope) ? 'ContributorWithSecrets' : 'Contributor'
}

/** Whether machines of this kind exchange a token of a trusted issuer instead of holding a secret (424-46). */
export const isFederated = (kind?: MachineCredentialKind | null) => kind === 'Federated'

/**
 * Whether the project issues and accepts shared certificates: they are the certificate half of a CertificateAndApiKey
 * login, so they follow that kind of the machine authentication policy.
 */
export const allowsSharedCertificates = (policy?: MachineAuthenticationPolicy | null) =>
  !!policy?.EnabledCredentialKinds?.includes('CertificateAndApiKey')

/** Whether machines of this kind get a client certificate (and with it a certificate lifetime). */
export const issuesCertificates = (kind?: MachineCredentialKind | null) =>
  kind === 'Certificate' || kind === 'CertificateAndApiKey'

/**
 * Days until the machine certificate expires: from its expiry, else from the last renewal and the machine's
 * lifetime (or the fallback, for machines created before the lifetime was stored); undefined if unknown.
 */
export function certificateDaysLeft(
  machine: MachineAccess,
  fallbackLifetimeDays?: number | null,
  now = Date.now(),
): number | undefined {
  if (!machine.CertificateThumbprint) {
    return undefined
  }
  if (machine.CertificateNotAfter) {
    return Math.floor((Date.parse(machine.CertificateNotAfter) - now) / 86_400_000)
  }
  const lifetimeDays = machine.CertificateLifetimeDays ?? fallbackLifetimeDays
  if (!machine.LastRenewalAt || !lifetimeDays) {
    return undefined
  }
  const expires = Date.parse(machine.LastRenewalAt) + lifetimeDays * 86_400_000
  return Math.floor((expires - now) / 86_400_000)
}

/** The one-time credentials of a create or reset response, for OneTimeSecretDialog. */
export function machineSecret(machine: MachineAccess, title: string, baseUrl?: string): OneTimeSecret {
  return {
    title,
    id: machine.Id,
    apiKey: machine.AccessKey,
    credentialKind: machine.CredentialKind,
    certificate: machine.Certificate,
    certificatePassword: machine.CertificatePassword,
    sharedCertificate: machine.UsesSharedCertificate,
    tokenEndpoint: machine.TokenEndpoint,
    tokenScope: machine.TokenScope,
    fileName: `${machine.Id ?? 'machine'}.pfx`,
    baseUrl,
  }
}

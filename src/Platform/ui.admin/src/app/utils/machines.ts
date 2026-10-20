import {
  getStorytellerConfig,
  type MachineAccess,
  type MachineAccessCreateScope,
  type MachineAuthenticationPolicy,
  type MachineCredentialKind,
  type MachineManagementScope,
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

/** What each machine scope allows (spec section 13: "a select with an explanation per scope"). */
export const scopeDescriptions: Record<MachineAccessCreateScope, string> = {
  AnnotationRead: 'Reads annotations.',
  ConfigurationRead: 'Reads configurations (resolved, with bindings evaluated).',
  DefaultRead: 'Reads annotations and configurations.',
  AnnotationReadWrite: 'Reads and writes annotations.',
  ConfigurationReadWrite: 'Reads and writes configurations.',
  DefaultReadWrite: 'Reads and writes annotations and configurations.',
  SecretsRead: 'Reads configurations and receives key/value secrets (over mTLS when the project requires it).',
  SecretsReadWrite: 'Reads and writes configurations and key/value secrets (over mTLS when the project requires it).',
}

/**
 * What each management scope opens (424-57): bulk and structural operations that no scope includes. Each adds to the
 * scope of the machine, which still decides what the operation may read and write.
 */
export const managementScopeDescriptions: Record<MachineManagementScope, string> = {
  ConfigurationTransfer: 'Exports and imports the views and the key/values of the project.',
  SchemaWrite: 'Writes and deletes type, annotation and descendant-type schemas.',
  TemplateWrite: 'Writes and deletes the templates of a view.',
  AnnotationManage: 'Creates annotations in bulk, deletes them with their descendants and restores them.',
  KeyValueManage: 'Moves key/values between protection levels in bulk, and restores them in bulk.',
}

/** Every management scope, in the order of the API. */
export const managementScopes = Object.keys(managementScopeDescriptions) as MachineManagementScope[]

/** The scope strings of the management scopes, as the credentials of a machine carry them. */
export const managementScopeLabels: Record<MachineManagementScope, string> = {
  ConfigurationTransfer: 'Configuration.Transfer',
  SchemaWrite: 'Schema.Write',
  TemplateWrite: 'Template.Write',
  AnnotationManage: 'Annotation.Manage',
  KeyValueManage: 'KeyValue.Manage',
}

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
 * Whether machines of this kind get credentials that the API shows once and a reset replaces: every kind but Federated,
 * which has no secret (424-46).
 */
export const hasSecret = (kind?: MachineCredentialKind | null) => kind !== 'Federated'

/**
 * Whether a reset of the machine, and new federation rules, need an Administrator: the machine has management scopes,
 * which only Administrators give out (424-57). Contributors reset the other machines, and delete any machine.
 */
export const needsAdministrator = (machine?: MachineAccess | null) => !!machine?.ManagementScopes?.length

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

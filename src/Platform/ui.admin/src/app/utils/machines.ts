import type { MachineAccess, MachineAccessCreateScope, MachineAuthenticationPolicy } from '@42for.net/2splatform.sdk'
import type { OneTimeSecret } from '@/components/access/OneTimeSecretDialog.vue'

/** What each machine scope allows (spec section 13: "a select with an explanation per scope"). */
export const scopeDescriptions: Record<MachineAccessCreateScope, string> = {
  AnnotationRead: 'Reads annotations.',
  ConfigurationRead: 'Reads configurations (resolved, with bindings evaluated).',
  DefaultRead: 'Reads annotations and configurations.',
  AnnotationReadWrite: 'Reads and writes annotations.',
  ConfigurationReadWrite: 'Reads and writes configurations.',
  DefaultReadWrite: 'Reads and writes annotations and configurations.',
}

export const credentialKindDescriptions: Record<NonNullable<MachineAuthenticationPolicy['CredentialKind']>, string> = {
  ApiKey: 'An API key per machine (Authorization: ApiKey …).',
  Certificate: 'A client certificate per machine (mTLS).',
  CertificateAndApiKey: 'A client certificate and an API key; both must be presented.',
  ClientCredentials: 'Client ID and secret of the identity provider, exchanged for a short-lived token.',
}

export const issuesCertificates = (policy?: MachineAuthenticationPolicy) =>
  policy?.CredentialKind === 'Certificate' || policy?.CredentialKind === 'CertificateAndApiKey'

/** Days until the machine certificate expires, from the last renewal and the policy lifetime; undefined if unknown. */
export function certificateDaysLeft(
  machine: MachineAccess,
  lifetimeDays: number | null | undefined,
  now = Date.now(),
): number | undefined {
  if (!machine.CertificateThumbprint || !machine.LastRenewalAt || !lifetimeDays) {
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
    tokenEndpoint: machine.TokenEndpoint,
    tokenScope: machine.TokenScope,
    fileName: `${machine.Id ?? 'machine'}.pfx`,
    baseUrl,
  }
}

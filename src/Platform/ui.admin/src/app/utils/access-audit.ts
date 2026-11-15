import {
  AccessAuditEntryAction,
  type AccessAuditEntry,
  type AccessAuditTargetKind,
  type AccountRoleName,
} from '@42for.net/2splatform.sdk'

// The access audit trail of an organization (424-263, docs/Platform/Storyteller/access-audit.md): readable labels of
// its actions, the project of a record, and its values, which are a role name, a description or compact JSON.

/** Every action, in the order of the API's enum. */
export const accessAuditActions = Object.values(AccessAuditEntryAction)

const actionLabels: Record<AccessAuditEntryAction, string> = {
  MemberGranted: 'Member granted',
  MemberRevoked: 'Member revoked',
  MemberRoleChanged: 'Role changed',
  MemberRemoved: 'Member removed',
  MemberJoined: 'Member joined',
  AccessPointCreated: 'Organization or project created',
  AccessPointUpdated: 'Project description changed',
  MachineCreated: 'Machine created',
  MachineReset: 'Machine credential reset',
  MachineDeleted: 'Machine deleted',
  MachineFederationRulesChanged: 'Federation rules changed',
  MachineAuthenticationPolicyChanged: 'Machine authentication changed',
  KeyValuePolicyChanged: 'Secrets policy changed',
  FederatedIssuerCreated: 'Trusted issuer added',
  FederatedIssuerUpdated: 'Trusted issuer changed',
  FederatedIssuerDeleted: 'Trusted issuer deleted',
  SharedCertificateIssued: 'Shared certificate issued',
  SharedCertificateRevoked: 'Shared certificate revoked',
}

/** The label of an action in the filter; an action the UI does not know yet keeps its name. */
export function accessAuditActionLabel(action: string | undefined): string {
  return (action && actionLabels[action as AccessAuditEntryAction]) || action || 'Unknown'
}

/** The label of a record: a created access point names what it created. */
export function accessAuditEntryLabel(entry: Pick<AccessAuditEntry, 'Action' | 'AccessPointKey'>): string {
  if (entry.Action === 'AccessPointCreated') {
    return accessAuditProject(entry.AccessPointKey) ? 'Project created' : 'Organization created'
  }
  return accessAuditActionLabel(entry.Action)
}

/** The project of a record's access point; undefined for the organization itself. */
export function accessAuditProject(accessPointKey: string | undefined): string | undefined {
  const dot = accessPointKey?.indexOf('.') ?? -1
  return dot > 0 ? accessPointKey!.slice(dot + 1) : undefined
}

const targetIcons: Record<AccessAuditTargetKind, string> = {
  Account: 'i-lucide-user-round',
  Machine: 'i-lucide-computer',
  Issuer: 'i-lucide-shield-check',
  Certificate: 'i-lucide-file-badge',
}

const targetKinds: Record<AccessAuditTargetKind, string> = {
  Account: 'Account',
  Machine: 'Machine',
  Issuer: 'Trusted issuer',
  Certificate: 'Shared certificate',
}

// Literal names: the client bundle holds only the icons its scan finds in the sources.
export const accessAuditTargetIcon = (kind: string | undefined) =>
  targetIcons[kind as AccessAuditTargetKind] ?? 'i-lucide-circle-dot'

export const accessAuditTargetKind = (kind: string | undefined) =>
  targetKinds[kind as AccessAuditTargetKind] ?? kind ?? 'Target'

const roles: readonly string[] = ['Owner', 'Administrator', 'ContributorWithSecrets', 'Contributor', 'Reader']

/** Whether a value is a role name, as the membership records and a created access point store it. */
export const isRoleValue = (value: string | undefined): value is AccountRoleName => !!value && roles.includes(value)

/** A value of JSON, readable: indented; any other value, and a cut one that no longer parses, as it is. */
export function readableAuditValue(value: string): string {
  const text = value.trim()
  if (!text.startsWith('{') && !text.startsWith('[')) {
    return value
  }
  try {
    return JSON.stringify(JSON.parse(text), null, 2)
  } catch {
    return value
  }
}

/** Characters a value shows in its table cell; a longer value, or JSON, is readable in full on demand. */
export const shortValueLength = 40

export function isLongAuditValue(value: string | undefined): boolean {
  if (!value) {
    return false
  }
  return value.length > shortValueLength || readableAuditValue(value) !== value
}

import type {
  AccessAuditEntry,
  AccessAuditEntryAction,
  AccessAuditTarget,
  AuditActor,
  ClientEncryptionRecipient,
  FederatedIssuer,
  FederationRule,
  KeyValuePolicy,
  MachineAccess,
  MachineAuthenticationPolicy,
  SharedCertificate,
} from '@42for.net/2splatform.sdk'
import { AccessAuditEntryAction as Actions } from '@42for.net/2splatform.sdk'
import { accountActor, mockActor } from './audit'
import { mockDirectory, type MockDb } from './db'
import { mockRecipient } from './key-values'

// The access audit trail of the mock API (424-263, docs/Platform/Storyteller/access-audit.md): one list of records per
// organization, written by every mocked access write with the values the API stores, never a secret. The organization
// acme of the legacy datasets and fabrikam of the showcase start with a seeded history.

/** Records per page, as the API pages them. */
export const accessAuditPageSize = 100

export const accessAuditActions: AccessAuditEntryAction[] = Object.values(Actions)

const day = 86_400_000
let sequence = 0

/** What a write records; the actor is the signed-in user unless it says otherwise. */
export interface AccessAuditChange {
  Action: AccessAuditEntryAction
  Target?: AccessAuditTarget
  OldValue?: string
  NewValue?: string
  Detail?: string
  Actor?: AuditActor
  CorrelationId?: string
}

/** An account of the mock directory as the target of a record, with the name the API takes at the change. */
export const accountTarget = (id: string, name = mockDirectory[id]?.name): AccessAuditTarget => ({
  Kind: 'Account',
  Id: id,
  Name: name,
})

export const machineTarget = (id: string): AccessAuditTarget => ({ Kind: 'Machine', Id: id })

export const issuerTarget = (name: string): AccessAuditTarget => ({ Kind: 'Issuer', Id: name })

export const certificateTarget = (certificate: SharedCertificate): AccessAuditTarget => ({
  Kind: 'Certificate',
  Id: certificate.Thumbprint ?? '',
  Name: certificate.Label,
})

// --- the values, as AccessAuditValues of the API writes them (compact JSON, no empty field) ---

export const machineValue = (machine: MachineAccess) =>
  JSON.stringify({
    CredentialKind: machine.CredentialKind,
    Scopes: machine.Scopes ?? [],
    AnnotationKey: machine.AnnotationKey || undefined,
    UsesSharedCertificate: machine.UsesSharedCertificate || undefined,
    CertificateThumbprint: machine.CertificateThumbprint || undefined,
  })

export const federationRulesValue = (rules: FederationRule[] | null | undefined) => JSON.stringify(rules ?? [])

export const machineAuthenticationValue = (policy: MachineAuthenticationPolicy | undefined) =>
  JSON.stringify({
    EnabledCredentialKinds: policy?.EnabledCredentialKinds,
    CredentialKind: policy?.CredentialKind,
    CertificateLifetimeDays: policy?.CertificateLifetimeDays ?? undefined,
  })

/** 16 hexadecimal digits of the key (the API takes the start of its SHA-256; the mock needs no real hash). */
function keyThumbprint(recipient: ClientEncryptionRecipient): string {
  const text = `${recipient.PublicKey?.Kty}.${recipient.PublicKey?.N}.${recipient.PublicKey?.E}`
  let high = 0x811c9dc5
  let low = 0x01000193
  for (const char of text) {
    high = Math.imul(high ^ char.charCodeAt(0), 0x01000193) >>> 0
    low = Math.imul(low ^ char.charCodeAt(0), 0x811c9dc5) >>> 0
  }
  return `${high.toString(16).padStart(8, '0')}${low.toString(16).padStart(8, '0')}`
}

/**
 * The access view of a key/value policy: what decides who gets secrets and how. Key versions, health, verifications
 * and rewraps are left out, so a verification or a rotation records nothing; a recipient's key only as a thumbprint.
 */
export const keyValuePolicyValue = (policy: KeyValuePolicy) =>
  JSON.stringify({
    AllowedProtections: policy.AllowedProtections,
    DefaultSecretProtection: policy.DefaultSecretProtection,
    RequireMtlsForSecretDelivery: policy.RequireMtlsForSecretDelivery,
    HsmBackend: policy.Hsm?.Backend,
    HsmRotationInterval: policy.Hsm?.RotationInterval,
    ByokKeyUri: policy.Byok?.KeyUri || undefined,
    ByokTenantId: policy.Byok?.TenantId || undefined,
    Recipients: (policy.Recipients ?? []).map((recipient) => ({
      Id: recipient.Id,
      Name: recipient.Name,
      IsEnabled: recipient.IsEnabled,
      KeyThumbprint: keyThumbprint(recipient),
    })),
  })

/** An issuer without its JWKS keys, only how many there are. */
export function issuerValue(issuer: FederatedIssuer): string {
  let keys: number | undefined
  try {
    const jwks = issuer.Jwks ? (JSON.parse(issuer.Jwks) as { keys?: unknown }) : undefined
    keys = Array.isArray(jwks?.keys) ? jwks.keys.length : undefined
  } catch {
    keys = undefined
  }
  return JSON.stringify({
    Issuer: issuer.Issuer,
    KeySource: issuer.KeySource,
    JwksUri: issuer.JwksUri || undefined,
    JwksKeys: keys,
    MaxTokenLifetimeSeconds: issuer.MaxTokenLifetimeSeconds ?? undefined,
    RequiredClaims: issuer.RequiredClaims,
    Description: issuer.Description || undefined,
  })
}

/** A shared certificate without the certificate and its password. */
export const certificateValue = (certificate: SharedCertificate) =>
  JSON.stringify({
    Label: certificate.Label,
    NotBefore: certificate.NotBefore,
    NotAfter: certificate.NotAfter,
    IsRevoked: certificate.IsRevoked || undefined,
  })

// --- the store ---

const organizationOf = (accessPointKey: string) => accessPointKey.split('.')[0]!

/** The time in the sortable form of the API's ids. */
const sortable = (time: string) => time.replace(/\D/g, '')

function newEntry(
  time: string,
  accessPointKey: string,
  change: AccessAuditChange,
  actor: AuditActor,
): AccessAuditEntry {
  sequence += 1
  return {
    Id: `aca.${sortable(time)}${String(sequence % 10_000).padStart(4, '0')}.${crypto.randomUUID().replaceAll('-', '')}`,
    Time: time,
    AccessPointKey: accessPointKey,
    CorrelationId: crypto.randomUUID(),
    ...change,
    Actor: change.Actor ?? actor,
  }
}

/**
 * The records of an organization, oldest first. Seeded on first use: acme in the legacy datasets, fabrikam in the
 * showcase; any other organization starts empty.
 */
export function accessAuditOf(db: MockDb, org: string): AccessAuditEntry[] {
  const trails = (db.accessAudit ??= {})
  let records = trails[org]
  if (!records) {
    records =
      db.scenario === 'showcase' ? (org === 'fabrikam' ? seedFabrikam(db) : []) : org === 'acme' ? seedAcme(db) : []
    trails[org] = records
  }
  return records
}

/** Records a change of an access point (`org` or `org.project`) by the signed-in user, now. */
export function recordAccessAudit(db: MockDb, accessPointKey: string, change: AccessAuditChange): void {
  accessAuditOf(db, organizationOf(accessPointKey)).push(
    newEntry(new Date().toISOString(), accessPointKey, change, mockActor(db)),
  )
}

/** Records a change unless its value stays the same: a no-op records nothing, as in the API. */
export function recordAccessChange(
  db: MockDb,
  accessPointKey: string,
  change: AccessAuditChange & { OldValue: string; NewValue: string },
): void {
  if (change.OldValue !== change.NewValue) {
    recordAccessAudit(db, accessPointKey, change)
  }
}

export interface AccessAuditFilter {
  /** The project of the records; undefined for the whole organization (its own records included). */
  project?: string
  action?: AccessAuditEntryAction
  target?: string
  from?: string
  to?: string
}

/** The records of an organization that match, newest first. */
export function queryAccessAudit(db: MockDb, org: string, filter: AccessAuditFilter): AccessAuditEntry[] {
  return accessAuditOf(db, org)
    .filter((entry) => !filter.project || entry.AccessPointKey === `${org}.${filter.project}`)
    .filter((entry) => !filter.action || entry.Action === filter.action)
    .filter((entry) => !filter.target || entry.Target?.Id === filter.target)
    .filter((entry) => !filter.from || (entry.Time ?? '') >= filter.from)
    .filter((entry) => !filter.to || (entry.Time ?? '') <= filter.to)
    .toSorted((a, b) => (b.Time ?? '').localeCompare(a.Time ?? '') || (b.Id ?? '').localeCompare(a.Id ?? ''))
}

// --- the seeds ---

type SeedLine = [daysAgo: number, accessPointKey: string, change: AccessAuditChange, actor: AuditActor]

function seeded(lines: SeedLine[], prefix: string): AccessAuditEntry[] {
  const now = Date.now()
  return lines.map(([daysAgo, accessPointKey, change, actor], index) => {
    const time = new Date(now - daysAgo * day).toISOString()
    const number = String(index + 1).padStart(12, '0')
    return {
      Id: `aca.${sortable(time)}0000.seed${prefix}${number}`,
      Time: time,
      AccessPointKey: accessPointKey,
      // fixed, so the screenshots and the tests can name them
      CorrelationId: `00000000-0000-4000-a000-${number}`,
      ...change,
      Actor: change.Actor ?? actor,
    }
  })
}

const system: AuditActor = { Kind: 'System', Id: 'system' }

/** The organization acme of the legacy datasets: its projects, members, machines, issuers and policies. */
function seedAcme(db: MockDb): AccessAuditEntry[] {
  const ada = accountActor('user_01MOCKADA')
  const grace = accountActor('user_01MOCKGRACE')
  const description = (key: string) => db.accessPoints[key]?.Description
  const pipeline = db.machines?.['acme.billing']?.find((machine) => machine.Id === 'mach-release-pipeline')
  const rules = pipeline?.FederationRules ?? []
  const stagingRules = rules.map((rule) => ({ ...rule, Subject: rule.Subject?.replace('production', 'staging') }))
  const certificate = db.certificates?.['acme.billing']?.[0]
  const issuer = db.issuers?.['acme.billing']?.[0]
  const policy = (
    allowed: KeyValuePolicy['AllowedProtections'],
    recipients: ClientEncryptionRecipient[],
    hsm = false,
  ) =>
    keyValuePolicyValue({
      AllowedProtections: allowed,
      DefaultSecretProtection: 'Standard',
      RequireMtlsForSecretDelivery: true,
      Hsm: hsm ? { Backend: 'KeyVaultPremium', RotationInterval: '365.00:00:00' } : undefined,
      Recipients: recipients,
    })
  const authentication = (kinds: MachineAuthenticationPolicy['EnabledCredentialKinds']) =>
    machineAuthenticationValue({ EnabledCredentialKinds: kinds, CredentialKind: 'ApiKey', CertificateLifetimeDays: 90 })

  return seeded(
    [
      [420, 'acme', { Action: 'AccessPointCreated', Target: accountTarget('user_01MOCKADA'), NewValue: 'Owner' }, ada],
      [
        420,
        'acme.billing',
        {
          Action: 'AccessPointCreated',
          Target: accountTarget('user_01MOCKADA'),
          NewValue: 'Owner',
          Detail: description('acme.billing'),
        },
        ada,
      ],
      [
        410,
        'acme',
        { Action: 'MemberGranted', Target: accountTarget('user_01MOCKGRACE'), NewValue: 'Administrator' },
        ada,
      ],
      [
        405,
        'acme.shipping',
        {
          Action: 'AccessPointCreated',
          Target: accountTarget('user_01MOCKGRACE'),
          NewValue: 'Owner',
          Detail: description('acme.shipping'),
        },
        system,
      ],
      [
        400,
        'acme.billing',
        {
          Action: 'MemberJoined',
          Target: accountTarget('user_01MOCKGRACE'),
          NewValue: 'Contributor',
          Detail: 'Invitation inv_01MOCKGRACE sent by Ada Lovelace (user_01MOCKADA).',
        },
        grace,
      ],
      [
        398,
        'acme.shipping',
        { Action: 'MemberGranted', Target: accountTarget('user_01MOCKADA'), NewValue: 'Contributor' },
        grace,
      ],
      [
        380,
        'acme.billing',
        {
          Action: 'MachineCreated',
          Target: machineTarget('mach-invoicing-worker'),
          NewValue: machineValue({ CredentialKind: 'ApiKey', Scopes: ['AnnotationRead', 'ConfigurationRead'] }),
        },
        ada,
      ],
      ...(certificate
        ? [
            [
              340,
              'acme.billing',
              {
                Action: 'SharedCertificateIssued',
                Target: certificateTarget(certificate),
                NewValue: certificateValue({ ...certificate, IsRevoked: false }),
              },
              ada,
            ] satisfies SeedLine,
          ]
        : []),
      [
        200,
        'acme.billing',
        { Action: 'MemberGranted', Target: accountTarget('user_01MOCKALAN'), NewValue: 'Contributor' },
        ada,
      ],
      [
        125,
        'acme.billing',
        {
          Action: 'MachineAuthenticationPolicyChanged',
          OldValue: authentication(['ApiKey']),
          NewValue: authentication(['ApiKey', 'Federated']),
        },
        ada,
      ],
      ...(issuer
        ? (['acme.billing', 'acme.shipping'] as const).map(
            (key, index) =>
              [
                120 - index,
                key,
                {
                  Action: 'FederatedIssuerCreated',
                  Target: issuerTarget(issuer.Name ?? 'github'),
                  NewValue: issuerValue(issuer),
                },
                index ? grace : ada,
              ] satisfies SeedLine,
          )
        : []),
      [
        118,
        'acme.billing',
        {
          Action: 'MachineCreated',
          Target: machineTarget('mach-release-pipeline'),
          NewValue: machineValue({ CredentialKind: 'Federated', Scopes: pipeline?.Scopes }),
          Detail: `Federation rules: ${federationRulesValue(stagingRules)}`,
        },
        ada,
      ],
      [
        45,
        'acme.billing',
        {
          Action: 'MachineDeleted',
          Target: machineTarget('mach-legacy-export'),
          OldValue: machineValue({ CredentialKind: 'ApiKey', Scopes: ['ConfigurationRead'] }),
        },
        grace,
      ],
      [
        40,
        'acme.billing',
        {
          Action: 'KeyValuePolicyChanged',
          OldValue: policy(['Public', 'Standard'], []),
          NewValue: policy(['Public', 'Standard'], [mockRecipient]),
        },
        ada,
      ],
      // a removed member keeps the name the record took at the change
      [
        30,
        'acme',
        { Action: 'MemberRemoved', Target: accountTarget('user_01MOCKCHARLES', 'Charles Babbage'), OldValue: 'Reader' },
        grace,
      ],
      [
        20,
        'acme.billing',
        {
          Action: 'KeyValuePolicyChanged',
          OldValue: policy(['Public', 'Standard'], [mockRecipient]),
          NewValue: policy(['Public', 'Standard', 'Hsm', 'Hyok'], [mockRecipient], true),
          // the request of the key/value audit's PolicyChange of that day (key-values.ts)
          CorrelationId: '00000000-0000-4000-8000-000000000020',
        },
        ada,
      ],
      [
        20,
        'acme.billing',
        {
          Action: 'MachineReset',
          Target: machineTarget('mach-invoicing-worker'),
          NewValue: 'ApiKey',
          Detail: 'The API key was replaced.',
        },
        ada,
      ],
      [
        14,
        'acme.billing',
        {
          Action: 'AccessPointUpdated',
          OldValue: "Billing of Acme's B2B customers.",
          NewValue: description('acme.billing'),
        },
        ada,
      ],
      [
        5,
        'acme.billing',
        {
          Action: 'MemberRoleChanged',
          Target: accountTarget('user_01MOCKALAN'),
          OldValue: 'Contributor',
          NewValue: 'Reader',
        },
        { ...ada, Via: 'mcp' },
      ],
      [
        2,
        'acme.billing',
        {
          Action: 'MachineFederationRulesChanged',
          Target: machineTarget('mach-release-pipeline'),
          OldValue: federationRulesValue(stagingRules),
          NewValue: federationRulesValue(rules),
        },
        ada,
      ],
    ],
    'acme',
  )
}

/** The organization fabrikam of the showcase: its three projects, their members and a short-lived machine. */
function seedFabrikam(db: MockDb): AccessAuditEntry[] {
  const description = (key: string) => db.accessPoints[key]?.Description
  const ada = accountActor('user_01MOCKADA')
  const grace = accountActor('user_01MOCKGRACE')
  const margaret = accountActor('user_01MOCKMARGARET')
  const authentication = (kinds: MachineAuthenticationPolicy['EnabledCredentialKinds']) =>
    machineAuthenticationValue({ EnabledCredentialKinds: kinds, CredentialKind: 'ApiKey', CertificateLifetimeDays: 90 })
  const mtls = (required: boolean) =>
    keyValuePolicyValue({
      AllowedProtections: ['Public', 'Standard'],
      DefaultSecretProtection: 'Standard',
      RequireMtlsForSecretDelivery: required,
      Recipients: [],
    })
  const pilot = machineValue({ CredentialKind: 'ApiKey', Scopes: ['ConfigurationRead'] })

  return seeded(
    [
      [
        300,
        'fabrikam',
        { Action: 'AccessPointCreated', Target: accountTarget('user_01MOCKADA'), NewValue: 'Owner' },
        ada,
      ],
      [
        300,
        'fabrikam.finance-cloud',
        {
          Action: 'AccessPointCreated',
          Target: accountTarget('user_01MOCKADA'),
          NewValue: 'Owner',
          Detail: description('fabrikam.finance-cloud'),
        },
        ada,
      ],
      [
        299,
        'fabrikam',
        { Action: 'MemberGranted', Target: accountTarget('user_01MOCKGRACE'), NewValue: 'Administrator' },
        ada,
      ],
      // DbCreator's data set showcase is this project (docs/Platform/Storyteller/data-sets.md)
      [
        290,
        'fabrikam.invoicing-suite',
        {
          Action: 'AccessPointCreated',
          Target: accountTarget('user_01MOCKGRACE'),
          NewValue: 'Owner',
          Detail: description('fabrikam.invoicing-suite'),
        },
        system,
      ],
      [
        289,
        'fabrikam.invoicing-suite',
        { Action: 'MemberGranted', Target: accountTarget('user_01MOCKADA'), NewValue: 'ContributorWithSecrets' },
        grace,
      ],
      [
        250,
        'fabrikam.fitness-cloud',
        {
          Action: 'AccessPointCreated',
          Target: accountTarget('user_01MOCKADA'),
          NewValue: 'Owner',
          Detail: description('fabrikam.fitness-cloud'),
        },
        ada,
      ],
      [
        249,
        'fabrikam.fitness-cloud',
        { Action: 'MemberGranted', Target: accountTarget('user_01MOCKMARGARET'), NewValue: 'Owner' },
        ada,
      ],
      [
        248,
        'fabrikam.fitness-cloud',
        {
          Action: 'MemberRoleChanged',
          Target: accountTarget('user_01MOCKADA'),
          OldValue: 'Owner',
          NewValue: 'Administrator',
        },
        margaret,
      ],
      [
        200,
        'fabrikam',
        {
          Action: 'MemberJoined',
          Target: accountTarget('user_01MOCKMARGARET'),
          NewValue: 'Contributor',
          Detail: 'Invitation inv_01SHOWMARGARET sent by Ada Lovelace (user_01MOCKADA).',
        },
        margaret,
      ],
      [
        150,
        'fabrikam.fitness-cloud',
        {
          Action: 'MachineAuthenticationPolicyChanged',
          OldValue: authentication(['ApiKey', 'Federated']),
          NewValue: authentication(['ApiKey', 'Certificate', 'Federated']),
        },
        margaret,
      ],
      [
        60,
        'fabrikam.finance-cloud',
        { Action: 'MachineCreated', Target: machineTarget('mach-pilot-import'), NewValue: pilot },
        ada,
      ],
      [
        40,
        'fabrikam.finance-cloud',
        { Action: 'MachineDeleted', Target: machineTarget('mach-pilot-import'), OldValue: pilot },
        ada,
      ],
      [
        10,
        'fabrikam.finance-cloud',
        { Action: 'KeyValuePolicyChanged', OldValue: mtls(false), NewValue: mtls(true) },
        ada,
      ],
      [
        3,
        'fabrikam.finance-cloud',
        {
          Action: 'MemberRoleChanged',
          Target: accountTarget('user_01MOCKKEN'),
          OldValue: 'Contributor',
          NewValue: 'Reader',
        },
        { ...grace, Via: 'mcp' },
      ],
    ],
    'fabrikam',
  )
}

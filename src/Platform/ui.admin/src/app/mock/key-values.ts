import type {
  AuditActor,
  AuditStamp,
  ClientEncryptionRecipient,
  KeyValue,
  KeyValueAuditEvent,
  KeyValueAuditEventOperation,
  KeyValuePolicy,
  KeyValueProtection,
  KeyValueSummary,
  KeyValueUsageDay,
  KeyValueUsageReport,
  KeyValueVersion,
} from '@42for.net/2splatform.sdk'
import { accountActor, mockActor } from './audit'
import type { MockDb } from './db'
import { showcaseKeyValues } from './showcase/key-values'

// The key/value pairs of the mock API (spec 29.8): a stateful store per scope with levels, versions with their
// expiry, the deleted state, the project policy, recipients, the audit trail and deterministic usage.

export interface StoredKeyValueVersion {
  Version: number
  Protection: KeyValueProtection
  Value: string
  ContentType?: string
  Modified: AuditStamp
  ExpirationTime?: string
  Recipients?: string[]
}

export interface StoredKeyValue {
  Key: string
  Created: AuditStamp
  Versions: StoredKeyValueVersion[]
  Deleted?: AuditStamp
  ExpiresAt?: string
}

export interface KeyValueScopeStore {
  seeded: boolean
  /** Pairs by their lower-cased key. */
  pairs: Record<string, StoredKeyValue>
}

export interface ScopeRef {
  org: string
  project: string
  view: string
}

const day = 86_400_000
export const versionRetentionDays = 365
export const deletedRetentionDays = 90
export const secretReferenceContentType = 'application/vnd.2s.secret-reference'

const now = () => new Date().toISOString()
const at = (offsetDays: number) => new Date(Date.now() + offsetDays * day).toISOString()

export const scopeIdOf = (scope: ScopeRef) => `${scope.org}/${scope.project}/${scope.view}`

/** A real 3072-bit RSA public key, so the browser can encrypt HYOK values for the mock project. */
export const mockRecipient: ClientEncryptionRecipient = {
  Id: 'invoicing-api',
  Name: 'Invoicing API',
  PublicKey: {
    Kty: 'RSA',
    N: 'yrZByMWftTVozr5gwdv2C564TkA9dJqT-CCKUL6T4VTnDP9PHUDl3Qwq2n1m9_Ioe4490btWdWk6Upur-oaexML4rSYY_gYYCE6lOXKG_eXDFQ0niJlHiAVmWc2jDdLftaMvedfwwAgjI21gKab8Gk45D-HI4IomIS-EzDH92y4ekT4ZyOMVQU1j2l-oY2OYNGCdZiBFTmehR-lR2kSP46J7RpABwBQ0cLU8ivJ46Dio_lbm954EOaVHXuakcUoxilPP-_45Y15wRyoBWy5ZLYAzZYMIGO1muEUCVgECIDwByBa_bTBm0f4k2iE3-KfHU5AZ6WZcjCG7WSmvJLA2XO3c72NfOICaBR-Xg0VHMARjMywY_DOIn33jSusiite17ipJ44treMvLDcocbPSG5xZ3ANGITlwoA4eLFgj3RHzNFJoMTxgG5VDuzmWgFhoL0dVIuB9dQUNczXH7BCMu66dBoF0y54MefiAUsv1rEm5b2Dj1DFzupuuhcC5UqimZ',
    E: 'AQAB',
  },
  IsEnabled: true,
  Created: { At: at(-40), By: accountActor('user_01MOCKADA') },
}

const base64Url = (text: string) => btoa(text).replaceAll('+', '-').replaceAll('/', '_').replace(/=+$/, '')

/** A syntactically valid client-encrypted document for the seeded recipient (not decryptable by anyone). */
export function placeholderJwe(place: string): string {
  return JSON.stringify({
    protected: base64Url(JSON.stringify({ enc: 'A256GCM', cty: 'text/plain', '2s': place })),
    recipients: [{ header: { alg: 'RSA-OAEP-256', kid: mockRecipient.Id }, encrypted_key: 'AA' }],
    iv: 'AAAAAAAAAAAAAAAA',
    ciphertext: 'AA',
    tag: 'AAAAAAAAAAAAAAAAAAAAAA',
  })
}

export function pair(
  key: string,
  protection: KeyValueProtection,
  values: string[],
  options: { contentType?: string; deletedDaysAgo?: number; recipients?: string[] } = {},
): StoredKeyValue {
  const author = accountActor('user_01MOCKADA')
  const grace = accountActor('user_01MOCKGRACE')
  const created = Date.now() - values.length * 9 * day
  const versions = values.map((value, index): StoredKeyValueVersion => {
    const version = index + 1
    const time = new Date(created + index * 9 * day).toISOString()
    return {
      Version: version,
      Protection: protection,
      Value: value,
      ContentType: options.contentType,
      Modified: { At: time, By: version % 2 ? author : grace },
      ExpirationTime: version === values.length ? undefined : new Date(created + index * 9 * day + versionRetentionDays * day).toISOString(),
      Recipients: options.recipients,
    }
  })
  return {
    Key: key,
    Created: { At: new Date(created).toISOString(), By: author },
    Versions: versions,
    ...(options.deletedDaysAgo !== undefined
      ? { Deleted: { At: at(-options.deletedDaysAgo), By: grace }, ExpiresAt: at(deletedRetentionDays - options.deletedDaysAgo) }
      : {}),
  }
}

function seedPairs(scope: ScopeRef): StoredKeyValue[] {
  const id = scopeIdOf(scope)
  if (id === 'acme/billing/default') {
    return [
      pair('db.host', 'Public', ['sql.internal', 'sql.internal.acme.example']),
      pair('db.port', 'Public', ['5432']),
      pair('db.password', 'Standard', ['s3cret-billing-1', 's3cret-billing-2']),
      pair('api.token', 'Hsm', ['tok_live_9f8e7d6c']),
      pair('app.name', 'Public', ['Billing']),
      pair('features.flags', 'Public', ['{"beta":false}', '{"beta":true,"newInvoice":false}'], { contentType: 'application/json' }),
      pair('vault.payment_key', 'Public', ['kv://acme-vault/payment-key'], { contentType: secretReferenceContentType }),
      pair('worker.key', 'Hyok', [placeholderJwe('acme/billing/default/worker.key')], { recipients: [mockRecipient.Id ?? 'invoicing-api'] }),
      pair('legacy.endpoint', 'Public', ['https://old.billing.example'], { deletedDaysAgo: 10 }),
    ]
  }
  if (id === 'acme/billing/staging') {
    return [pair('db.host', 'Public', ['sql.staging']), pair('db.password', 'Standard', ['staging-secret'])]
  }
  return showcaseKeyValues(id)
}

export function storeOf(db: MockDb, scope: ScopeRef): KeyValueScopeStore {
  const stores = (db.keyValues ??= {})
  const id = scopeIdOf(scope)
  let store = stores[id]
  if (!store) {
    store = { seeded: true, pairs: {} }
    for (const stored of seedPairs(scope)) {
      store.pairs[stored.Key.toLowerCase()] = stored
    }
    stores[id] = store
  }
  return store
}

export const normalizeKey = (key: string) => key.trim().toLowerCase()

export const keyPattern = /^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$/

export const isSecret = (protection: KeyValueProtection | undefined) => !!protection && protection !== 'Public'

export const latest = (stored: StoredKeyValue): StoredKeyValueVersion => stored.Versions.at(-1)!

export function toSummary(stored: StoredKeyValue, includeValues: boolean): KeyValueSummary {
  const current = latest(stored)
  return {
    Key: stored.Key,
    Protection: current.Protection,
    Value: includeValues && !isSecret(current.Protection) ? current.Value : undefined,
    ContentType: current.ContentType,
    IsReference: current.ContentType === secretReferenceContentType,
    Version: current.Version,
    Modified: current.Modified,
    IsDeleted: !!stored.Deleted,
    ExpiresAt: stored.ExpiresAt ?? null,
  }
}

export function toKeyValue(stored: StoredKeyValue, version: StoredKeyValueVersion, reveal: boolean): KeyValue {
  const secret = isSecret(version.Protection)
  return {
    Key: stored.Key,
    Protection: version.Protection,
    IsSecret: secret,
    Value: secret && !reveal ? undefined : version.Value,
    ContentType: version.ContentType,
    IsReference: version.ContentType === secretReferenceContentType,
    Recipients: version.Recipients,
    Version: version.Version,
    Created: stored.Created,
    Modified: version.Modified,
    IsDeleted: !!stored.Deleted,
    Deleted: stored.Deleted,
    ExpiresAt: stored.ExpiresAt ?? null,
  }
}

export function toVersion(stored: StoredKeyValue, version: StoredKeyValueVersion): KeyValueVersion {
  const current = latest(stored)
  return {
    Version: version.Version,
    Protection: version.Protection,
    Modified: version.Modified,
    ExpirationTime: version === current && !stored.Deleted ? undefined : version.ExpirationTime ?? stored.ExpiresAt,
    IsCurrent: version === current,
  }
}

/** Appends a version (and revives a deleted key); the previous version gets its expiry. */
export function writeVersion(
  store: KeyValueScopeStore,
  key: string,
  write: { Protection: KeyValueProtection; Value: string; ContentType?: string; Recipients?: string[] },
  author: AuditActor,
): StoredKeyValue {
  const id = normalizeKey(key)
  const existing = store.pairs[id]
  const time = now()
  const version: StoredKeyValueVersion = {
    Version: (existing ? Math.max(...existing.Versions.map((entry) => entry.Version)) : 0) + 1,
    Protection: write.Protection,
    Value: write.Value,
    ContentType: write.ContentType,
    Modified: { At: time, By: author },
    Recipients: write.Recipients,
  }
  if (existing) {
    const previous = latest(existing)
    previous.ExpirationTime = at(versionRetentionDays)
    existing.Versions.push(version)
    delete existing.Deleted
    delete existing.ExpiresAt
    return existing
  }
  const created: StoredKeyValue = { Key: key.trim(), Created: { At: time, By: author }, Versions: [version] }
  store.pairs[id] = created
  return created
}

export function softDelete(stored: StoredKeyValue, author: AuditActor): void {
  stored.Deleted = { At: now(), By: author }
  stored.ExpiresAt = at(deletedRetentionDays)
}

export function restore(stored: StoredKeyValue): void {
  delete stored.Deleted
  delete stored.ExpiresAt
}

// --- policy ---

const keyVersion = (seed: string) => {
  let value = 7
  for (const char of seed) {
    value = (value * 31 + char.charCodeAt(0)) >>> 0
  }
  return value.toString(16).padStart(8, '0').slice(0, 8)
}

export function policyOf(db: MockDb, projectKey: string): KeyValuePolicy {
  const policies = (db.keyValuePolicies ??= {})
  let policy = policies[projectKey]
  if (!policy) {
    const billing = projectKey === 'acme.billing'
    policy = {
      AllowedProtections: billing ? ['Public', 'Standard', 'Hsm', 'Hyok'] : ['Public', 'Standard'],
      DefaultSecretProtection: 'Standard',
      RequireMtlsForSecretDelivery: true,
      Standard: {
        KeyId: `https://st-keys.vault.azure.net/keys/kek-${keyVersion(projectKey)}${keyVersion(`${projectKey}/x`)}`,
        CurrentKeyVersion: keyVersion(`${projectKey}/standard/1`),
        NextRotationAt: at(300),
        CheckedAt: at(-0.1),
      },
      Hsm: billing
        ? {
            Backend: 'KeyVaultPremium',
            Key: {
              KeyId: `https://st-premium.vault.azure.net/keys/kek-${keyVersion(projectKey)}${keyVersion(`${projectKey}/x`)}`,
              CurrentKeyVersion: keyVersion(`${projectKey}/hsm/1`),
              NextRotationAt: at(200),
              CheckedAt: at(-0.1),
            },
            RotationInterval: '365.00:00:00',
          }
        : undefined,
      Recipients: billing ? [mockRecipient] : [],
      Version: billing ? 3 : 1,
      Modified: { At: at(-30), By: accountActor('user_01MOCKADA') },
    }
    policies[projectKey] = policy
  }
  return policy
}

// --- audit ---

export function auditOf(db: MockDb, projectKey: string): KeyValueAuditEvent[] {
  const audits = (db.keyValueAudit ??= {})
  let events = audits[projectKey]
  if (!events) {
    events = projectKey === 'acme.billing' ? seedAudit() : []
    audits[projectKey] = events
  }
  return events
}

let counter = 0

export function recordAudit(
  db: MockDb,
  projectKey: string,
  event: Partial<KeyValueAuditEvent> & { Operation: KeyValueAuditEventOperation },
): void {
  auditOf(db, projectKey).push({
    Id: `kva.${Date.now()}.${(counter++).toString(16)}`,
    Time: now(),
    Actor: mockActor(db),
    Channel: 'User',
    Count: 1,
    CorrelationId: crypto.randomUUID(),
    ...event,
  })
}

function seedAudit(): KeyValueAuditEvent[] {
  const entry = (daysAgo: number, event: Partial<KeyValueAuditEvent> & { Operation: KeyValueAuditEventOperation }): KeyValueAuditEvent => ({
    Id: `kva.seed.${daysAgo}.${event.Operation}`,
    Time: at(-daysAgo),
    Actor: accountActor('user_01MOCKADA'),
    Channel: 'User',
    Count: 1,
    CorrelationId: `00000000-0000-4000-8000-${String(daysAgo).padStart(12, '0')}`,
    ...event,
  })
  return [
    entry(27, { Operation: 'Create', View: 'default', Key: 'db.password', Version: 1, Protection: 'Standard' }),
    entry(20, { Operation: 'PolicyChange', Detail: 'AllowedProtections: Public, Standard, Hsm, Hyok' }),
    entry(18, { Operation: 'Create', View: 'default', Key: 'api.token', Version: 1, Protection: 'Hsm' }),
    entry(12, { Operation: 'Update', View: 'default', Key: 'db.password', Version: 2, Protection: 'Standard' }),
    entry(9, { Operation: 'Reveal', View: 'default', Key: 'db.password', Version: 2, Protection: 'Standard', Actor: accountActor('user_01MOCKGRACE') }),
    entry(6, { Operation: 'BindingRead', View: 'default', Key: 'db.password', Protection: 'Standard', Actor: { Kind: 'Machine', Id: 'mach-billing-sync' }, Channel: 'MachineMtls', Count: 143 }),
    entry(3, { Operation: 'Rotate', Protection: 'Standard', Detail: 'Key version b7c1e0a9', Channel: 'User' }),
    entry(3, { Operation: 'Rewrap', Protection: 'Standard', Detail: '4 envelopes', Actor: { Kind: 'System', Id: 'system' }, Channel: 'System' }),
    entry(1, { Operation: 'Export', View: 'default', Detail: '2 secrets revealed', Actor: accountActor('user_01MOCKGRACE') }),
  ]
}

// --- usage ---

const dayHash = (projectKey: string, date: string) => {
  let value = 11
  for (const char of `${projectKey}/${date}`) {
    value = (value * 33 + char.charCodeAt(0)) >>> 0
  }
  return value
}

export function usageReport(db: MockDb, projectKey: string, from: string, to: string): KeyValueUsageReport {
  const [org, project] = projectKey.split('.') as [string, string]
  const store = storeOf(db, { org, project, view: 'default' })
  const active = Object.values(store.pairs).filter((stored) => !stored.Deleted)
  const count = (level: KeyValueProtection) => active.filter((stored) => latest(stored).Protection === level && latest(stored).ContentType !== secretReferenceContentType).length
  const references = active.filter((stored) => latest(stored).ContentType === secretReferenceContentType).length
  const versions = Object.values(store.pairs).reduce((sum, stored) => sum + stored.Versions.length, 0)
  const bytes = Object.values(store.pairs).reduce((sum, stored) => sum + stored.Versions.reduce((inner, version) => inner + version.Value.length, 0), 0)

  const days: KeyValueUsageDay[] = []
  const start = new Date(`${from}T00:00:00Z`)
  const end = new Date(`${to}T00:00:00Z`)
  const today = new Date()
  for (let time = start.getTime(); time <= end.getTime() && time <= today.getTime(); time += day) {
    const date = new Date(time).toISOString().slice(0, 10)
    const hash = dayHash(projectKey, date)
    days.push({
      Date: date,
      Values: {
        'keys.public': count('Public'),
        'keys.reference': references,
        'keys.standard': count('Standard'),
        'keys.hsm': count('Hsm'),
        'keys.byok': count('Byok'),
        'keys.hyok': count('Hyok'),
        versions,
        'storage.bytes': bytes,
        'reads.api': 40 + (hash % 60),
        'reads.binding': 200 + (hash % 400),
        reveals: hash % 5,
        writes: hash % 8,
        'kms.standard': 10 + (hash % 20),
        'kms.hsm': hash % 10,
        'kms.byok': 0,
      },
    })
  }

  const totals: Record<string, number> = {}
  const gauges = new Set(['keys.public', 'keys.reference', 'keys.standard', 'keys.hsm', 'keys.byok', 'keys.hyok', 'versions', 'storage.bytes'])
  for (const entry of days) {
    for (const [meter, value] of Object.entries(entry.Values ?? {})) {
      totals[meter] = (totals[meter] ?? 0) + value
    }
  }
  for (const meter of gauges) {
    if (meter in totals) {
      totals[meter] = Math.round(((totals[meter] ?? 0) / 30) * 100) / 100
    }
  }

  return {
    From: from,
    To: to,
    Totals: totals,
    Days: days,
    EstimatedCharges: {
      'keys.standard': Math.round((totals['keys.standard'] ?? 0) * 0.1 * 100) / 100,
      'keys.hsm': Math.round((totals['keys.hsm'] ?? 0) * 0.4 * 100) / 100,
      'keys.hyok': Math.round((totals['keys.hyok'] ?? 0) * 0.1 * 100) / 100,
      reads: Math.round((((totals['reads.api'] ?? 0) + (totals['reads.binding'] ?? 0)) / 10_000) * 0.05 * 100) / 100,
    },
  }
}

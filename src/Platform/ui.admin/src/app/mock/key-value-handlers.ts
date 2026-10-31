import { http, HttpResponse, type HttpHandler } from 'msw'
import type {
  ClientEncryptionRecipient,
  ClientEncryptionRecipientCreate,
  ClientEncryptionRecipientUpdate,
  KeyValueBulkItemResult,
  KeyValueBulkRequest,
  KeyValueDocument,
  KeyValueExportRequest,
  KeyValueImportItemResult,
  KeyValuePolicyUpdate,
  KeyValueProtection,
  KeyValueRotateRequest,
  KeyValueWrite,
} from '@42for.net/2splatform.sdk'
import { mockActor, stamp } from './audit'
import type { MockDb } from './db'
import { diffTexts } from './diff'
import { errorResponse, requireRole, withDb } from './handlers'
import {
  auditOf,
  isSecret,
  keyPattern,
  latest,
  normalizeKey,
  policyOf,
  recordAudit,
  restore,
  secretReferenceContentType,
  softDelete,
  storeOf,
  toKeyValue,
  toSummary,
  toVersion,
  usageReport,
  writeVersion,
  type ScopeRef,
  type StoredKeyValue,
} from './key-values'

// The key/value API of the mock (spec 29.8): the same rules as the service for roles, levels, versions and plans.

const api = '*/api/v1'

type ProjectParams = { org: string; project: string }
type ScopeParams = ProjectParams & { view: string }
type KeyParams = ScopeParams & { key: string }

const projectKeyOf = (params: ProjectParams) => `${params.org}.${params.project}`
const scopeOf = (params: ScopeParams): ScopeRef => ({ org: params.org, project: params.project, view: params.view })

const referencePattern =
  /^(kv:\/\/[a-z0-9-]+\/[A-Za-z0-9-]+(\/[a-f0-9]{32})?|https:\/\/[a-z0-9-]+\.vault\.azure\.net\/secrets\/[A-Za-z0-9-]+(\/[a-f0-9]{32})?)\/?$/i

const notFound = (view: string, key: string) => errorResponse(404, `The key '${key}' does not exist in view '${view}'.`, 'KeyValueNotFound')

const hasSecretRights = (db: MockDb, projectKey: string) => !requireRole(db, projectKey, 'ContributorWithSecrets')

function listPairs(db: MockDb, scope: ScopeRef, state: string | null, prefix: string | null, levels: string[] | null): StoredKeyValue[] {
  const store = storeOf(db, scope)
  return Object.values(store.pairs)
    .filter((stored) => (state === 'All' ? true : state === 'Deleted' ? !!stored.Deleted : !stored.Deleted))
    .filter((stored) => !prefix || stored.Key.toLowerCase().startsWith(prefix.toLowerCase()))
    .filter((stored) => !levels?.length || levels.includes(latest(stored).Protection))
    .toSorted((a, b) => a.Key.localeCompare(b.Key))
}

/** The checks a write shares with the import: key, level, rights, reference and client-encrypted shapes. */
function validateWrite(
  db: MockDb,
  scope: ScopeRef,
  key: string,
  protection: KeyValueProtection,
  value: string,
  contentType: string | undefined,
): { error?: Response; recipients?: string[] } {
  const projectKey = `${scope.org}.${scope.project}`
  const policy = policyOf(db, projectKey)

  if (!keyPattern.test(key) || key.length > 128) {
    return { error: errorResponse(400, `The key '${key}' is not valid.`, 'InvalidKey') }
  }
  if (!(policy.AllowedProtections ?? []).includes(protection)) {
    return {
      error: errorResponse(409, `The protection level '${protection}' is not allowed in this project (allowed: ${(policy.AllowedProtections ?? []).join(', ')}).`, 'ProtectionNotAllowed'),
    }
  }
  if (isSecret(protection) && !hasSecretRights(db, projectKey)) {
    return { error: errorResponse(403, `The key '${key}' is a secret; the caller has no rights to secrets.`, 'AccessDenied') }
  }
  if (new TextEncoder().encode(value).length > (protection === 'Hyok' ? 49_152 : 24_576)) {
    return { error: errorResponse(400, `The value of '${key}' is too large.`, 'ValueTooLarge') }
  }
  if (contentType === secretReferenceContentType) {
    if (isSecret(protection)) {
      return { error: errorResponse(400, 'A reference is a public pair; the platform never resolves it.', 'InvalidSecretReference') }
    }
    if (!referencePattern.test(value.trim())) {
      return { error: errorResponse(400, `'${value}' is not a Key Vault secret reference (kv://vault/name or the vault URI).`, 'InvalidSecretReference') }
    }
  }
  if (protection === 'Hyok') {
    try {
      const document = JSON.parse(value) as { protected?: string; recipients?: { header?: { kid?: string } }[] }
      const header = JSON.parse(atob((document.protected ?? '').replaceAll('-', '+').replaceAll('_', '/'))) as { enc?: string; '2s'?: string }
      const place = `${scope.org}/${scope.project}/${scope.view}/${normalizeKey(key)}`
      if (header.enc !== 'A256GCM' || header['2s'] !== place || !document.recipients?.length) {
        return { error: errorResponse(400, 'The client-encrypted value is not a JWE for this key.', 'InvalidClientEncryptedValue') }
      }
      const enabled = new Set((policy.Recipients ?? []).filter((recipient) => recipient.IsEnabled !== false).map((recipient) => recipient.Id))
      const kids = document.recipients.map((recipient) => recipient.header?.kid ?? '')
      const unknown = kids.find((kid) => !enabled.has(kid))
      if (unknown !== undefined) {
        return { error: errorResponse(400, `The recipient '${unknown}' is unknown or disabled.`, 'UnknownRecipient') }
      }
      return { recipients: kids }
    } catch {
      return { error: errorResponse(400, 'The client-encrypted value is not JSON.', 'InvalidClientEncryptedValue') }
    }
  }
  return {}
}

const auditedWrite = (db: MockDb, scope: ScopeRef, stored: StoredKeyValue, created: boolean) => {
  const version = latest(stored)
  if (isSecret(version.Protection)) {
    recordAudit(db, `${scope.org}.${scope.project}`, {
      Operation: created ? 'Create' : 'Update',
      View: scope.view,
      Key: stored.Key,
      Version: version.Version,
      Protection: version.Protection,
    })
  }
}

function bulkKeys(store: Record<string, StoredKeyValue>, request: KeyValueBulkRequest): string[] {
  if (request.Keys?.length) {
    return request.Keys.map((key) => key.trim())
  }
  const prefix = (request.KeyPrefix ?? '').toLowerCase()
  return Object.values(store)
    .filter((stored) => stored.Key.toLowerCase().startsWith(prefix))
    .map((stored) => stored.Key)
}

const projectHandlers: HttpHandler[] = [
  http.get(
    `${api}/:org/:project/key-values/policy`,
    withDb<ProjectParams>((db, { params }) => requireRole(db, projectKeyOf(params), 'Reader') ?? HttpResponse.json(policyOf(db, projectKeyOf(params)))),
  ),

  http.put(
    `${api}/:org/:project/key-values/policy`,
    withDb<ProjectParams>(async (db, { params, request }) => {
      const projectKey = projectKeyOf(params)
      const denied = requireRole(db, projectKey, 'Administrator')
      if (denied) {
        return denied
      }
      const update = (await request.json()) as KeyValuePolicyUpdate
      const policy = policyOf(db, projectKey)

      if (update.ExpectedVersion !== undefined && update.ExpectedVersion !== null && update.ExpectedVersion !== policy.Version) {
        return errorResponse(409, `The policy is at version ${policy.Version}, not ${update.ExpectedVersion}.`, 'PolicyVersionMismatch')
      }

      if (update.AllowedProtections) {
        const allowed = [...new Set(['Public', ...update.AllowedProtections])] as KeyValueProtection[]
        const removed = (policy.AllowedProtections ?? []).filter((level) => !allowed.includes(level))
        // the stores of the project's views are seeded on first use; count them all
        for (const view of new Set(['default', ...(db.views[projectKey] ?? []).map((entry) => entry.Name ?? 'default')])) {
          storeOf(db, { org: params.org, project: params.project, view })
        }
        for (const level of removed) {
          const counts: Record<string, number> = {}
          for (const [scopeId, store] of Object.entries(db.keyValues ?? {})) {
            if (!scopeId.startsWith(`${params.org}/${params.project}/`)) {
              continue
            }
            const count = Object.values(store.pairs).filter((stored) => latest(stored).Protection === level).length
            if (count) {
              counts[scopeId.split('/')[2]!] = count
            }
          }
          if (Object.keys(counts).length) {
            const detail = Object.entries(counts)
              .map(([view, count]) => `${view}: ${count}`)
              .join(', ')
            return errorResponse(409, `The protection level '${level}' is still used by keys of the project (${detail}); change them first.`, 'ProtectionInUse')
          }
        }
        if (allowed.includes('Byok') && !policy.Byok?.VerifiedAt) {
          return errorResponse(409, 'Set and verify the customer key before allowing Byok.', 'ByokNotConfigured')
        }
        if (allowed.includes('Hyok') && !(policy.Recipients ?? []).some((recipient) => recipient.IsEnabled !== false)) {
          return errorResponse(409, 'Add an enabled recipient before allowing Hyok.', 'ProtectionNotAllowed')
        }
        if (allowed.includes('Hsm') && !policy.Hsm) {
          policy.Hsm = {
            Backend: 'KeyVaultPremium',
            Key: { KeyId: `https://st-premium.vault.azure.net/keys/kek-${projectKey.replace('.', '-')}`, CurrentKeyVersion: '0f1e2d3c', NextRotationAt: new Date(Date.now() + 365 * 86_400_000).toISOString() },
            RotationInterval: '365.00:00:00',
          }
        }
        policy.AllowedProtections = allowed
      }
      if (update.DefaultSecretProtection) {
        if (!isSecret(update.DefaultSecretProtection) || !(policy.AllowedProtections ?? []).includes(update.DefaultSecretProtection)) {
          return errorResponse(400, 'The default secret level must be an allowed secret level.', 'ProtectionNotAllowed')
        }
        policy.DefaultSecretProtection = update.DefaultSecretProtection
      }
      if (update.RequireMtlsForSecretDelivery !== undefined && update.RequireMtlsForSecretDelivery !== null) {
        policy.RequireMtlsForSecretDelivery = update.RequireMtlsForSecretDelivery
      }
      if (update.HsmRotationInterval && policy.Hsm) {
        policy.Hsm.RotationInterval = update.HsmRotationInterval
      }
      if (update.Byok) {
        if (!update.Byok.KeyUri) {
          if ((policy.AllowedProtections ?? []).includes('Byok')) {
            return errorResponse(409, "The protection level 'Byok' is still allowed; disable it before removing the key.", 'ProtectionInUse')
          }
          policy.Byok = undefined
        } else {
          const changed = policy.Byok?.KeyUri !== update.Byok.KeyUri.replace(/\/[a-f0-9]{32}$/i, '') || policy.Byok?.TenantId !== update.Byok.TenantId
          if (changed && (policy.AllowedProtections ?? []).includes('Byok')) {
            return errorResponse(409, "The protection level 'Byok' is allowed; disable it before changing the key.", 'ProtectionInUse')
          }
          policy.Byok = { KeyUri: update.Byok.KeyUri.replace(/\/[a-f0-9]{32}$/i, ''), TenantId: update.Byok.TenantId ?? '', Health: 'Unknown' }
        }
      }
      policy.Version = (policy.Version ?? 0) + 1
      policy.Modified = stamp(db)
      recordAudit(db, projectKey, { Operation: 'PolicyChange', Detail: `Allowed: ${(policy.AllowedProtections ?? []).join(', ')}` })
      return HttpResponse.json(policy)
    }),
  ),

  http.get(
    `${api}/:org/:project/key-values/policy/byok/setup`,
    withDb<ProjectParams>((db, { params }) =>
      requireRole(db, projectKeyOf(params), 'Administrator') ??
      HttpResponse.json({
        ClientId: '3b1f6e2a-0c4d-4e8f-9a1b-2c3d4e5f6a7b',
        ConsentUrl: 'https://login.microsoftonline.com/organizations/adminconsent?client_id=3b1f6e2a-0c4d-4e8f-9a1b-2c3d4e5f6a7b',
        RequiredRole: 'Key Vault Crypto Service Encryption User',
      }),
    ),
  ),

  http.post(
    `${api}/:org/:project/key-values/policy/byok/verify`,
    withDb<ProjectParams>((db, { params }) => {
      const projectKey = projectKeyOf(params)
      const denied = requireRole(db, projectKey, 'Administrator')
      if (denied) {
        return denied
      }
      const policy = policyOf(db, projectKey)
      if (!policy.Byok) {
        return errorResponse(409, 'No customer key is set.', 'ByokNotConfigured')
      }
      const valid = /^https:\/\/[a-z0-9-]+\.vault\.azure\.net\/keys\/[A-Za-z0-9-]+\/?$/i.test(policy.Byok.KeyUri ?? '') && /^[0-9a-f-]{36}$/i.test(policy.Byok.TenantId ?? '')
      if (!valid) {
        policy.Byok = { ...policy.Byok, Health: 'Unavailable', LastCheckedAt: new Date().toISOString(), LastError: 'The key could not be read: 404 KeyNotFound.' }
        return errorResponse(424, 'The customer key could not be verified: the key was not found or the role is missing.', 'CustomerKeyInvalid')
      }
      policy.Byok = { ...policy.Byok, Health: 'Healthy', VerifiedAt: new Date().toISOString(), LastCheckedAt: new Date().toISOString(), CurrentKeyVersion: 'c0ffee42', LastError: undefined }
      recordAudit(db, projectKey, { Operation: 'ByokVerify', Detail: policy.Byok.KeyUri })
      return HttpResponse.json(policy)
    }),
  ),

  http.post(
    `${api}/:org/:project/key-values/policy/rotate`,
    withDb<ProjectParams>(async (db, { params, request }) => {
      const projectKey = projectKeyOf(params)
      const denied = requireRole(db, projectKey, 'Administrator')
      if (denied) {
        return denied
      }
      const body = (await request.json()) as KeyValueRotateRequest
      const policy = policyOf(db, projectKey)
      const key = body.Protection === 'Standard' ? policy.Standard : body.Protection === 'Hsm' ? policy.Hsm?.Key : undefined
      if (!key) {
        return errorResponse(400, 'Only the Standard and Hsm keys are rotated by the platform.', 'NotSupportedForProtection')
      }
      const next = Math.random().toString(16).slice(2, 10)
      key.CurrentKeyVersion = next
      key.NextRotationAt = new Date(Date.now() + 365 * 86_400_000).toISOString()
      const envelopes = Object.values(db.keyValues ?? {})
        .flatMap((store) => Object.values(store.pairs))
        .reduce((sum, stored) => sum + stored.Versions.filter((version) => version.Protection === body.Protection).length, 0)
      policy.Rewrap = { Protection: body.Protection, TargetKeyVersion: next, StartedAt: new Date().toISOString(), Processed: envelopes, Remaining: 0, CompletedAt: new Date().toISOString() }
      policy.Version = (policy.Version ?? 0) + 1
      recordAudit(db, projectKey, { Operation: 'Rotate', Protection: body.Protection, Detail: `Key version ${next}` })
      return HttpResponse.json(policy)
    }),
  ),

  http.get(
    `${api}/:org/:project/key-values/recipients`,
    withDb<ProjectParams>((db, { params }) => requireRole(db, projectKeyOf(params), 'Reader') ?? HttpResponse.json(policyOf(db, projectKeyOf(params)).Recipients ?? [])),
  ),

  http.post(
    `${api}/:org/:project/key-values/recipients`,
    withDb<ProjectParams>(async (db, { params, request }) => {
      const projectKey = projectKeyOf(params)
      const denied = requireRole(db, projectKey, 'Administrator')
      if (denied) {
        return denied
      }
      const body = (await request.json()) as ClientEncryptionRecipientCreate
      const policy = policyOf(db, projectKey)
      const recipients = (policy.Recipients ??= [])
      const id = (body.Id ?? `rcp-${Math.random().toString(36).slice(2, 8)}`).trim()
      if (!/^[A-Za-z0-9_-]{1,64}$/.test(id)) {
        return errorResponse(400, `The recipient id '${id}' is not valid.`, 'RecipientInvalid')
      }
      if (recipients.some((recipient) => recipient.Id === id)) {
        return errorResponse(409, `The recipient '${id}' exists.`, 'RecipientExists')
      }
      if (recipients.length >= 20) {
        return errorResponse(409, 'A project has at most 20 recipients.', 'RecipientInvalid')
      }
      let publicKey: ClientEncryptionRecipient['PublicKey']
      const text = (body.PublicKey ?? '').trim()
      if (text.startsWith('{')) {
        try {
          const jwk = JSON.parse(text) as { kty?: string; n?: string; e?: string }
          if (jwk.kty !== 'RSA' || !jwk.n || !jwk.e) {
            return errorResponse(400, 'The JWK must be an RSA public key with n and e.', 'RecipientInvalid')
          }
          if (Math.floor((jwk.n.length * 6) / 8) * 8 < 3072) {
            return errorResponse(400, 'The recipient key must have at least 3072 bits.', 'RecipientInvalid')
          }
          publicKey = { Kty: 'RSA', N: jwk.n, E: jwk.e }
        } catch {
          return errorResponse(400, 'The public key is neither PEM nor a JWK object.', 'RecipientInvalid')
        }
      } else if (text.includes('BEGIN PUBLIC KEY')) {
        // The mock does not parse SPKI; the key is accepted as a 3072-bit placeholder.
        publicKey = { Kty: 'RSA', N: 'A'.repeat(512), E: 'AQAB' }
      } else {
        return errorResponse(400, 'The public key is neither PEM nor a JWK object.', 'RecipientInvalid')
      }
      const recipient: ClientEncryptionRecipient = { Id: id, Name: body.Name, PublicKey: publicKey, IsEnabled: true, Created: stamp(db) }
      recipients.push(recipient)
      policy.Version = (policy.Version ?? 0) + 1
      recordAudit(db, projectKey, { Operation: 'RecipientAdded', Detail: id })
      return HttpResponse.json(recipient)
    }),
  ),

  http.patch(
    `${api}/:org/:project/key-values/recipients/:id`,
    withDb<ProjectParams & { id: string }>(async (db, { params, request }) => {
      const projectKey = projectKeyOf(params)
      const denied = requireRole(db, projectKey, 'Administrator')
      if (denied) {
        return denied
      }
      const body = (await request.json()) as ClientEncryptionRecipientUpdate
      const policy = policyOf(db, projectKey)
      const recipient = (policy.Recipients ?? []).find((entry) => entry.Id === params.id)
      if (!recipient) {
        return errorResponse(404, `The recipient '${params.id}' does not exist.`, 'RecipientNotFound')
      }
      if (body.IsEnabled === false && recipient.IsEnabled !== false && (policy.AllowedProtections ?? []).includes('Hyok')) {
        const others = (policy.Recipients ?? []).filter((entry) => entry.Id !== recipient.Id && entry.IsEnabled !== false)
        if (others.length === 0) {
          return errorResponse(409, 'The last enabled recipient cannot be disabled while Hyok is allowed.', 'ProtectionInUse')
        }
      }
      if (body.Name) {
        recipient.Name = body.Name
      }
      if (body.IsEnabled !== undefined && body.IsEnabled !== null) {
        recipient.IsEnabled = body.IsEnabled
      }
      policy.Version = (policy.Version ?? 0) + 1
      recordAudit(db, projectKey, { Operation: 'RecipientChanged', Detail: recipient.Id })
      return HttpResponse.json(recipient)
    }),
  ),

  http.delete(
    `${api}/:org/:project/key-values/recipients/:id`,
    withDb<ProjectParams & { id: string }>((db, { params }) => {
      const projectKey = projectKeyOf(params)
      const denied = requireRole(db, projectKey, 'Administrator')
      if (denied) {
        return denied
      }
      const policy = policyOf(db, projectKey)
      const recipient = (policy.Recipients ?? []).find((entry) => entry.Id === params.id)
      if (!recipient) {
        return errorResponse(404, `The recipient '${params.id}' does not exist.`, 'RecipientNotFound')
      }
      const others = (policy.Recipients ?? []).filter((entry) => entry.Id !== recipient.Id && entry.IsEnabled !== false)
      if (recipient.IsEnabled !== false && others.length === 0 && (policy.AllowedProtections ?? []).includes('Hyok')) {
        return errorResponse(409, 'The last enabled recipient cannot be removed while Hyok is allowed.', 'ProtectionInUse')
      }
      policy.Recipients = (policy.Recipients ?? []).filter((entry) => entry.Id !== recipient.Id)
      policy.Version = (policy.Version ?? 0) + 1
      recordAudit(db, projectKey, { Operation: 'RecipientRemoved', Detail: recipient.Id })
      return new HttpResponse(null, { status: 204 })
    }),
  ),

  http.get(
    `${api}/:org/:project/key-values/audit`,
    withDb<ProjectParams>((db, { params, request }) => {
      const projectKey = projectKeyOf(params)
      const denied = requireRole(db, projectKey, 'Administrator')
      if (denied) {
        return denied
      }
      const url = new URL(request.url)
      const view = url.searchParams.get('view')
      const key = url.searchParams.get('key')?.toLowerCase()
      const operation = url.searchParams.get('operation')
      const from = url.searchParams.get('from')
      const to = url.searchParams.get('to')
      const offset = Number(url.searchParams.get('continuationToken') ?? 0) || 0
      const events = auditOf(db, projectKey)
        .filter((event) => !view || event.View === view)
        .filter((event) => !key || event.Key?.toLowerCase() === key)
        .filter((event) => !operation || event.Operation?.toLowerCase() === operation.toLowerCase())
        .filter((event) => !from || (event.Time ?? '') >= new Date(from).toISOString())
        .filter((event) => !to || (event.Time ?? '') < new Date(to).toISOString())
        .toSorted((a, b) => (b.Time ?? '').localeCompare(a.Time ?? ''))
      const page = events.slice(offset, offset + 50)
      return HttpResponse.json({ Items: page, Count: page.length, ContinuationToken: offset + 50 < events.length ? String(offset + 50) : null })
    }),
  ),

  http.get(
    `${api}/:org/:project/usage/key-values`,
    withDb<ProjectParams>((db, { params, request }) => {
      const projectKey = projectKeyOf(params)
      const denied = requireRole(db, projectKey, 'Administrator')
      if (denied) {
        return denied
      }
      const url = new URL(request.url)
      const today = new Date().toISOString().slice(0, 10)
      const to = url.searchParams.get('to') ?? today
      const from = url.searchParams.get('from') ?? new Date(new Date(`${to}T00:00:00Z`).getTime() - 30 * 86_400_000).toISOString().slice(0, 10)
      if (to < from) {
        return errorResponse(400, 'The end of the range is before its start.', 'InvalidQuery')
      }
      return HttpResponse.json(usageReport(db, projectKey, from, to))
    }),
  ),
]

const viewHandlers: HttpHandler[] = [
  http.get(
    `${api}/:org/:project/:view/key-values`,
    withDb<ScopeParams>((db, { params, request }) => {
      const denied = requireRole(db, projectKeyOf(params), 'Reader')
      if (denied) {
        return denied
      }
      const url = new URL(request.url)
      const levels = url.searchParams.getAll('protection').flatMap((value) => value.split(',')).filter(Boolean)
      const includeValues = url.searchParams.get('includeValues') === 'true'
      const offset = Number(url.searchParams.get('continuationToken') ?? 0) || 0
      const pairs = listPairs(db, scopeOf(params), url.searchParams.get('state'), url.searchParams.get('keyPrefix'), levels)
      const page = pairs.slice(offset, offset + 100)
      return HttpResponse.json({
        Items: page.map((stored) => toSummary(stored, includeValues)),
        Count: page.length,
        ContinuationToken: offset + 100 < pairs.length ? String(offset + 100) : null,
      })
    }),
  ),

  http.get(
    `${api}/:org/:project/:view/key-values/:key`,
    withDb<KeyParams>((db, { params, request }) => {
      const projectKey = projectKeyOf(params)
      const denied = requireRole(db, projectKey, 'Reader')
      if (denied) {
        return denied
      }
      const url = new URL(request.url)
      const reveal = url.searchParams.get('reveal') === 'true'
      const includeDeleted = url.searchParams.get('includeDeleted') === 'true'
      const stored = storeOf(db, scopeOf(params)).pairs[normalizeKey(params.key)]
      if (!stored || (stored.Deleted && !includeDeleted)) {
        return notFound(params.view, params.key)
      }
      const current = latest(stored)
      if (reveal && isSecret(current.Protection)) {
        if (!hasSecretRights(db, projectKey)) {
          return errorResponse(403, `The key '${stored.Key}' is a secret; the caller has no rights to secrets.`, 'AccessDenied')
        }
        recordAudit(db, projectKey, { Operation: 'Reveal', View: params.view, Key: stored.Key, Version: current.Version, Protection: current.Protection })
      }
      return HttpResponse.json(toKeyValue(stored, current, reveal))
    }),
  ),

  http.put(
    `${api}/:org/:project/:view/key-values/:key`,
    withDb<KeyParams>(async (db, { params, request }) => {
      const projectKey = projectKeyOf(params)
      const denied = requireRole(db, projectKey, 'Contributor')
      if (denied) {
        return denied
      }
      const write = (await request.json()) as KeyValueWrite
      const scope = scopeOf(params)
      const store = storeOf(db, scope)
      const existing = store.pairs[normalizeKey(params.key)]
      const active = existing && !existing.Deleted ? existing : undefined
      const protection = (write.Protection ?? active?.Versions.at(-1)?.Protection) as KeyValueProtection | undefined

      if (!protection) {
        return errorResponse(400, `The protection level is required to create the key '${params.key}'.`, 'ProtectionRequired')
      }
      if (write.ExpectedVersion !== undefined && write.ExpectedVersion !== null) {
        const actual = active?.Versions.at(-1)?.Version ?? 0
        if (write.ExpectedVersion !== actual) {
          return errorResponse(412, `The key '${params.key}' is at version ${actual}, not ${write.ExpectedVersion}. Reload and retry.`, 'KeyValueVersionMismatch')
        }
      }
      if (active && isSecret(active.Versions.at(-1)?.Protection) !== isSecret(protection) && !hasSecretRights(db, projectKey)) {
        return errorResponse(403, `The key '${params.key}' changes between public and secret; the caller has no rights to secrets.`, 'AccessDenied')
      }
      const checked = validateWrite(db, scope, params.key, protection, write.Value ?? '', write.ContentType ?? undefined)
      if (checked.error) {
        return checked.error
      }
      const stored = writeVersion(store, params.key, { Protection: protection, Value: write.Value ?? '', ContentType: write.ContentType ?? undefined, Recipients: checked.recipients }, mockActor(db))
      auditedWrite(db, scope, stored, !active)
      return HttpResponse.json(toKeyValue(stored, latest(stored), false))
    }),
  ),

  http.delete(
    `${api}/:org/:project/:view/key-values/:key`,
    withDb<KeyParams>((db, { params, request }) => {
      const projectKey = projectKeyOf(params)
      const url = new URL(request.url)
      const permanent = url.searchParams.get('permanent') === 'true'
      const denied = requireRole(db, projectKey, permanent ? 'Administrator' : 'Contributor')
      if (denied) {
        return denied
      }
      const store = storeOf(db, scopeOf(params))
      const id = normalizeKey(params.key)
      const stored = store.pairs[id]
      if (!stored || (stored.Deleted && !permanent)) {
        return notFound(params.view, params.key)
      }
      const current = latest(stored)
      if (isSecret(current.Protection) && !hasSecretRights(db, projectKey)) {
        return errorResponse(403, `The key '${stored.Key}' is a secret; the caller has no rights to secrets.`, 'AccessDenied')
      }
      const expected = url.searchParams.get('expectedVersion')
      if (expected && Number(expected) !== current.Version) {
        return errorResponse(412, `The key '${stored.Key}' is at version ${current.Version}, not ${expected}.`, 'KeyValueVersionMismatch')
      }
      if (permanent) {
        delete store.pairs[id]
      } else {
        softDelete(stored, mockActor(db))
      }
      if (isSecret(current.Protection)) {
        recordAudit(db, projectKey, { Operation: permanent ? 'PermanentDelete' : 'Delete', View: params.view, Key: stored.Key, Version: current.Version, Protection: current.Protection })
      }
      return new HttpResponse(null, { status: 204 })
    }),
  ),

  http.post(
    `${api}/:org/:project/:view/key-values/restore`,
    withDb<ScopeParams>(async (db, { params, request }) => {
      const projectKey = projectKeyOf(params)
      const denied = requireRole(db, projectKey, 'Contributor')
      if (denied) {
        return denied
      }
      const body = (await request.json()) as KeyValueBulkRequest
      const store = storeOf(db, scopeOf(params))
      const items = bulkKeys(store.pairs, body).map((key): KeyValueBulkItemResult => {
        const stored = store.pairs[normalizeKey(key)]
        if (!stored || !stored.Deleted) {
          return { Key: key, Outcome: stored ? 'Unchanged' : 'NotFound' }
        }
        if (isSecret(latest(stored).Protection) && !hasSecretRights(db, projectKey)) {
          return { Key: key, Outcome: 'Forbidden', ErrorCode: 'AccessDenied', Message: 'A secret; no secret rights.' }
        }
        restore(stored)
        if (isSecret(latest(stored).Protection)) {
          recordAudit(db, projectKey, { Operation: 'Restore', View: params.view, Key: stored.Key, Version: latest(stored).Version, Protection: latest(stored).Protection })
        }
        return { Key: stored.Key, Outcome: 'Done', Version: latest(stored).Version }
      })
      return HttpResponse.json({ Items: items })
    }),
  ),

  http.post(
    `${api}/:org/:project/:view/key-values/purge`,
    withDb<ScopeParams>(async (db, { params, request }) => {
      const projectKey = projectKeyOf(params)
      const denied = requireRole(db, projectKey, 'Administrator')
      if (denied) {
        return denied
      }
      const body = (await request.json()) as KeyValueBulkRequest
      const store = storeOf(db, scopeOf(params))
      const items = bulkKeys(store.pairs, body).map((key): KeyValueBulkItemResult => {
        const id = normalizeKey(key)
        const stored = store.pairs[id]
        if (!stored?.Deleted) {
          return { Key: key, Outcome: 'NotFound', Message: stored ? 'Not deleted.' : undefined }
        }
        delete store.pairs[id]
        if (isSecret(latest(stored).Protection)) {
          recordAudit(db, projectKey, { Operation: 'Purge', View: params.view, Key: stored.Key, Protection: latest(stored).Protection })
        }
        return { Key: stored.Key, Outcome: 'Done' }
      })
      return HttpResponse.json({ Items: items })
    }),
  ),

  http.post(
    `${api}/:org/:project/:view/key-values/protect`,
    withDb<ScopeParams>(async (db, { params, request }) => {
      const projectKey = projectKeyOf(params)
      const denied = requireRole(db, projectKey, 'Contributor')
      if (denied) {
        return denied
      }
      const body = (await request.json()) as KeyValueBulkRequest
      const target = body.Protection as KeyValueProtection | undefined
      if (!target) {
        return errorResponse(400, 'The target Protection is required.', 'ProtectionRequired')
      }
      const policy = policyOf(db, projectKey)
      if (!(policy.AllowedProtections ?? []).includes(target)) {
        return errorResponse(409, `The protection level '${target}' is not allowed in this project.`, 'ProtectionNotAllowed')
      }
      const store = storeOf(db, scopeOf(params))
      const items = bulkKeys(store.pairs, body).map((key): KeyValueBulkItemResult => {
        const stored = store.pairs[normalizeKey(key)]
        if (!stored || stored.Deleted) {
          return { Key: key, Outcome: 'NotFound' }
        }
        const current = latest(stored)
        if (!hasSecretRights(db, projectKey)) {
          return { Key: stored.Key, Outcome: 'Forbidden', ErrorCode: 'AccessDenied' }
        }
        if (target === 'Public' || target === 'Hyok' || current.Protection === 'Public' || current.Protection === 'Hyok') {
          return { Key: stored.Key, Outcome: 'NotAllowed', ErrorCode: 'ReprotectNotPossible', Message: 'Only Standard, Hsm and Byok can be changed on the server; Public and Hyok need a new value from the client.' }
        }
        if (current.Protection === target) {
          return { Key: stored.Key, Outcome: 'Unchanged', Version: current.Version }
        }
        writeVersion(store, stored.Key, { Protection: target, Value: current.Value, ContentType: current.ContentType }, mockActor(db))
        recordAudit(db, projectKey, { Operation: 'Reprotect', View: params.view, Key: stored.Key, Version: latest(stored).Version, Protection: target, Detail: `${current.Protection} -> ${target}` })
        return { Key: stored.Key, Outcome: 'Done', Version: latest(stored).Version }
      })
      return HttpResponse.json({ Items: items })
    }),
  ),

  http.get(
    `${api}/:org/:project/:view/key-values/:key/versions`,
    withDb<KeyParams>((db, { params }) => {
      const denied = requireRole(db, projectKeyOf(params), 'Reader')
      if (denied) {
        return denied
      }
      const stored = storeOf(db, scopeOf(params)).pairs[normalizeKey(params.key)]
      return stored ? HttpResponse.json(stored.Versions.map((version) => toVersion(stored, version))) : notFound(params.view, params.key)
    }),
  ),

  http.get(
    `${api}/:org/:project/:view/key-values/:key/versions/:version`,
    withDb<KeyParams & { version: string }>((db, { params, request }) => {
      const projectKey = projectKeyOf(params)
      const denied = requireRole(db, projectKey, 'Reader')
      if (denied) {
        return denied
      }
      const stored = storeOf(db, scopeOf(params)).pairs[normalizeKey(params.key)]
      const version = stored?.Versions.find((entry) => entry.Version === Number(params.version))
      if (!stored || !version) {
        return errorResponse(404, `The version ${params.version} of the key '${params.key}' does not exist.`, 'KeyValueNotFound')
      }
      const reveal = new URL(request.url).searchParams.get('reveal') === 'true'
      if (reveal && isSecret(version.Protection)) {
        if (!hasSecretRights(db, projectKey)) {
          return errorResponse(403, `The key '${stored.Key}' is a secret; the caller has no rights to secrets.`, 'AccessDenied')
        }
        recordAudit(db, projectKey, { Operation: 'Reveal', View: params.view, Key: stored.Key, Version: version.Version, Protection: version.Protection, Detail: `version ${version.Version}` })
      }
      return HttpResponse.json(toKeyValue(stored, version, reveal))
    }),
  ),

  http.get(
    `${api}/:org/:project/:view/key-values/:key/versions/:version/diff{/:from}?`,
    withDb<KeyParams & { version: string; from?: string }>((db, { params }) => {
      const projectKey = projectKeyOf(params)
      const denied = requireRole(db, projectKey, 'Reader')
      if (denied) {
        return denied
      }
      const stored = storeOf(db, scopeOf(params)).pairs[normalizeKey(params.key)]
      const to = Number(params.version)
      const from = params.from === undefined ? to - 1 : Number(params.from)
      const text = (version: number): string | undefined => {
        if (version === 0) {
          return ''
        }
        const entry = stored?.Versions.find((candidate) => candidate.Version === version)
        if (!entry) {
          return undefined
        }
        if (entry.Protection === 'Hyok') {
          throw new Error('hyok')
        }
        if (isSecret(entry.Protection) && !hasSecretRights(db, projectKey)) {
          throw new Error('forbidden')
        }
        return entry.Value
      }
      try {
        const before = text(from)
        const after = text(to)
        if (before === undefined || after === undefined) {
          return errorResponse(404, `A version of the key '${params.key}' does not exist.`, 'KeyValueNotFound')
        }
        // like the API: each secret version of the diff is a reveal, recorded once both sides resolved, in one request
        const correlationId = crypto.randomUUID()
        for (const version of new Set([from, to])) {
          const entry = stored?.Versions.find((candidate) => candidate.Version === version)
          if (stored && entry && isSecret(entry.Protection)) {
            recordAudit(db, projectKey, { Operation: 'Reveal', View: params.view, Key: stored.Key, Version: entry.Version, Protection: entry.Protection, Detail: `diff ${from}..${to}`, CorrelationId: correlationId })
          }
        }
        return HttpResponse.json(diffTexts(before, after))
      } catch (error) {
        return (error as Error).message === 'hyok'
          ? errorResponse(400, 'A client-encrypted value cannot be compared on the server.', 'NotSupportedForProtection')
          : errorResponse(403, `The key '${params.key}' is a secret; the caller has no rights to secrets.`, 'AccessDenied')
      }
    }),
  ),

  http.post(
    `${api}/:org/:project/:view/key-values/export`,
    withDb<ScopeParams>(async (db, { params, request }) => {
      const projectKey = projectKeyOf(params)
      const denied = requireRole(db, projectKey, 'Reader')
      if (denied) {
        return denied
      }
      const text = await request.text()
      const body = (text ? JSON.parse(text) : {}) as KeyValueExportRequest
      if (body.IncludeSecrets && !hasSecretRights(db, projectKey)) {
        return errorResponse(403, 'Secret values need secret rights.', 'AccessDenied')
      }
      const pairs = listPairs(db, scopeOf(params), 'Active', body.KeyPrefix ?? null, (body.Protections as string[] | undefined) ?? null)
      const document: KeyValueDocument = {
        Format: '2s.key-values.v1',
        Items: pairs.map((stored) => {
          const current = latest(stored)
          const secret = isSecret(current.Protection)
          return { Key: stored.Key, Protection: current.Protection, Value: secret && !body.IncludeSecrets ? undefined : current.Value, ContentType: current.ContentType }
        }),
      }
      if (body.IncludeSecrets) {
        recordAudit(db, projectKey, { Operation: 'Export', View: params.view, Detail: `${pairs.filter((stored) => isSecret(latest(stored).Protection)).length} secrets revealed` })
      }
      return HttpResponse.json(document)
    }),
  ),

  http.post(
    `${api}/:org/:project/:view/key-values/import`,
    withDb<ScopeParams>(async (db, { params, request }) => {
      const projectKey = projectKeyOf(params)
      const denied = requireRole(db, projectKey, 'Contributor')
      if (denied) {
        return denied
      }
      const url = new URL(request.url)
      const mode = (url.searchParams.get('mode') ?? 'Merge').toLowerCase() === 'replace' ? 'Replace' : 'Merge'
      const dryRun = url.searchParams.get('dryRun') === 'true'
      const prefix = (url.searchParams.get('keyPrefix') ?? '').toLowerCase()
      const document = (await request.json()) as KeyValueDocument
      const scope = scopeOf(params)
      const store = storeOf(db, scope)
      const items = document.Items ?? []
      const secretsNeeded = items.some((item) => isSecret(item.Protection) && item.Value != null) && !hasSecretRights(db, projectKey)
      if (secretsNeeded) {
        return errorResponse(403, 'The document carries secret values; the caller has no rights to secrets.', 'AccessDenied')
      }

      const seen = new Set<string>()
      const invalid: KeyValueImportItemResult[] = []
      const plan: (KeyValueImportItemResult & { value?: string; contentType?: string; recipients?: string[] })[] = []
      for (const item of items) {
        const key = item.Key ?? ''
        const id = normalizeKey(key)
        const level = (item.Protection ?? 'Public') as KeyValueProtection
        const existing = store.pairs[id]
        const current = existing ? latest(existing) : undefined
        const fail = (code: string, message: string) => invalid.push({ Key: key, Protection: level, Action: 'Create', ErrorCode: code, Message: message })

        if (seen.has(id)) {
          fail('ImportInvalid', 'The key appears twice.')
          continue
        }
        seen.add(id)
        if (item.Value == null) {
          if (!isSecret(level)) {
            fail('SecretValueMissing', 'A public item needs a value.')
          } else if (!existing || existing.Deleted) {
            fail('SecretValueMissing', 'The secret does not exist, so it needs a value.')
          } else {
            plan.push({ Key: existing.Key, Protection: current!.Protection, Action: 'Unchanged', Version: current!.Version })
          }
          continue
        }
        const checked = validateWrite(db, scope, key, level, item.Value, item.ContentType ?? undefined)
        if (checked.error) {
          const body = (await checked.error.json()) as { Message: string; ErrorCode?: string }
          fail(body.ErrorCode ?? 'ImportInvalid', body.Message)
          continue
        }
        const unchanged = current && !existing?.Deleted && current.Protection === level && current.Value === item.Value && (current.ContentType ?? undefined) === (item.ContentType ?? undefined)
        plan.push({
          Key: existing?.Key ?? key.trim(),
          Protection: level,
          Action: unchanged ? 'Unchanged' : existing && !existing.Deleted ? 'Update' : 'Create',
          Version: unchanged ? current?.Version : undefined,
          value: item.Value,
          contentType: item.ContentType ?? undefined,
          recipients: checked.recipients,
        })
      }
      if (mode === 'Replace') {
        for (const stored of Object.values(store.pairs)) {
          if (!stored.Deleted && stored.Key.toLowerCase().startsWith(prefix) && !seen.has(stored.Key.toLowerCase())) {
            if (isSecret(latest(stored).Protection) && !hasSecretRights(db, projectKey)) {
              invalid.push({ Key: stored.Key, Protection: latest(stored).Protection, Action: 'Delete', ErrorCode: 'AccessDenied', Message: 'A secret would be deleted; no secret rights.' })
            } else {
              plan.push({ Key: stored.Key, Protection: latest(stored).Protection, Action: 'Delete', Version: latest(stored).Version })
            }
          }
        }
      }
      if (invalid.length) {
        return HttpResponse.json({ Message: `${invalid.length} item(s) of the document are invalid; nothing was written.`, ErrorCode: 'ImportInvalid', Details: invalid }, { status: 400 })
      }
      const results: KeyValueImportItemResult[] = plan.map(({ value, contentType, recipients, ...result }) => {
        if (dryRun || result.Action === 'Unchanged') {
          return result
        }
        if (result.Action === 'Delete') {
          const stored = store.pairs[normalizeKey(result.Key ?? '')]!
          softDelete(stored, mockActor(db))
          return result
        }
        const stored = writeVersion(store, result.Key ?? '', { Protection: (result.Protection ?? 'Public') as KeyValueProtection, Value: value ?? '', ContentType: contentType, Recipients: recipients }, mockActor(db))
        return { ...result, Version: latest(stored).Version }
      })
      if (!dryRun) {
        recordAudit(db, projectKey, { Operation: 'Import', View: params.view, Detail: `${mode}: ${results.filter((item) => item.Action !== 'Unchanged').length} changes` })
      }
      return HttpResponse.json({ IsDryRun: dryRun, IsComplete: true, Items: results })
    }),
  ),
]

export const keyValueHandlers: HttpHandler[] = [...projectHandlers, ...viewHandlers]

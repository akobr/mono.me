import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import { setupServer } from 'msw/node'
import {
  deleteKeyValue,
  getKeyValue,
  getKeyValues,
  getKeyValueVersionDiffCustom,
  getKeyValueVersions,
  importKeyValues,
  protectKeyValues,
  purgeKeyValues,
  restoreKeyValues,
  setKeyValue,
  setKeyValuePolicy,
  verifyByok,
  type DiffResult,
} from '@42for.net/2splatform.sdk'
import { mockHandlers } from '@/mock/all-handlers'
import { loadDb, saveDb } from '@/mock/db'

// The stateful mock of the key/value API, through the SDK: the same handlers the browser and the e2e suite use.
const server = setupServer(...mockHandlers)
beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

const [org, project, view] = ['acme', 'billing', 'default']

async function expectError(action: () => Promise<unknown>, status: number, errorCode?: string) {
  try {
    await action()
  } catch (error) {
    expect(error).toMatchObject(errorCode ? { status, errorCode } : { status })
    return
  }
  throw new Error(`Expected a ${status} error.`)
}

describe('key/value mock', () => {
  it('lists the seeded pairs with masked secrets, filters and the deleted state', async () => {
    const active = await getKeyValues(org, project, view, { includeValues: true })
    const keys = active.Items?.map((item) => item.Key)

    expect(keys).toContain('db.password')
    expect(keys).not.toContain('legacy.endpoint')
    expect(active.Items?.find((item) => item.Key === 'db.password')?.Value).toBeUndefined()
    expect(active.Items?.find((item) => item.Key === 'db.host')?.Value).toBe('sql.internal.acme.example')
    expect(active.Items?.find((item) => item.Key === 'vault.payment_key')?.IsReference).toBe(true)

    const hsm = await getKeyValues(org, project, view, { protection: 'Hsm' })
    expect(hsm.Items?.map((item) => item.Key)).toEqual(['api.token'])

    const deleted = await getKeyValues(org, project, view, { state: 'Deleted' })
    expect(deleted.Items?.map((item) => item.Key)).toEqual(['legacy.endpoint'])
    expect(deleted.Items?.[0]?.ExpiresAt).toBeTruthy()
  })

  it('writes versions with the optimistic check, keeps the level on an update, and reveals on request', async () => {
    const created = await setKeyValue(org, project, view, 'new.key', { Protection: 'Standard', Value: 'one', ExpectedVersion: 0 })
    expect(created.Version).toBe(1)
    expect(created.Value).toBeUndefined()

    const updated = await setKeyValue(org, project, view, 'new.key', { Value: 'two', ExpectedVersion: 1 })
    expect(updated.Version).toBe(2)
    expect(updated.Protection).toBe('Standard')

    await expectError(() => setKeyValue(org, project, view, 'new.key', { Value: 'three', ExpectedVersion: 1 }), 412, 'KeyValueVersionMismatch')
    await expectError(() => setKeyValue(org, project, view, 'brand.new', { Value: 'x' }), 400, 'ProtectionRequired')
    await expectError(() => setKeyValue(org, project, view, 'bad', { Protection: 'Byok', Value: 'x' }), 409, 'ProtectionNotAllowed')
    await expectError(() => setKeyValue(org, project, view, 'ref', { Protection: 'Public', Value: 'nope', ContentType: 'application/vnd.2s.secret-reference' }), 400, 'InvalidSecretReference')

    const hidden = await getKeyValue(org, project, view, 'new.key')
    expect(hidden.Value).toBeUndefined()
    const revealed = await getKeyValue(org, project, view, 'new.key', { reveal: true })
    expect(revealed.Value).toBe('two')

    const versions = await getKeyValueVersions(org, project, view, 'new.key')
    expect(versions.map((entry) => [entry.Version, entry.IsCurrent])).toEqual([[1, false], [2, true]])
    const diff = (await getKeyValueVersionDiffCustom(org, project, view, 'new.key', 2, 1)) as DiffResult
    expect(diff.Stats?.Additions).toBe(1)
  })

  it('deletes, restores, protects and purges with outcomes per key', async () => {
    await deleteKeyValue(org, project, view, 'db.port')
    await expectError(() => getKeyValue(org, project, view, 'db.port'), 404, 'KeyValueNotFound')
    expect((await getKeyValue(org, project, view, 'db.port', { includeDeleted: true })).IsDeleted).toBe(true)

    const restored = await restoreKeyValues(org, project, view, { Keys: ['db.port', 'missing'] })
    expect(restored.Items?.map((item) => [item.Key, item.Outcome])).toEqual([['db.port', 'Done'], ['missing', 'NotFound']])

    const protectedResult = await protectKeyValues(org, project, view, { Keys: ['db.password', 'db.host'], Protection: 'Hsm' })
    expect(protectedResult.Items?.map((item) => item.Outcome)).toEqual(['Done', 'NotAllowed'])
    expect((await getKeyValue(org, project, view, 'db.password')).Protection).toBe('Hsm')

    const purged = await purgeKeyValues(org, project, view, { Keys: ['legacy.endpoint', 'db.host'] })
    expect(purged.Items?.map((item) => item.Outcome)).toEqual(['Done', 'NotFound'])
    expect((await getKeyValues(org, project, view, { state: 'Deleted' })).Items).toEqual([])
  })

  it('plans an import first, then applies it, and Replace deletes the other keys under the prefix', async () => {
    const document = {
      Format: '2s.key-values.v1',
      Items: [
        { Key: 'db.host', Protection: 'Public' as const, Value: 'sql.internal.acme.example' },
        { Key: 'db.user', Protection: 'Public' as const, Value: 'billing' },
        { Key: 'db.password', Protection: 'Standard' as const },
      ],
    }
    const plan = await importKeyValues(org, project, view, document, { mode: 'Replace', dryRun: true, keyPrefix: 'db.' })
    expect(plan.IsDryRun).toBe(true)
    expect(Object.fromEntries((plan.Items ?? []).map((item) => [item.Key, item.Action]))).toEqual({ 'db.host': 'Unchanged', 'db.user': 'Create', 'db.password': 'Unchanged', 'db.port': 'Delete' })
    expect((await getKeyValues(org, project, view)).Items?.map((item) => item.Key)).not.toContain('db.user')

    const result = await importKeyValues(org, project, view, document, { mode: 'Replace', keyPrefix: 'db.' })
    expect(result.IsComplete).toBe(true)
    expect((await getKeyValues(org, project, view)).Items?.map((item) => item.Key)).toContain('db.user')
    expect((await getKeyValues(org, project, view, { state: 'Deleted' })).Items?.map((item) => item.Key)).toContain('db.port')

    await expectError(() => importKeyValues(org, project, view, { Items: [{ Key: '1bad', Protection: 'Public', Value: 'x' }] }), 400, 'ImportInvalid')
  })

  it('enforces the policy: in-use levels, the customer key and the version', async () => {
    await expectError(() => setKeyValuePolicy(org, project, { AllowedProtections: ['Public', 'Standard'] }), 409, 'ProtectionInUse')
    await expectError(() => setKeyValuePolicy(org, project, { AllowedProtections: ['Public', 'Standard', 'Hsm', 'Hyok', 'Byok'] }), 409, 'ByokNotConfigured')

    const withKey = await setKeyValuePolicy(org, project, { Byok: { KeyUri: 'https://customer.vault.azure.net/keys/kek', TenantId: '11111111-2222-3333-4444-555555555555' } })
    expect(withKey.Byok?.Health).toBe('Unknown')
    const verified = await verifyByok(org, project)
    expect(verified.Byok?.Health).toBe('Healthy')
    const allowed = await setKeyValuePolicy(org, project, { AllowedProtections: ['Public', 'Standard', 'Hsm', 'Hyok', 'Byok'], ExpectedVersion: verified.Version })
    expect(allowed.AllowedProtections).toContain('Byok')

    await expectError(() => setKeyValuePolicy(org, project, { RequireMtlsForSecretDelivery: false, ExpectedVersion: 1 }), 409, 'PolicyVersionMismatch')
  })

  it('refuses secrets to a reader and permanent deletes to a contributor', async () => {
    const db = loadDb()
    db.account!.AccessMap = { ...db.account!.AccessMap, 'acme.billing': 'Contributor' }
    saveDb(db)

    await expectError(() => getKeyValue(org, project, view, 'db.password', { reveal: true }), 403, 'AccessDenied')
    await expectError(() => setKeyValue(org, project, view, 'other.secret', { Protection: 'Standard', Value: 'x' }), 403, 'AccessDenied')
    await expectError(() => deleteKeyValue(org, project, view, 'db.host', { permanent: true }), 403)
    expect((await setKeyValue(org, project, view, 'public.ok', { Protection: 'Public', Value: 'x' })).Version).toBe(1)
  })
})

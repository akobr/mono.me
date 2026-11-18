import { afterAll, afterEach, beforeAll, describe, expect, it } from 'vitest'
import { setupServer } from 'msw/node'
import {
  acceptInvitation,
  addRecipient,
  createAccessPoint,
  createMachineAccess,
  deleteFederatedIssuer,
  deleteMachineAccess,
  getAccessAudit,
  getKeyValuePolicy,
  issueSharedCertificate,
  removeMember,
  resetMachineAccess,
  revokeSharedCertificate,
  setFederatedIssuer,
  setKeyValuePolicy,
  setMachineAuthentication,
  setMachineFederationRules,
  setMemberRole,
  updateAccessPoint,
  verifyByok,
  type AccessAuditEntry,
  type GetAccessAuditParams,
} from '@42for.net/2splatform.sdk'
import { accessAuditOf, accessAuditPageSize, recordAccessAudit } from '@/mock/access-audit'
import { mockHandlers } from '@/mock/all-handlers'
import { loadDb, resetDb, saveDb } from '@/mock/db'

// The access audit of the mock API (424-263), through the SDK: the seeded trail of acme, the filters and pages of the
// GET, and a record from every mocked access write, with the values the API stores and never a secret.
const server = setupServer(...mockHandlers)
beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

const ada = { Kind: 'Account', Id: 'user_01MOCKADA', Name: 'Ada Lovelace' }

async function expectError(action: () => Promise<unknown>, status: number, errorCode?: string) {
  try {
    await action()
  } catch (error) {
    expect(error).toMatchObject(errorCode ? { status, errorCode } : { status })
    return
  }
  throw new Error(`Expected a ${status} error.`)
}

/** Every record of the key that matches, newest first, over all pages. */
async function trail(key = 'acme', params: GetAccessAuditParams = {}): Promise<AccessAuditEntry[]> {
  const records: AccessAuditEntry[] = []
  let continuationToken: string | undefined
  do {
    const page = await getAccessAudit(key, { ...params, continuationToken })
    records.push(...(page.Items ?? []))
    continuationToken = page.ContinuationToken ?? undefined
  } while (continuationToken)
  return records
}

/** The newest record of the organization acme. */
const newest = async () => (await getAccessAudit('acme')).Items?.[0]
const count = async () => (await trail()).length

describe('access audit mock: the trail', () => {
  it('starts acme with its seeded history, newest first, across the organization and its projects', async () => {
    const records = await trail()

    expect(records.length).toBeGreaterThan(15)
    expect(records.map((entry) => entry.Time)).toEqual(
      records
        .map((entry) => entry.Time)
        .toSorted()
        .toReversed(),
    )
    expect(new Set(records.map((entry) => entry.AccessPointKey))).toEqual(
      new Set(['acme', 'acme.billing', 'acme.shipping']),
    )
    expect(records[0]).toMatchObject({ Action: 'MachineFederationRulesChanged', AccessPointKey: 'acme.billing' })
    // a person acting through MCP, the System of DbCreator, and a removed member who keeps a name
    expect(records.find((entry) => entry.Action === 'MemberRoleChanged')?.Actor).toMatchObject({ Via: 'mcp' })
    expect(records.some((entry) => entry.Actor?.Kind === 'System')).toBe(true)
    expect(records.find((entry) => entry.Action === 'MemberRemoved')?.Target).toEqual({
      Kind: 'Account',
      Id: 'user_01MOCKCHARLES',
      Name: 'Charles Babbage',
    })
    // the change of the key/value policy shares its request with the key/value audit's PolicyChange
    expect(
      records.filter((entry) => entry.Action === 'KeyValuePolicyChanged').map((entry) => entry.CorrelationId),
    ).toContain('00000000-0000-4000-8000-000000000020')
  })

  it('filters by project, action, target and time, as the API does', async () => {
    const all = await trail()

    const billing = await trail('acme', { project: 'billing' })
    expect(billing.length).toBeGreaterThan(0)
    expect(billing.every((entry) => entry.AccessPointKey === 'acme.billing')).toBe(true)

    const roles = await trail('acme', { action: 'memberrolechanged' })
    expect(roles.map((entry) => entry.Action)).toEqual(['MemberRoleChanged'])

    const machine = await trail('acme', { target: 'mach-invoicing-worker' })
    expect(machine.map((entry) => entry.Action)).toEqual(['MachineReset', 'MachineCreated'])

    const day = 86_400_000
    const recent = await trail('acme', { from: new Date(Date.now() - 10 * day).toISOString() })
    expect(recent.map((entry) => entry.Action)).toEqual(['MachineFederationRulesChanged', 'MemberRoleChanged'])
    const old = await trail('acme', { to: new Date(Date.now() - 400 * day).toISOString() })
    expect(old.length).toBe(
      all.filter((entry) => (entry.Time ?? '') <= new Date(Date.now() - 400 * day).toISOString()).length,
    )
    expect(old.at(0)?.Action).toBe('MemberJoined')
  })

  it('gives a project key its own records only, and refuses another project in the query', async () => {
    const records = await trail('acme.billing')

    expect(records.length).toBe((await trail('acme', { project: 'billing' })).length)
    await expectError(() => getAccessAudit('acme.billing', { project: 'shipping' }), 400, 'InvalidQuery')
  })

  it('refuses the roles below Administrator of the key itself, and bad filters', async () => {
    // Ada contributes to acme/shipping, reads northwind/portal and has no role on northwind
    await expectError(() => getAccessAudit('acme.shipping'), 403, 'AccessDenied')
    await expectError(() => getAccessAudit('northwind'), 403, 'AccessDenied')
    await expectError(() => getAccessAudit('acme', { action: 'Everything' }), 400, 'InvalidQuery')
    await expectError(() => getAccessAudit('acme', { from: 'yesterday' }), 400, 'InvalidQuery')
    await expectError(() => getAccessAudit('acme', { continuationToken: 'not-a-token' }), 400, 'InvalidQuery')
  })

  it('pages 100 records at a time, after the last record of the page before', async () => {
    const db = loadDb()
    for (let index = 0; index < 120; index++) {
      recordAccessAudit(db, 'acme.billing', { Action: 'MachineReset', NewValue: 'ApiKey', Detail: `reset ${index}` })
    }
    saveDb(db)
    const total = accessAuditOf(loadDb(), 'acme').length

    const first = await getAccessAudit('acme')
    expect(first.Items).toHaveLength(accessAuditPageSize)
    expect(first.Count).toBe(accessAuditPageSize)
    expect(first.ContinuationToken).toBeTruthy()

    // a record written between the pages comes on a new first page only, and shifts nothing of the next one
    const between = loadDb()
    recordAccessAudit(between, 'acme.billing', { Action: 'MachineReset', NewValue: 'ApiKey', Detail: 'between' })
    saveDb(between)

    const second = await getAccessAudit('acme', { continuationToken: first.ContinuationToken })
    expect(second.Items).toHaveLength(total - accessAuditPageSize)
    expect(second.ContinuationToken).toBeUndefined()
    expect(second.Items!.map((entry) => entry.Detail)).not.toContain('between')
    expect(new Set([...first.Items!, ...second.Items!].map((entry) => entry.Id)).size).toBe(total)
    expect((await getAccessAudit('acme')).Items![0]!.Detail).toBe('between')
  })

  it('starts the showcase with the history of fabrikam, and an organization without one empty', async () => {
    resetDb('showcase')

    const records = await trail('fabrikam')
    expect(records.length).toBeGreaterThan(5)
    expect(records.every((entry) => entry.AccessPointKey?.startsWith('fabrikam'))).toBe(true)
    expect(await trail('ledgerline')).toEqual([])
  })
})

describe('access audit mock: the writes', () => {
  it('records a role change and a removal with the actor, the target and both roles; the same role again records nothing', async () => {
    await setMemberRole('acme.billing', 'user_01MOCKGRACE', { Role: 'Reader' })

    expect(await newest()).toMatchObject({
      Action: 'MemberRoleChanged',
      AccessPointKey: 'acme.billing',
      Actor: ada,
      Target: { Kind: 'Account', Id: 'user_01MOCKGRACE', Name: 'Grace Hopper' },
      OldValue: 'Contributor',
      NewValue: 'Reader',
    })
    expect((await newest())?.CorrelationId).toMatch(/^[0-9a-f-]{36}$/)

    const before = await count()
    await setMemberRole('acme.billing', 'user_01MOCKGRACE', { Role: 'Reader' })
    expect(await count()).toBe(before)

    await removeMember('acme.billing', 'user_01MOCKALAN')
    expect(await newest()).toMatchObject({
      Action: 'MemberRemoved',
      OldValue: 'Reader',
      Target: { Name: 'Alan Turing' },
    })
    expect((await newest())?.NewValue).toBeUndefined()
  })

  it('records the join of an accepted invitation, with the invitation and its sender', async () => {
    const db = loadDb()
    db.invitations = [
      ...(db.invitations ?? []),
      {
        Id: 'inv_01TESTSHIPPING',
        AccessPointKey: 'acme.shipping',
        Email: db.user.email,
        Role: 'Administrator',
        Status: 'Pending',
        InvitedById: 'user_01MOCKGRACE',
        InvitedByName: 'Grace Hopper',
      },
    ]
    saveDb(db)

    await acceptInvitation('inv_01TESTSHIPPING')

    expect(await newest()).toMatchObject({
      Action: 'MemberJoined',
      AccessPointKey: 'acme.shipping',
      Actor: ada,
      Target: { Id: 'user_01MOCKADA' },
      OldValue: 'Contributor',
      NewValue: 'Administrator',
      Detail: 'Invitation inv_01TESTSHIPPING sent by Grace Hopper (user_01MOCKGRACE).',
    })
  })

  it('records the life of a machine without its secrets', async () => {
    const created = await createMachineAccess('acme', 'billing', {
      Scopes: ['AnnotationRead'],
      CredentialKind: 'ApiKey',
    })
    const createdRecord = await newest()
    expect(createdRecord).toMatchObject({
      Action: 'MachineCreated',
      Target: { Kind: 'Machine', Id: created.Id },
      NewValue: JSON.stringify({ CredentialKind: 'ApiKey', Scopes: ['AnnotationRead'] }),
    })

    const reset = await resetMachineAccess('acme', 'billing', created.Id!)
    const resetRecord = await newest()
    expect(resetRecord).toMatchObject({
      Action: 'MachineReset',
      NewValue: 'ApiKey',
      Detail: 'The API key was replaced.',
    })

    await deleteMachineAccess('acme', 'billing', created.Id!)
    expect(await newest()).toMatchObject({
      Action: 'MachineDeleted',
      Target: { Id: created.Id },
      OldValue: createdRecord?.NewValue,
    })

    const text = JSON.stringify(await trail())
    expect(created.AccessKey).toBeTruthy()
    expect(text).not.toContain(created.AccessKey)
    expect(text).not.toContain(reset.AccessKey)
  })

  it('records changed federation rules and machine authentication, and nothing when they stay', async () => {
    const rules = [
      {
        Issuer: 'github',
        Audience: 'http://api.test/api/v1/acme/billing/access/token',
        Subject: 'repo:acme@65/billing@74:ref:refs/heads/main',
      },
    ]
    await setMachineFederationRules('acme', 'billing', 'mach-release-pipeline', rules)
    const record = await newest()
    expect(record).toMatchObject({ Action: 'MachineFederationRulesChanged', NewValue: JSON.stringify(rules) })
    expect(JSON.parse(record!.OldValue!)).toHaveLength(1)

    let before = await count()
    await setMachineFederationRules('acme', 'billing', 'mach-release-pipeline', rules)
    expect(await count()).toBe(before)

    const policy = {
      EnabledCredentialKinds: ['ApiKey', 'Certificate'],
      CredentialKind: 'ApiKey',
      CertificateLifetimeDays: 30,
    } as const
    await setMachineAuthentication('acme.billing', {
      ...policy,
      EnabledCredentialKinds: [...policy.EnabledCredentialKinds],
    })
    expect(await newest()).toMatchObject({
      Action: 'MachineAuthenticationPolicyChanged',
      OldValue: JSON.stringify({
        EnabledCredentialKinds: ['ApiKey', 'Federated'],
        CredentialKind: 'ApiKey',
        CertificateLifetimeDays: 90,
      }),
      NewValue: JSON.stringify({
        EnabledCredentialKinds: ['ApiKey', 'Certificate'],
        CredentialKind: 'ApiKey',
        CertificateLifetimeDays: 30,
      }),
    })
    before = await count()
    await setMachineAuthentication('acme.billing', {
      ...policy,
      EnabledCredentialKinds: [...policy.EnabledCredentialKinds],
    })
    expect(await count()).toBe(before)
  })

  it('records nothing for a refused change', async () => {
    // Ada only contributes to acme/shipping
    const before = await count()
    await expectError(() => setKeyValuePolicy('acme', 'shipping', { RequireMtlsForSecretDelivery: false }), 403)
    await expectError(() => setMemberRole('acme.shipping', 'user_01MOCKGRACE', { Role: 'Reader' }), 403)
    expect(await count()).toBe(before)
  })

  it('stores nothing of a key/value policy update that a later check refuses', async () => {
    // the policy of the mock is seeded on its first read
    await getKeyValuePolicy('acme', 'billing')
    const before = await count()
    const stored = JSON.stringify(loadDb().keyValuePolicies?.['acme.billing'] ?? null)

    // the levels pass (the same, in another order), the default level does not: the whole update is refused
    await expectError(
      () =>
        setKeyValuePolicy('acme', 'billing', {
          AllowedProtections: ['Hyok', 'Hsm', 'Standard', 'Public'],
          DefaultSecretProtection: 'Public',
        }),
      400,
    )

    expect(await count()).toBe(before)
    expect(JSON.stringify(loadDb().keyValuePolicies?.['acme.billing'] ?? null)).toBe(stored)
  })

  it('records a change of the key/value policy by its access view, a recipient without its key, and no verification', async () => {
    await setKeyValuePolicy('acme', 'billing', { RequireMtlsForSecretDelivery: false })
    const record = await newest()
    expect(record).toMatchObject({ Action: 'KeyValuePolicyChanged', AccessPointKey: 'acme.billing' })
    expect(JSON.parse(record!.OldValue!)).toMatchObject({ RequireMtlsForSecretDelivery: true })
    expect(JSON.parse(record!.NewValue!)).toMatchObject({
      RequireMtlsForSecretDelivery: false,
      HsmBackend: 'KeyVaultPremium',
    })

    let before = await count()
    await setKeyValuePolicy('acme', 'billing', { RequireMtlsForSecretDelivery: false })
    expect(await count()).toBe(before)

    const jwk = JSON.stringify({ kty: 'RSA', n: 'B'.repeat(520), e: 'AQAB' })
    await addRecipient('acme', 'billing', { Id: 'reporting', Name: 'Reporting', PublicKey: jwk })
    const added = await newest()
    expect(JSON.parse(added!.NewValue!).Recipients).toContainEqual(
      expect.objectContaining({
        Id: 'reporting',
        Name: 'Reporting',
        IsEnabled: true,
        KeyThumbprint: expect.stringMatching(/^[0-9a-f]{16}$/),
      }),
    )
    expect(added!.NewValue).not.toContain('B'.repeat(64))

    await setKeyValuePolicy('acme', 'billing', {
      Byok: {
        KeyUri: 'https://acme-keys.vault.azure.net/keys/storyteller',
        TenantId: '11111111-2222-3333-4444-555555555555',
      },
    })
    expect(JSON.parse((await newest())!.NewValue!)).toMatchObject({
      ByokKeyUri: 'https://acme-keys.vault.azure.net/keys/storyteller',
    })
    before = await count()
    await verifyByok('acme', 'billing')
    expect(await count()).toBe(before)
  })

  it('records trusted issuers created, changed and deleted, without their keys', async () => {
    const jwks = JSON.stringify({ keys: [{ kty: 'RSA', kid: 'one', n: 'modulusofthekey', e: 'AQAB' }] })
    const gitlab = { Issuer: 'https://gitlab.example.com', KeySource: 'Jwks', Jwks: jwks } as const
    await setFederatedIssuer('acme', 'billing', 'gitlab', { ...gitlab })
    expect(await newest()).toMatchObject({
      Action: 'FederatedIssuerCreated',
      Target: { Kind: 'Issuer', Id: 'gitlab' },
    })
    expect(JSON.parse((await newest())!.NewValue!)).toEqual({
      Issuer: 'https://gitlab.example.com',
      KeySource: 'Jwks',
      JwksKeys: 1,
      JwksThumbprint: expect.stringMatching(/^[0-9a-f]{16}$/),
    })

    const before = await count()
    await setFederatedIssuer('acme', 'billing', 'gitlab', { ...gitlab })
    // the same keys with their members in another order are the same key set
    const reordered = JSON.stringify({ keys: [{ e: 'AQAB', n: 'modulusofthekey', kid: 'one', kty: 'RSA' }] })
    await setFederatedIssuer('acme', 'billing', 'gitlab', { ...gitlab, Jwks: reordered })
    expect(await count()).toBe(before)

    // another key in its place, the same number of keys: a change
    const swapped = JSON.stringify({ keys: [{ kty: 'RSA', kid: 'two', n: 'anotherkeymodulus', e: 'AQAB' }] })
    await setFederatedIssuer('acme', 'billing', 'gitlab', { ...gitlab, Jwks: swapped })
    expect(await newest()).toMatchObject({ Action: 'FederatedIssuerUpdated' })
    await setFederatedIssuer('acme', 'billing', 'gitlab', { ...gitlab })

    await setFederatedIssuer('acme', 'billing', 'gitlab', { ...gitlab, Description: 'GitLab CI' })
    expect(await newest()).toMatchObject({ Action: 'FederatedIssuerUpdated' })

    await deleteFederatedIssuer('acme', 'billing', 'gitlab')
    expect(await newest()).toMatchObject({ Action: 'FederatedIssuerDeleted', Target: { Id: 'gitlab' } })
    expect(JSON.stringify(await trail())).not.toContain('modulusofthekey')
    expect(JSON.stringify(await trail())).not.toContain('anotherkeymodulus')
  })

  it('records shared certificates issued and revoked once, without the certificate', async () => {
    await setMachineAuthentication('acme.billing', {
      EnabledCredentialKinds: ['ApiKey', 'CertificateAndApiKey'],
      CredentialKind: 'ApiKey',
    })
    const issued = await issueSharedCertificate('acme', 'billing', { Label: 'nightly', LifetimeDays: 30 })
    expect(await newest()).toMatchObject({
      Action: 'SharedCertificateIssued',
      Target: { Kind: 'Certificate', Id: issued.Thumbprint, Name: 'nightly' },
    })
    expect(JSON.parse((await newest())!.NewValue!)).toEqual({
      Label: 'nightly',
      NotBefore: issued.NotBefore,
      NotAfter: issued.NotAfter,
    })

    await revokeSharedCertificate('acme', 'billing', issued.Thumbprint!)
    expect(await newest()).toMatchObject({ Action: 'SharedCertificateRevoked' })
    expect(JSON.parse((await newest())!.NewValue!)).toMatchObject({ IsRevoked: true })
    const before = await count()
    await revokeSharedCertificate('acme', 'billing', issued.Thumbprint!)
    expect(await count()).toBe(before)

    const text = JSON.stringify(await trail())
    expect(text).not.toContain(issued.Certificate)
    expect(text).not.toContain(issued.CertificatePassword)
  })

  it('records a new organization and project as two access points, and a changed description once', async () => {
    await createAccessPoint({ Organization: 'globex', Project: 'portal', ProjectDescription: 'The portal of Globex.' })

    const globex = await trail('globex')
    expect(globex.map((entry) => [entry.Action, entry.AccessPointKey])).toEqual([
      ['AccessPointCreated', 'globex.portal'],
      ['AccessPointCreated', 'globex'],
    ])
    expect(globex[0]).toMatchObject({
      Target: { Id: 'user_01MOCKADA' },
      NewValue: 'Owner',
      Detail: 'The portal of Globex.',
    })

    await updateAccessPoint('acme.billing', { Description: 'Billing.' })
    expect(await newest()).toMatchObject({ Action: 'AccessPointUpdated', NewValue: 'Billing.' })
    expect((await newest())?.OldValue).toContain("Billing of Acme's B2B customers.")
    const before = await count()
    await updateAccessPoint('acme.billing', { Description: ' Billing. ' })
    expect(await count()).toBe(before)
  })
})

import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import { setupServer } from 'msw/node'
import {
  addRecipient,
  createInvitation,
  createMachineAccess,
  deleteKeyValue,
  exportKeyValues,
  getAuthConfiguration,
  getKeyValue,
  getKeyValueVersion,
  getKeyValueVersionDiffCustom,
  getResolvedConfigurationConditional,
  importKeyValues,
  isStepUpRequired,
  issueSharedCertificate,
  protectKeyValues,
  purgeKeyValues,
  removeMember,
  resetMachineAccess,
  restoreKeyValues,
  setFederatedIssuer,
  setKeyValue,
  setMachineFederationRules,
  setMemberRole,
  stepUpMaxAge,
  updateRecipient,
} from '@42for.net/2splatform.sdk'
import { createMockAuth } from '@/mock/auth'
import { mockHandlers } from '@/mock/all-handlers'
import { loadDb, saveDb } from '@/mock/db'
import { putVersion } from '@/mock/documents'
import { isStaleSignIn, setStaleSignIn, stepUpMessage } from '@/mock/step-up'

// The stale sign-in of the mock API (424-264): off by default; under it the guarded handlers answer the API's step-up
// 401, restore and purge refuse the secrets per item, and the resolved read says why secrets stay unresolved.

const server = setupServer(...mockHandlers)
beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => {
  server.resetHandlers()
  setStaleSignIn(false)
})
afterAll(() => server.close())

const [org, project, view] = ['acme', 'billing', 'default']
const projectKey = `${org}.${project}`

/** The error of a call that must fail. */
async function failure(action: () => Promise<unknown>): Promise<unknown> {
  try {
    await action()
  } catch (error) {
    return error
  }
  throw new Error('The call did not fail.')
}

async function expectStepUp(action: () => Promise<unknown>) {
  const error = await failure(action)
  expect(isStepUpRequired(error)).toBe(true)
  expect(error).toMatchObject({ status: 401, message: stepUpMessage })
  expect(stepUpMaxAge(error)).toBe(600)
}

describe('the mock API with a stale sign-in', () => {
  it('reports the step-up in the discovery and keeps the sign-in recent by default', async () => {
    expect((await getAuthConfiguration()).StepUp).toEqual({ Enabled: true, MaxAge: 600 })
    expect(isStaleSignIn()).toBe(false)
    expect((await getKeyValue(org, project, view, 'db.password', { reveal: true })).Value).toBe('s3cret-billing-2')
  })

  it('answers the 401 with its challenge to a reveal of a secret, and reveals a public pair', async () => {
    setStaleSignIn(true)
    const response = await fetch(`http://api.test/api/v1/${org}/${project}/${view}/key-values/db.password?reveal=true`)

    expect(response.status).toBe(401)
    expect(response.headers.get('www-authenticate')).toBe(
      'Bearer error="insufficient_user_authentication", error_description="A recent sign-in is required", max_age="600"',
    )
    expect(await response.json()).toEqual({
      Message: stepUpMessage,
      ErrorCode: 'StepUpRequired',
      Details: { MaxAge: 600 },
    })

    await expectStepUp(() => getKeyValue(org, project, view, 'db.password', { reveal: true }))
    await expectStepUp(() => getKeyValueVersion(org, project, view, 'db.password', 1, { reveal: true }))
    await expectStepUp(() => getKeyValueVersionDiffCustom(org, project, view, 'db.password', 2, 1))
    // without reveal, and a public pair: as before
    expect((await getKeyValue(org, project, view, 'db.password')).Value).toBeUndefined()
    expect((await getKeyValue(org, project, view, 'db.host', { reveal: true })).Value).toBe('sql.internal.acme.example')
  })

  it('refuses the writes of secrets, and writes public pairs', async () => {
    setStaleSignIn(true)

    await expectStepUp(() => setKeyValue(org, project, view, 'db.password', { Value: 'new' }))
    await expectStepUp(() => setKeyValue(org, project, view, 'new.secret', { Protection: 'Standard', Value: 'x' }))
    // a secret made public is a write of the secret too
    await expectStepUp(() => setKeyValue(org, project, view, 'db.password', { Protection: 'Public', Value: 'x' }))
    await expectStepUp(() => deleteKeyValue(org, project, view, 'db.password'))
    await expectStepUp(() => protectKeyValues(org, project, view, { Keys: ['db.password'], Protection: 'Hsm' }))
    await expectStepUp(() => exportKeyValues(org, project, view, { IncludeSecrets: true }))
    await expectStepUp(() =>
      importKeyValues(
        org,
        project,
        view,
        { Format: '2s.key-values.v1', Items: [{ Key: 'api.secret', Protection: 'Standard', Value: 'x' }] },
        { dryRun: true },
      ),
    )
    // so is an import that makes an active secret public
    await expectStepUp(() =>
      importKeyValues(
        org,
        project,
        view,
        { Format: '2s.key-values.v1', Items: [{ Key: 'db.password', Protection: 'Public', Value: 'x' }] },
        { dryRun: true },
      ),
    )

    expect((await setKeyValue(org, project, view, 'new.public', { Protection: 'Public', Value: 'x' })).Version).toBe(1)
    expect((await exportKeyValues(org, project, view, {})).Items?.length).toBeGreaterThan(0)
    expect(
      (
        await importKeyValues(org, project, view, {
          Format: '2s.key-values.v1',
          Items: [{ Key: 'other.public', Protection: 'Public', Value: 'y' }],
        })
      ).Items?.[0],
    ).toMatchObject({ Action: 'Create' })
    await deleteKeyValue(org, project, view, 'db.host')
  })

  it('refuses the secrets of a restore and a purge per item, with the step-up code', async () => {
    await deleteKeyValue(org, project, view, 'db.password')
    await deleteKeyValue(org, project, view, 'db.host')
    setStaleSignIn(true)

    const restored = await restoreKeyValues(org, project, view, { Keys: ['db.password', 'db.host'] })
    expect(restored.Items).toEqual([
      { Key: 'db.password', Outcome: 'Forbidden', ErrorCode: 'StepUpRequired', Message: stepUpMessage },
      expect.objectContaining({ Key: 'db.host', Outcome: 'Done' }),
    ])

    const purged = await purgeKeyValues(org, project, view, { Keys: ['db.password'] })
    expect(purged.Items).toEqual([
      { Key: 'db.password', Outcome: 'Forbidden', ErrorCode: 'StepUpRequired', Message: stepUpMessage },
    ])
  })

  it('leaves the secrets of a resolved read unresolved and says why, on a 200', async () => {
    const db = loadDb()
    putVersion(db, { org, project, view }, 'configuration:exe.northwind.invoicing.prod', {
      password: '@(db.password, primaryVault)',
    })
    saveDb(db)

    const recent = await getResolvedConfigurationConditional(org, project, view, 'exe.northwind.invoicing.prod')
    expect(recent.secretsWithheld).toBeUndefined()

    setStaleSignIn(true)
    const stale = await getResolvedConfigurationConditional(org, project, view, 'exe.northwind.invoicing.prod')
    expect(stale).toMatchObject({ status: 200, secretsWithheld: 'step-up-required' })
    expect(stale.value?.Content).toMatchObject({ password: '@(db.password, primaryVault)' })
  })

  it('refuses role changes, removing others, invitations, machine credentials, rules, a replaced issuer and certificates', async () => {
    // shared certificates belong to CertificateAndApiKey
    const db = loadDb()
    const point = db.accessPoints[projectKey]!
    point.MachineAuthentication = {
      ...point.MachineAuthentication,
      EnabledCredentialKinds: ['ApiKey', 'Federated', 'CertificateAndApiKey'],
    }
    saveDb(db)
    setStaleSignIn(true)

    await expectStepUp(() => setMemberRole(projectKey, 'user_01MOCKGRACE', { Role: 'Reader' }))
    await expectStepUp(() => removeMember(projectKey, 'user_01MOCKGRACE'))
    await expectStepUp(() => createInvitation(projectKey, { Email: 'new@example.com', Role: 'Reader' }))
    await expectStepUp(() =>
      createMachineAccess(org, project, { Scopes: ['AnnotationRead'], CredentialKind: 'ApiKey' }),
    )
    await expectStepUp(() => resetMachineAccess(org, project, 'mach-invoicing-worker'))
    // the seeded rules, valid as they are
    const rules = loadDb().machines?.[projectKey]?.find(
      (machine) => machine.Id === 'mach-release-pipeline',
    )?.FederationRules
    await expectStepUp(() => setMachineFederationRules(org, project, 'mach-release-pipeline', rules))
    await expectStepUp(() =>
      setFederatedIssuer(org, project, 'github', {
        Issuer: 'https://token.actions.githubusercontent.com',
        KeySource: 'Discovery',
      }),
    )
    await expectStepUp(() => issueSharedCertificate(org, project, { Label: 'ci', LifetimeDays: 30 }))
    await expectStepUp(() =>
      addRecipient(org, project, {
        Id: 'ops',
        PublicKey: '-----BEGIN PUBLIC KEY-----\nAAAA\n-----END PUBLIC KEY-----',
      }),
    )

    // a new issuer replaces nothing
    expect(
      (
        await setFederatedIssuer(org, project, 'gitlab', {
          Issuer: 'https://gitlab.example.com',
          KeySource: 'Discovery',
        })
      ).Name,
    ).toBe('gitlab')
  })

  it('lets a member leave without a recent sign-in', async () => {
    const db = loadDb()
    db.accessPoints[projectKey]!.AccessMap = { ...db.accessPoints[projectKey]!.AccessMap, user_01MOCKGRACE: 'Owner' }
    saveDb(db)
    setStaleSignIn(true)

    await expect(removeMember(projectKey, 'user_01MOCKADA')).resolves.toBeUndefined()
  })

  it('refuses a recipient change with a stale sign-in', async () => {
    setStaleSignIn(false)
    await addRecipient(org, project, {
      Id: 'ops',
      PublicKey: '-----BEGIN PUBLIC KEY-----\nAAAA\n-----END PUBLIC KEY-----',
    })
    setStaleSignIn(true)

    await expectStepUp(() => updateRecipient(org, project, 'ops', { Name: 'Operations' }))
  })
})

describe('the mock sign-in', () => {
  it('has an old sign-in while stale, and the step-up makes it recent and comes back with its marker', async () => {
    const assign = vi.spyOn(location, 'assign').mockImplementation(() => {})
    setStaleSignIn(true)
    const auth = createMockAuth()

    expect((await auth.getAuthTime())!).toBeLessThan(Date.now() - 30 * 60_000)
    expect(auth.getStepUpReturn()).toBeUndefined()
    // the same time with every read until the state changes, however the clock moves
    const stale = await auth.getAuthTime()
    vi.spyOn(Date, 'now').mockReturnValue(Date.now() + 5_000)
    expect(await auth.getAuthTime()).toBe(stale)
    vi.mocked(Date.now).mockRestore()

    // a plain sign-in reuses the session: still stale
    await auth.signIn({ returnTo: '/acme' })
    expect(isStaleSignIn()).toBe(true)

    const before = await auth.getAuthTime()
    await auth.signIn({ returnTo: '/acme/billing/default/key-values', maxAge: 0 })
    expect(isStaleSignIn()).toBe(false)
    expect(assign).toHaveBeenLastCalledWith('/callback')

    // the page after the redirect
    const returned = createMockAuth()
    expect(returned.consumeReturnTo()).toBe('/acme/billing/default/key-values')
    expect(returned.getStepUpReturn()).toMatchObject({ previousAuthTime: before })
    expect((await returned.getAuthTime())!).toBeGreaterThan(before!)
    // read once per page
    expect(createMockAuth().getStepUpReturn()).toBeUndefined()
  })
})

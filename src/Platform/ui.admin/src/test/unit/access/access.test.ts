import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import { defineComponent, h, ref, type Ref } from 'vue'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import {
  createMachineAccess,
  getMachineAuthentication,
  issueSharedCertificate,
  resetMachineAccess,
  setMachineAuthentication,
  type AccessPointMember,
  type MachineAccessCreate,
  type MachineAuthenticationPolicy,
} from '@42for.net/2splatform.sdk'
import { memberRules } from '@/utils/roles'
import { allowsSharedCertificates, certificateDaysLeft, issuesCertificates, machineSecret } from '@/utils/machines'
import MachineCreateDialog from '@/components/access/MachineCreateDialog.vue'
import OneTimeSecretDialog from '@/components/access/OneTimeSecretDialog.vue'
import MembersTable from '@/components/access/MembersTable.vue'
import { mockHandlers } from '@/mock/all-handlers'
import { loadDb } from '@/mock/db'
import { mountInApp, mountScreen, settle } from '../../helpers'

const server = setupServer(...mockHandlers)
beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterEach(() => server.resetHandlers())
afterAll(() => server.close())

const member = (id: string, role: AccessPointMember['Role']): AccessPointMember => ({ AccountId: id, Role: role })

describe('member rules', () => {
  const members = [member('owner', 'Owner'), member('admin', 'Administrator'), member('reader', 'Reader')]

  it('protects the last Owner', () => {
    expect(memberRules(members[0]!, members, 'admin', 'Owner')).toMatchObject({
      canChange: false,
      canRemove: false,
      reason: 'The last Owner cannot be demoted or removed',
    })
    const twoOwners = [...members, member('owner2', 'Owner')]
    expect(memberRules(members[0]!, twoOwners, 'owner2', 'Owner').canChange).toBe(true)
  })

  it('lets only Owners grant or revoke Owner', () => {
    expect(memberRules(members[2]!, members, 'admin', 'Administrator').roles).not.toContain('Owner')
    expect(memberRules(members[2]!, members, 'owner', 'Owner').roles).toContain('Owner')
    const twoOwners = [...members, member('owner2', 'Owner')]
    expect(memberRules(twoOwners[3]!, twoOwners, 'admin', 'Administrator')).toMatchObject({
      canChange: false,
      reason: 'Only Owners can change an Owner',
    })
  })

  it('stops everyone from changing their own role, and everyone below Administrator', () => {
    expect(memberRules(members[1]!, members, 'admin', 'Administrator')).toMatchObject({
      canChange: false,
      canRemove: false,
      reason: 'You cannot change your own role',
    })
    // The API rejects a self change for Owners too (409 SelfRoleChange); an Owner who is not the last may leave.
    const twoOwners = [...members, member('owner2', 'Owner')]
    expect(memberRules(twoOwners[0]!, twoOwners, 'owner', 'Owner')).toMatchObject({
      canChange: false,
      canRemove: true,
      reason: 'You cannot change your own role',
    })
    expect(memberRules(members[2]!, members, 'x', 'Contributor')).toMatchObject({
      canChange: false,
      reason: 'Requires the Administrator role',
    })
  })
})

describe('machines', () => {
  it('computes the certificate expiry from the expiry, else the last renewal and the lifetime', () => {
    const now = Date.parse('2026-10-10T00:00:00Z')
    const machine = { CertificateThumbprint: 'AB', LastRenewalAt: '2026-08-01T00:00:00Z' }

    expect(certificateDaysLeft(machine, 90, now)).toBe(20)
    expect(certificateDaysLeft(machine, 30, now)).toBeLessThan(0)
    // The machine's own lifetime wins over the project default.
    expect(certificateDaysLeft({ ...machine, CertificateLifetimeDays: 30 }, 90, now)).toBeLessThan(0)
    expect(certificateDaysLeft({ ...machine, CertificateNotAfter: '2026-10-15T00:00:00Z' }, 30, now)).toBe(5)
    expect(certificateDaysLeft({ LastRenewalAt: '2026-08-01T00:00:00Z' }, 90, now)).toBeUndefined()
    expect(issuesCertificates('CertificateAndApiKey')).toBe(true)
    expect(issuesCertificates('Certificate')).toBe(true)
    expect(issuesCertificates('ClientCredentials')).toBe(false)
    expect(issuesCertificates(undefined)).toBe(false)
  })

  it('maps one-time credentials of a response', () => {
    expect(machineSecret({ Id: 'm1', AccessKey: 'st_x', Certificate: 'AA==' }, 'Created', 'https://api')).toMatchObject(
      {
        title: 'Created',
        id: 'm1',
        apiKey: 'st_x',
        certificate: 'AA==',
        fileName: 'm1.pfx',
        baseUrl: 'https://api',
      },
    )
    expect(
      machineSecret({ Id: 'client_1', AccessKey: 'sk', CredentialKind: 'ClientCredentials' }, 'Reset'),
    ).toMatchObject({
      id: 'client_1',
      apiKey: 'sk',
      credentialKind: 'ClientCredentials',
    })
    expect(
      machineSecret(
        { Id: 'm2', AccessKey: 'st_y', CredentialKind: 'CertificateAndApiKey', UsesSharedCertificate: true },
        'Created',
      ),
    ).toMatchObject({ apiKey: 'st_y', sharedCertificate: true, certificate: undefined })
  })

  it('allows shared certificates only with the CertificateAndApiKey kind', () => {
    expect(allowsSharedCertificates({ EnabledCredentialKinds: ['ApiKey', 'CertificateAndApiKey'] })).toBe(true)
    expect(allowsSharedCertificates({ EnabledCredentialKinds: ['ApiKey', 'Certificate'] })).toBe(false)
    expect(allowsSharedCertificates(undefined)).toBe(false)
  })
})

describe('machine authentication policy against the mock API', () => {
  it('enables several kinds, and each machine picks one of them', async () => {
    const policy = await setMachineAuthentication('acme.billing', {
      EnabledCredentialKinds: ['Certificate', 'ApiKey'],
      CertificateLifetimeDays: 30,
    })

    expect(policy).toEqual({
      EnabledCredentialKinds: ['ApiKey', 'Certificate'],
      CredentialKind: 'Certificate',
      CertificateLifetimeDays: 30,
    })
    expect(await getMachineAuthentication('acme.billing')).toEqual(policy)

    const certificate = await createMachineAccess('acme', 'billing', { Scope: 'DefaultRead' })
    expect(certificate).toMatchObject({ CredentialKind: 'Certificate', CertificateLifetimeDays: 30 })
    expect(certificate.CertificateNotAfter).toBeDefined()
    const apiKey = await createMachineAccess('acme', 'billing', { Scope: 'DefaultRead', CredentialKind: 'ApiKey' })
    expect(apiKey.CredentialKind).toBe('ApiKey')
    expect(apiKey.CertificateLifetimeDays).toBeUndefined()
    await expect(
      createMachineAccess('acme', 'billing', { Scope: 'DefaultRead', CredentialKind: 'ClientCredentials' }),
    ).rejects.toMatchObject({
      status: 400,
      message: 'The credential kind ClientCredentials is not enabled in the project acme.billing.',
    })
  })

  it('issues shared certificates and creates machines that use them only with CertificateAndApiKey', async () => {
    await setMachineAuthentication('acme.billing', { EnabledCredentialKinds: ['ApiKey'] })
    await expect(issueSharedCertificate('acme', 'billing', { Label: 'edge' })).rejects.toMatchObject({
      status: 400,
      message: 'The project acme.billing does not enable CertificateAndApiKey, which shared certificates belong to.',
    })
    await expect(
      createMachineAccess('acme', 'billing', { Scope: 'DefaultRead', UseSharedCertificate: true }),
    ).rejects.toMatchObject({ status: 400 })

    await setMachineAuthentication('acme.billing', { EnabledCredentialKinds: ['ApiKey', 'CertificateAndApiKey'] })
    expect((await issueSharedCertificate('acme', 'billing', { Label: 'edge' })).Label).toBe('edge')
    const machine = await createMachineAccess('acme', 'billing', {
      Scope: 'DefaultRead',
      CredentialKind: 'CertificateAndApiKey',
      UseSharedCertificate: true,
      CertificateLifetimeDays: 30,
    })
    expect(machine).toMatchObject({ CredentialKind: 'CertificateAndApiKey', UsesSharedCertificate: true })
    expect(machine.AccessKey).toBeDefined()
    expect(machine.Certificate).toBeUndefined()
    expect(machine.CertificateThumbprint).toBeUndefined()
    expect(machine.CertificateLifetimeDays).toBeUndefined()

    const reset = await resetMachineAccess('acme', 'billing', machine.Id!)
    expect(reset.Certificate).toBeUndefined()
    expect(reset.UsesSharedCertificate).toBe(true)
  })

  it('rejects a default kind that is not enabled, and a policy without kinds', async () => {
    await expect(
      setMachineAuthentication('acme.billing', { EnabledCredentialKinds: ['ApiKey'], CredentialKind: 'Certificate' }),
    ).rejects.toMatchObject({ status: 400 })
    await expect(setMachineAuthentication('acme.billing', { EnabledCredentialKinds: [] })).rejects.toMatchObject({
      status: 400,
    })
  })
})

describe('MachineCreateDialog', () => {
  /** Mounts the dialog with a policy that a test may replace, and opens it. */
  async function openDialog(policy: Ref<MachineAuthenticationPolicy | undefined>) {
    const open = ref(false)
    const Harness = defineComponent({
      setup: () => () =>
        h(MachineCreateDialog, {
          open: open.value,
          'onUpdate:open': (value: boolean) => (open.value = value),
          policy: policy.value,
        }),
    })
    const screen = await mountScreen(Harness, {})
    open.value = true
    await settle(6)
    return screen
  }

  async function create(policy: MachineAuthenticationPolicy, before?: () => Promise<void>) {
    const bodies: MachineAccessCreate[] = []
    server.use(
      http.post('*/api/v1/:org/:project/access/machines', async ({ request }) => {
        bodies.push((await request.json()) as MachineAccessCreate)
        return HttpResponse.json({ Id: 'm1', AccessKey: '' }, { status: 201 })
      }),
    )
    const screen = await openDialog(ref(policy))
    await before?.()
    const text = document.body.textContent ?? ''
    ;[...document.body.querySelectorAll('button')].find((button) => button.textContent?.trim() === 'Create')!.click()
    await settle(6)
    screen.host.unmount()
    return { body: bodies[0], text }
  }

  it('preselects the default kind and lifetime of the project', async () => {
    const { body, text } = await create({
      EnabledCredentialKinds: ['ApiKey', 'Certificate'],
      CredentialKind: 'Certificate',
      CertificateLifetimeDays: 30,
    })

    expect(text).toContain('Credential kind')
    expect(text).toContain('Certificate lifetime (days)')
    expect(body).toMatchObject({ CredentialKind: 'Certificate', CertificateLifetimeDays: 30 })
  })

  it('asks no lifetime for a kind without a certificate', async () => {
    const { body, text } = await create({
      EnabledCredentialKinds: ['ApiKey'],
      CredentialKind: 'ApiKey',
      CertificateLifetimeDays: 365,
    })

    expect(text).not.toContain('Certificate lifetime (days)')
    expect(text).not.toContain('Use a shared certificate of the project')
    expect(body).toMatchObject({ CredentialKind: 'ApiKey', CertificateLifetimeDays: null })
  })

  it('preselects the kind of a policy that arrives after the dialog opened, keeping a picked kind', async () => {
    const policy = ref<MachineAuthenticationPolicy>()
    const screen = await openDialog(policy)
    expect(document.body.textContent).not.toContain('Credential kind')

    policy.value = {
      EnabledCredentialKinds: ['ApiKey', 'Certificate'],
      CredentialKind: 'Certificate',
      CertificateLifetimeDays: 30,
    }
    await settle(4)
    expect(document.body.textContent).toContain('Credential kind')
    expect(document.body.textContent).toContain('Certificate lifetime (days)')

    // A later policy (a refetch) does not replace the kind that is already picked while it stays enabled...
    policy.value = { EnabledCredentialKinds: ['ApiKey', 'Certificate'], CredentialKind: 'ApiKey' }
    await settle(4)
    expect(document.body.textContent).toContain('Certificate lifetime (days)')

    // ...but a kind the policy no longer enables gives way to its default, which is what Create sends.
    const bodies: MachineAccessCreate[] = []
    server.use(
      http.post('*/api/v1/:org/:project/access/machines', async ({ request }) => {
        bodies.push((await request.json()) as MachineAccessCreate)
        return HttpResponse.json({ Id: 'm1', AccessKey: '' }, { status: 201 })
      }),
    )
    policy.value = { EnabledCredentialKinds: ['ApiKey'], CredentialKind: 'ApiKey' }
    await settle(4)
    expect(document.body.textContent).not.toContain('Certificate lifetime (days)')
    ;[...document.body.querySelectorAll('button')].find((button) => button.textContent?.trim() === 'Create')!.click()
    await settle(6)
    expect(bodies[0]).toMatchObject({ CredentialKind: 'ApiKey', CertificateLifetimeDays: null })
    screen.host.unmount()
  })

  it('offers a shared certificate for CertificateAndApiKey and then asks no lifetime', async () => {
    const policy: MachineAuthenticationPolicy = {
      EnabledCredentialKinds: ['CertificateAndApiKey'],
      CredentialKind: 'CertificateAndApiKey',
      CertificateLifetimeDays: 90,
    }

    const own = await create(policy)
    expect(own.text).toContain('Use a shared certificate of the project')
    expect(own.text).toContain('Certificate lifetime (days)')
    expect(own.body).toMatchObject({ CredentialKind: 'CertificateAndApiKey', CertificateLifetimeDays: 90 })
    expect(own.body?.UseSharedCertificate).toBeUndefined()

    const shared = await create(policy, async () => {
      ;(document.body.querySelector('button[role="checkbox"]') as HTMLButtonElement).click()
      await settle(4)
    })
    expect(shared.text).not.toContain('Certificate lifetime (days)')
    expect(shared.text).toContain('No certificate is issued for this machine')
    expect(shared.body).toMatchObject({
      CredentialKind: 'CertificateAndApiKey',
      CertificateLifetimeDays: null,
      UseSharedCertificate: true,
    })
  })
})

describe('OneTimeSecretDialog', () => {
  it('shows client credentials as a client ID and secret with a token request, never as an API key', async () => {
    const view = mountInApp(OneTimeSecretDialog, {
      secret: { title: 'New credentials', id: 'client_1', apiKey: 'sk_secret', credentialKind: 'ClientCredentials' },
      onClose: () => {},
    })
    await settle()

    const text = document.body.textContent ?? ''
    expect(text).toContain('Client ID')
    expect(text).toContain('Client secret')
    expect(text).not.toContain('API key')
    expect(text).toContain('-d client_id=client_1')
    expect(text).toContain('-d client_secret=sk_secret')
    expect(text).toContain('<token-endpoint>')
    expect(text).not.toContain('ApiKey')
    view.host.unmount()
  })

  it('tells that a machine with a shared certificate got no certificate of its own', async () => {
    const view = mountInApp(OneTimeSecretDialog, {
      secret: {
        title: 'Created',
        apiKey: 'st_secret',
        credentialKind: 'CertificateAndApiKey',
        sharedCertificate: true,
      },
      onClose: () => {},
    })
    await settle()

    const text = document.body.textContent ?? ''
    expect(text).toContain('Uses a shared certificate')
    expect(text).toContain('API key')
    expect(text).not.toContain('Download .pfx')
    view.host.unmount()
  })

  it('cannot be closed before the credentials are acknowledged, and downloads the .pfx', async () => {
    const createObjectURL = vi.fn((_blob: Blob) => 'blob:pfx')
    URL.createObjectURL = createObjectURL
    URL.revokeObjectURL = vi.fn()
    const closed = vi.fn()
    const view = mountInApp(OneTimeSecretDialog, {
      secret: { title: 'Created', apiKey: 'st_secret', certificate: btoa('pfx-bytes'), certificatePassword: 'pw' },
      onClose: closed,
    })
    await settle()

    const done = () => [...document.querySelectorAll('button')].find((button) => button.textContent?.trim() === 'Done')!
    expect(done().disabled).toBe(true)

    ;[...document.querySelectorAll('button')].find((button) => button.textContent?.includes('Download .pfx'))!.click()
    expect(createObjectURL).toHaveBeenCalledOnce()
    const blob = createObjectURL.mock.calls[0]![0]
    expect(blob.type).toBe('application/x-pkcs12')

    ;(document.querySelector('button[role="checkbox"]') as HTMLButtonElement).click()
    await settle()
    expect(done().disabled).toBe(false)
    done().click()
    expect(closed).toHaveBeenCalledOnce()
    view.host.unmount()
  })
})

describe('MembersTable against the mock API', () => {
  it('disables the last Owner and changes another member’s role', async () => {
    const view = await mountScreen(
      MembersTable,
      { accessPointKey: 'acme.billing' },
      { accessMap: { 'acme.billing': 'Owner', acme: 'Owner' } },
    )
    useSessionAccount('user_01MOCKADA')
    await settle(10)

    const selects = view.host.findAll('button[role="combobox"]')
    expect(selects).toHaveLength(3)
    expect(selects[0]!.attributes('disabled')).toBeDefined()

    expect(loadDb().accessPoints['acme.billing']?.AccessMap?.user_01MOCKALAN).toBe('Reader')
    view.host.unmount()
  })
})

function useSessionAccount(id: string) {
  // The mock user is the caller; the session account id makes the "you" rules apply to the right row.
  server.use(
    http.get('*/api/v1/access/account', () => HttpResponse.json({ Id: id, AccessMap: { 'acme.billing': 'Owner' } })),
  )
}

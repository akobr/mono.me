import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import type { AccessPointMember } from '@42for.net/2splatform.sdk'
import { memberRules } from '@/utils/roles'
import { certificateDaysLeft, issuesCertificates, machineSecret } from '@/utils/machines'
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

  it('stops Administrators from changing their own role, and everyone below Administrator', () => {
    expect(memberRules(members[1]!, members, 'admin', 'Administrator')).toMatchObject({
      canChange: false,
      reason: 'Administrators cannot change their own role',
    })
    expect(memberRules(members[2]!, members, 'x', 'Contributor')).toMatchObject({
      canChange: false,
      reason: 'Requires the Administrator role',
    })
  })
})

describe('machines', () => {
  it('computes the certificate expiry from the last renewal and the policy', () => {
    const now = Date.parse('2026-10-10T00:00:00Z')
    const machine = { CertificateThumbprint: 'AB', LastRenewalAt: '2026-08-01T00:00:00Z' }

    expect(certificateDaysLeft(machine, 90, now)).toBe(20)
    expect(certificateDaysLeft(machine, 30, now)).toBeLessThan(0)
    expect(certificateDaysLeft({ LastRenewalAt: '2026-08-01T00:00:00Z' }, 90, now)).toBeUndefined()
    expect(issuesCertificates({ CredentialKind: 'CertificateAndApiKey' })).toBe(true)
    expect(issuesCertificates({ CredentialKind: 'ClientCredentials' })).toBe(false)
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
  })
})

describe('OneTimeSecretDialog', () => {
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

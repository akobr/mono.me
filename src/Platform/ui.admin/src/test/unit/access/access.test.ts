import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from 'vitest'
import { defineComponent, h, ref, type Ref } from 'vue'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import {
  createMachineAccess,
  deleteMachineAccess,
  getMachineAccess,
  getMachineAuthentication,
  issueSharedCertificate,
  MachineScope,
  resetMachineAccess,
  setMachineAuthentication,
  setMachineFederationRules,
  type AccessPointMember,
  type AccountRole,
  type FederationRule,
  type MachineAccessCreate,
  type MachineAuthenticationPolicy,
} from '@42for.net/2splatform.sdk'
import { memberRules } from '@/utils/roles'
import {
  allowsSharedCertificates,
  certificateDaysLeft,
  inApiOrder,
  isManagementScope,
  isSecretsScope,
  issuesCertificates,
  machineScopes,
  machineSecret,
  managementScopes,
  requiredRoleFor,
  runtimeScopes,
  scopeDescriptions,
  scopeLabels,
  scopePresets,
  withImpliedReads,
} from '@/utils/machines'
import MachineCreateDialog from '@/components/access/MachineCreateDialog.vue'
import OneTimeSecretDialog from '@/components/access/OneTimeSecretDialog.vue'
import MembersTable from '@/components/access/MembersTable.vue'
import { mockHandlers } from '@/mock/all-handlers'
import { loadDb, saveDb } from '@/mock/db'
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

  it('lists every machine scope in the order of the API, in two groups, with its scope string and what it allows', () => {
    expect(machineScopes).toEqual(Object.values(MachineScope))
    expect(runtimeScopes.map((scope) => scopeLabels[scope])).toEqual([
      'Annotation.Read',
      'Annotation.Write',
      'Configuration.Read',
      'Configuration.Write',
      'Secrets.Read',
      'Secrets.Write',
    ])
    expect(managementScopes.map((scope) => scopeLabels[scope])).toEqual([
      'Configuration.Transfer',
      'Schema.Write',
      'Template.Write',
      'Annotation.Manage',
      'KeyValue.Manage',
    ])
    expect(machineScopes.filter(isManagementScope)).toEqual(managementScopes)
    expect(machineScopes.filter(isSecretsScope)).toEqual(['SecretsRead', 'SecretsWrite'])
    expect(machineScopes.every((scope) => scopeDescriptions[scope].length > 0)).toBe(true)
    // The secrets scopes include no configuration scope, so they name the one they need.
    expect(scopeDescriptions.SecretsRead).toContain('Needs Configuration.Read as well.')
    expect(scopeDescriptions.SecretsWrite).toContain('Needs Configuration.Write as well.')
  })

  it('completes the write scopes with their read scopes, distinct and in the order of the API', () => {
    expect(withImpliedReads(['SecretsWrite', 'ConfigurationTransfer', 'AnnotationWrite', 'AnnotationWrite'])).toEqual([
      'AnnotationRead',
      'AnnotationWrite',
      'SecretsRead',
      'SecretsWrite',
      'ConfigurationTransfer',
    ])
    // Secrets scopes imply no configuration scope, and management scopes imply nothing.
    expect(withImpliedReads(['SchemaWrite', 'SecretsRead'])).toEqual(['SecretsRead', 'SchemaWrite'])
    expect(inApiOrder(['KeyValueManage', 'ConfigurationRead', 'KeyValueManage'])).toEqual([
      'ConfigurationRead',
      'KeyValueManage',
    ])
  })

  it('offers presets of typical machines, each as the API stores it', () => {
    expect(scopePresets.map((preset) => [preset.name, preset.scopes.map((scope) => scopeLabels[scope])])).toEqual([
      ['Runtime reader', ['Annotation.Read', 'Configuration.Read']],
      ['Runtime with secrets', ['Annotation.Read', 'Configuration.Read', 'Secrets.Read']],
      ['CI writer', ['Annotation.Read', 'Annotation.Write', 'Configuration.Read', 'Configuration.Write']],
      [
        'Git sync',
        [
          'Annotation.Read',
          'Annotation.Write',
          'Configuration.Read',
          'Configuration.Write',
          'Configuration.Transfer',
          'Schema.Write',
          'Template.Write',
          'Annotation.Manage',
        ],
      ],
      ['Backup', ['Annotation.Read', 'Configuration.Read', 'Configuration.Transfer']],
      ['Schema deployer', ['Configuration.Read', 'Configuration.Write', 'Schema.Write', 'Template.Write']],
    ])
    for (const preset of scopePresets) {
      expect(withImpliedReads(preset.scopes)).toEqual(preset.scopes)
    }
  })

  it('names the role that gives the scopes of a machine, and with them its reset and its federation rules', () => {
    expect(requiredRoleFor(['AnnotationRead', 'AnnotationWrite', 'ConfigurationWrite'])).toBe('Contributor')
    expect(requiredRoleFor(['ConfigurationRead', 'SecretsRead'])).toBe('ContributorWithSecrets')
    expect(requiredRoleFor({ Scopes: ['AnnotationRead', 'AnnotationManage'] })).toBe('Administrator')
    expect(requiredRoleFor({ Scopes: ['SecretsWrite', 'KeyValueManage'] })).toBe('Administrator')
    // A machine without scopes (stored before 424-79) needs no more than the routes.
    expect(requiredRoleFor({})).toBe('Contributor')
    expect(requiredRoleFor(undefined)).toBe('Contributor')
  })
})

describe('machine scopes against the mock API', () => {
  const rule: FederationRule = {
    Issuer: 'github',
    Audience: 'http://api.test/api/v1/acme/billing/access/token',
    Subject: 'repo:acme/billing:ref:refs/heads/main',
  }
  const noRuntimeScope =
    'A machine needs at least one runtime scope: AnnotationRead, AnnotationWrite, ConfigurationRead, ConfigurationWrite, SecretsRead or SecretsWrite.'

  /** Changes the user's role in acme.billing, which is Owner in the seed. */
  function setRole(role: AccountRole) {
    const db = loadDb()
    db.account!.AccessMap!['acme.billing'] = role
    saveDb(db)
  }

  it('stores the scopes of a new machine with the read scope of each write scope, distinct and in the order of the API', async () => {
    const machine = await createMachineAccess('acme', 'billing', {
      Scopes: ['SchemaWrite', 'ConfigurationWrite', 'AnnotationRead', 'SchemaWrite', 'SecretsWrite'],
    })
    const stored: MachineScope[] = [
      'AnnotationRead',
      'ConfigurationRead',
      'ConfigurationWrite',
      'SecretsRead',
      'SecretsWrite',
      'SchemaWrite',
    ]

    expect(machine.Scopes).toEqual(stored)
    expect((await getMachineAccess('acme', 'billing', machine.Id!)).Scopes).toEqual(stored)
    // The API reads integers as enum values.
    expect(
      (await createMachineAccess('acme', 'billing', { Scopes: [6, 1] as unknown as MachineScope[] })).Scopes,
    ).toEqual(['AnnotationRead', 'AnnotationWrite', 'ConfigurationTransfer'])
    // And names in any case, and integers in strings.
    const lenient = ['configurationread', ' 4 '] as unknown as MachineScope[]
    expect((await createMachineAccess('acme', 'billing', { Scopes: lenient })).Scopes).toEqual([
      'ConfigurationRead',
      'SecretsRead',
    ])
  })

  it('refuses an undefined or unknown scope, and a machine without a runtime scope, as the API does', async () => {
    await expect(
      createMachineAccess('acme', 'billing', { Scopes: ['AnnotationRead', 99 as unknown as MachineScope] }),
    ).rejects.toMatchObject({ status: 400, message: "The machine scope 99 doesn't exist." })
    // Scope strings are not enum names.
    await expect(
      createMachineAccess('acme', 'billing', { Scopes: ['AnnotationRead', 'Annotation.Write' as MachineScope] }),
    ).rejects.toMatchObject({ status: 400, message: 'The request body is not a machine (at $.Scopes[1]).' })
    // The API reads the whole body before it looks for an undefined value.
    await expect(
      createMachineAccess('acme', 'billing', { Scopes: [99, 'Bogus'] as unknown as MachineScope[] }),
    ).rejects.toMatchObject({ status: 400, message: 'The request body is not a machine (at $.Scopes[1]).' })
    await expect(
      createMachineAccess('acme', 'billing', { Scopes: ['AnnotationRead', '99' as MachineScope] }),
    ).rejects.toMatchObject({ status: 400, message: "The machine scope 99 doesn't exist." })
    // A missing list, an empty one and management scopes alone.
    for (const body of [{}, { Scopes: [] }, { Scopes: ['ConfigurationTransfer'] }] as MachineAccessCreate[]) {
      await expect(createMachineAccess('acme', 'billing', body)).rejects.toMatchObject({
        status: 400,
        message: noRuntimeScope,
      })
    }
    expect(loadDb().machines?.['acme.billing']).toHaveLength(3)
  })

  it('lets only Administrators give management scopes, and only users with secret rights secrets scopes', async () => {
    // The user contributes to acme.shipping.
    await expect(
      createMachineAccess('acme', 'shipping', { Scopes: ['AnnotationRead', 'ConfigurationTransfer'] }),
    ).rejects.toMatchObject({
      status: 403,
      errorCode: 'AccessDenied',
      message: "No Administrator access to 'acme.shipping'.",
    })
    await expect(
      createMachineAccess('acme', 'shipping', { Scopes: ['ConfigurationRead', 'SecretsRead'] }),
    ).rejects.toMatchObject({
      status: 403,
      errorCode: 'AccessDenied',
      message: "No ContributorWithSecrets access to 'acme.shipping'.",
    })
    expect(loadDb().machines?.['acme.shipping'] ?? []).toEqual([])
    await expect(
      createMachineAccess('acme', 'shipping', { Scopes: ['AnnotationWrite', 'ConfigurationWrite'] }),
    ).resolves.toMatchObject({
      Scopes: ['AnnotationRead', 'AnnotationWrite', 'ConfigurationRead', 'ConfigurationWrite'],
    })

    setRole('ContributorWithSecrets')
    await expect(createMachineAccess('acme', 'billing', { Scopes: ['SecretsWrite'] })).resolves.toMatchObject({
      Scopes: ['SecretsRead', 'SecretsWrite'],
    })
    await expect(
      createMachineAccess('acme', 'billing', { Scopes: ['ConfigurationRead', 'KeyValueManage'] }),
    ).rejects.toMatchObject({ status: 403, message: "No Administrator access to 'acme.billing'." })
  })

  it('resets a machine or replaces its federation rules only for users with the role its scopes need', async () => {
    const managed = await createMachineAccess('acme', 'billing', {
      Scopes: ['ConfigurationRead', 'KeyValueManage'],
      CredentialKind: 'ApiKey',
    })
    const federated = await createMachineAccess('acme', 'billing', {
      Scopes: ['AnnotationWrite', 'ConfigurationTransfer'],
      CredentialKind: 'Federated',
      FederationRules: [rule],
    })
    const withSecrets = await createMachineAccess('acme', 'billing', {
      Scopes: ['ConfigurationRead', 'SecretsRead'],
      CredentialKind: 'Federated',
      FederationRules: [rule],
    })
    const secretsKey = await createMachineAccess('acme', 'billing', {
      Scopes: ['ConfigurationRead', 'SecretsRead'],
      CredentialKind: 'ApiKey',
    })
    const denied = (role: string) => ({
      status: 403,
      errorCode: 'AccessDenied',
      message: `No ${role} access to 'acme.billing'.`,
    })

    setRole('Contributor')
    await expect(resetMachineAccess('acme', 'billing', managed.Id!)).rejects.toMatchObject(denied('Administrator'))
    await expect(setMachineFederationRules('acme', 'billing', federated.Id!, [rule])).rejects.toMatchObject(
      denied('Administrator'),
    )
    // The seeded mach-billing-sync reads secrets: the role is checked before its kind.
    await expect(resetMachineAccess('acme', 'billing', 'mach-billing-sync')).rejects.toMatchObject(
      denied('ContributorWithSecrets'),
    )
    await expect(resetMachineAccess('acme', 'billing', secretsKey.Id!)).rejects.toMatchObject(
      denied('ContributorWithSecrets'),
    )
    await expect(setMachineFederationRules('acme', 'billing', withSecrets.Id!, [rule])).rejects.toMatchObject(
      denied('ContributorWithSecrets'),
    )
    // Runtime scopes without secrets stay with Contributors.
    expect((await resetMachineAccess('acme', 'billing', 'mach-invoicing-worker')).AccessKey).toBeDefined()
    await expect(setMachineFederationRules('acme', 'billing', 'mach-release-pipeline', [rule])).resolves.toMatchObject({
      FederationRules: [rule],
    })

    setRole('ContributorWithSecrets')
    expect((await resetMachineAccess('acme', 'billing', secretsKey.Id!)).AccessKey).toBeDefined()
    // Past the role, the seeded Certificate machine has no secret to reset (424-82).
    await expect(resetMachineAccess('acme', 'billing', 'mach-billing-sync')).rejects.toMatchObject({ status: 400 })
    await expect(setMachineFederationRules('acme', 'billing', withSecrets.Id!, [rule])).resolves.toMatchObject({
      Scopes: ['ConfigurationRead', 'SecretsRead'],
    })
    await expect(resetMachineAccess('acme', 'billing', managed.Id!)).rejects.toMatchObject(denied('Administrator'))

    setRole('Owner')
    const reset = await resetMachineAccess('acme', 'billing', managed.Id!)
    expect(reset.AccessKey).toBeDefined()
    expect(reset.Scopes).toEqual(['ConfigurationRead', 'KeyValueManage'])
    await expect(setMachineFederationRules('acme', 'billing', federated.Id!, [rule])).resolves.toMatchObject({
      Scopes: ['AnnotationRead', 'AnnotationWrite', 'ConfigurationTransfer'],
    })
  })

  it('deletes a machine only for users with the role its scopes need', async () => {
    const managed = await createMachineAccess('acme', 'billing', {
      Scopes: ['ConfigurationRead', 'ConfigurationTransfer'],
      CredentialKind: 'ApiKey',
    })
    const denied = (role: string) => ({
      status: 403,
      errorCode: 'AccessDenied',
      message: `No ${role} access to 'acme.billing'.`,
    })
    const ids = () => (loadDb().machines?.['acme.billing'] ?? []).map((machine) => machine.Id)

    setRole('Contributor')
    await expect(deleteMachineAccess('acme', 'billing', managed.Id!)).rejects.toMatchObject(denied('Administrator'))
    // The seeded mach-billing-sync reads secrets.
    await expect(deleteMachineAccess('acme', 'billing', 'mach-billing-sync')).rejects.toMatchObject(
      denied('ContributorWithSecrets'),
    )
    expect(ids()).toEqual(expect.arrayContaining([managed.Id, 'mach-billing-sync']))
    // Runtime scopes without secrets stay with Contributors.
    await deleteMachineAccess('acme', 'billing', 'mach-invoicing-worker')
    expect(ids()).not.toContain('mach-invoicing-worker')

    setRole('ContributorWithSecrets')
    await deleteMachineAccess('acme', 'billing', 'mach-billing-sync')
    await expect(deleteMachineAccess('acme', 'billing', managed.Id!)).rejects.toMatchObject(denied('Administrator'))

    setRole('Administrator')
    await deleteMachineAccess('acme', 'billing', managed.Id!)
    expect(ids()).not.toContain(managed.Id)
    expect(ids()).not.toContain('mach-billing-sync')
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

    const certificate = await createMachineAccess('acme', 'billing', { Scopes: ['AnnotationRead'] })
    expect(certificate).toMatchObject({ CredentialKind: 'Certificate', CertificateLifetimeDays: 30 })
    expect(certificate.CertificateNotAfter).toBeDefined()
    const apiKey = await createMachineAccess('acme', 'billing', {
      Scopes: ['AnnotationRead'],
      CredentialKind: 'ApiKey',
    })
    expect(apiKey.CredentialKind).toBe('ApiKey')
    expect(apiKey.CertificateLifetimeDays).toBeUndefined()
    await expect(
      createMachineAccess('acme', 'billing', { Scopes: ['AnnotationRead'], CredentialKind: 'ClientCredentials' }),
    ).rejects.toMatchObject({
      status: 400,
      message: 'The credential kind ClientCredentials is not enabled in the project acme.billing.',
      errorCode: 'MachineAccessNotSupported',
    })
  })

  it('issues shared certificates and creates machines that use them only with CertificateAndApiKey', async () => {
    await setMachineAuthentication('acme.billing', { EnabledCredentialKinds: ['ApiKey'] })
    await expect(issueSharedCertificate('acme', 'billing', { Label: 'edge' })).rejects.toMatchObject({
      status: 400,
      message: 'The project acme.billing does not enable CertificateAndApiKey, which shared certificates belong to.',
      errorCode: 'MachineAccessNotSupported',
    })
    await expect(
      createMachineAccess('acme', 'billing', { Scopes: ['AnnotationRead'], UseSharedCertificate: true }),
    ).rejects.toMatchObject({ status: 400, errorCode: 'MachineAccessNotSupported' })

    await setMachineAuthentication('acme.billing', { EnabledCredentialKinds: ['ApiKey', 'CertificateAndApiKey'] })
    expect((await issueSharedCertificate('acme', 'billing', { Label: 'edge' })).Label).toBe('edge')
    const machine = await createMachineAccess('acme', 'billing', {
      Scopes: ['AnnotationRead'],
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
    expect(reset.AccessKey).toBeDefined()
    expect(reset.AccessKey).not.toBe(machine.AccessKey)
    expect(reset.Certificate).toBeUndefined()
    expect(reset.UsesSharedCertificate).toBe(true)
  })

  it('refuses the reset of a Certificate machine, which is renewed, and leaves it as it was (424-82, 424-102)', async () => {
    // The seed is stored first: until then every read makes it anew, with other times.
    saveDb(loadDb())
    const stored = () => loadDb().machines?.['acme.billing']?.find((entry) => entry.Id === 'mach-billing-sync')
    const before = stored()
    expect(before?.CredentialKind).toBe('Certificate')

    await expect(resetMachineAccess('acme', 'billing', 'mach-billing-sync')).rejects.toMatchObject({
      status: 400,
      message:
        'A Certificate machine has no key to reset: it renews its certificate itself over mTLS, and deleting the machine requests the revocation of its certificate.',
      errorCode: 'MachineAccessNotSupported',
    })
    expect(stored()).toEqual(before)
  })

  it('resets only the API key of a CertificateAndApiKey machine, which keeps its own certificate (424-82)', async () => {
    await setMachineAuthentication('acme.billing', { EnabledCredentialKinds: ['ApiKey', 'CertificateAndApiKey'] })
    const machine = await createMachineAccess('acme', 'billing', {
      Scopes: ['AnnotationRead'],
      CredentialKind: 'CertificateAndApiKey',
      CertificateLifetimeDays: 30,
    })
    expect(machine.Certificate).toBeDefined()
    expect(machine.CertificateThumbprint).toBeDefined()
    const stored = () => loadDb().machines?.['acme.billing']?.find((entry) => entry.Id === machine.Id)
    // Renewed earlier: the reset keeps that time, as it keeps the certificate.
    const renewedAt = '2026-08-01T00:00:00.000Z'
    const db = loadDb()
    db.machines!['acme.billing']!.find((entry) => entry.Id === machine.Id)!.LastRenewalAt = renewedAt
    saveDb(db)
    const certificate = {
      CredentialKind: 'CertificateAndApiKey',
      CertificateThumbprint: machine.CertificateThumbprint,
      CertificateLifetimeDays: 30,
      CertificateNotAfter: machine.CertificateNotAfter,
      LastRenewalAt: renewedAt,
    }

    const reset = await resetMachineAccess('acme', 'billing', machine.Id!)
    expect(reset.AccessKey).toBeDefined()
    expect(reset.AccessKey).not.toBe(machine.AccessKey)
    expect(reset).toMatchObject({ ...certificate, Scopes: ['AnnotationRead'] })
    expect(reset.UsesSharedCertificate).toBeFalsy()
    expect(reset.Certificate).toBeUndefined()
    expect(reset.CertificatePassword).toBeUndefined()
    // The stored machine keeps the certificate and holds no secret.
    expect(stored()).toMatchObject(certificate)
    expect(stored()?.AccessKey).toBeUndefined()
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
  /** Mounts the dialog with a policy that a test may replace, and opens it; the user is an Owner unless a role is given. */
  async function openDialog(policy: Ref<MachineAuthenticationPolicy | undefined>, role?: string) {
    const open = ref(false)
    const Harness = defineComponent({
      setup: () => () =>
        h(MachineCreateDialog, {
          open: open.value,
          'onUpdate:open': (value: boolean) => (open.value = value),
          policy: policy.value,
        }),
    })
    const screen = await mountScreen(Harness, {}, role ? { accessMap: { 'acme.billing': role } } : {})
    open.value = true
    await settle(6)
    return { ...screen, open }
  }

  /** The checkbox of the dialog with this label. */
  function checkbox(label: string) {
    const element = [...document.body.querySelectorAll('label')].find((entry) => entry.textContent?.trim() === label)
    return document.getElementById(element!.htmlFor) as HTMLButtonElement
  }

  async function create(policy: MachineAuthenticationPolicy, before?: () => Promise<void>, role?: string) {
    const bodies: MachineAccessCreate[] = []
    server.use(
      http.post('*/api/v1/:org/:project/access/machines', async ({ request }) => {
        bodies.push((await request.json()) as MachineAccessCreate)
        return HttpResponse.json({ Id: 'm1', AccessKey: '' }, { status: 201 })
      }),
    )
    const screen = await openDialog(ref(policy), role)
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
      checkbox('Use a shared certificate of the project').click()
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

  describe('scopes', () => {
    const policy: MachineAuthenticationPolicy = { EnabledCredentialKinds: ['ApiKey'], CredentialKind: 'ApiKey' }
    const checked = (label: string) => checkbox(label).getAttribute('aria-checked') === 'true'
    /** The scope strings of the picked scopes, in the order of the dialog. */
    const picked = () => machineScopes.map((scope) => scopeLabels[scope]).filter(checked)

    /** The button of the dialog with this label. */
    function button(label: string) {
      return [...document.body.querySelectorAll('button')].find((entry) => entry.textContent?.trim() === label)!
    }

    async function click(element: HTMLElement) {
      element.click()
      await settle(4)
    }

    it('starts from Runtime reader, offers every scope and preset to an Owner, and sends only the scopes', async () => {
      const { body, text } = await create(policy, async () => {
        expect(picked()).toEqual(['Annotation.Read', 'Configuration.Read'])
        expect(machineScopes.every((scope) => !checkbox(scopeLabels[scope]).disabled)).toBe(true)
        expect(scopePresets.every((preset) => !button(preset.name).disabled)).toBe(true)
      })

      expect(text).toContain('Runtime scopes')
      expect(text).toContain('Management scopes')
      expect(text).toContain('Bulk and structural operations. Each also needs the runtime scope of its operation')
      expect(text).not.toContain('Only Administrators give management scopes.')
      expect(text).not.toContain('Only users with secret rights give secrets scopes.')
      expect(body?.Scopes).toEqual(['AnnotationRead', 'ConfigurationRead'])
      expect(body).not.toHaveProperty('Scope')
      expect(body).not.toHaveProperty('ManagementScopes')
    })

    it('replaces the picked scopes with a preset, and sends them in the order of the API', async () => {
      const { body } = await create(policy, async () => {
        await click(checkbox('Secrets.Read'))
        await click(button('Git sync'))
        expect(picked()).toEqual([
          'Annotation.Read',
          'Annotation.Write',
          'Configuration.Read',
          'Configuration.Write',
          'Configuration.Transfer',
          'Schema.Write',
          'Template.Write',
          'Annotation.Manage',
        ])
        await click(checkbox('KeyValue.Manage'))
        await click(checkbox('Template.Write'))
      })

      expect(body?.Scopes).toEqual([
        'AnnotationRead',
        'AnnotationWrite',
        'ConfigurationRead',
        'ConfigurationWrite',
        'ConfigurationTransfer',
        'SchemaWrite',
        'AnnotationManage',
        'KeyValueManage',
      ])
      // Nothing about the preset.
      expect(JSON.stringify(body)).not.toContain('Git sync')
    })

    it('picks the read scope with a write scope, and drops the write scope with its read scope', async () => {
      const { body } = await create(policy, async () => {
        await click(checkbox('Secrets.Write'))
        await click(checkbox('Annotation.Write'))
        expect(picked()).toEqual([
          'Annotation.Read',
          'Annotation.Write',
          'Configuration.Read',
          'Secrets.Read',
          'Secrets.Write',
        ])
        await click(checkbox('Annotation.Read'))
        expect(picked()).toEqual(['Configuration.Read', 'Secrets.Read', 'Secrets.Write'])
        // A write scope goes alone.
        await click(checkbox('Secrets.Write'))
      })

      expect(body?.Scopes).toEqual(['ConfigurationRead', 'SecretsRead'])
    })

    it('creates no machine without a runtime scope', async () => {
      const screen = await openDialog(ref(policy))
      await click(checkbox('Annotation.Read'))
      await click(checkbox('Configuration.Read'))
      expect(document.body.textContent).toContain('Pick at least one runtime scope.')
      expect(button('Create').disabled).toBe(true)

      // Management scopes alone are not enough.
      await click(checkbox('Configuration.Transfer'))
      expect(button('Create').disabled).toBe(true)
      await click(checkbox('Configuration.Read'))
      expect(document.body.textContent).not.toContain('Pick at least one runtime scope.')
      expect(button('Create').disabled).toBe(false)
      screen.host.unmount()
    })

    it('starts from Runtime reader again when the dialog opens again', async () => {
      const bodies: MachineAccessCreate[] = []
      server.use(
        http.post('*/api/v1/:org/:project/access/machines', async ({ request }) => {
          bodies.push((await request.json()) as MachineAccessCreate)
          return HttpResponse.json({ Id: 'm1', AccessKey: '' }, { status: 201 })
        }),
      )
      const screen = await openDialog(ref(policy))
      await click(button('Schema deployer'))
      expect(checked('Template.Write')).toBe(true)

      screen.open.value = false
      await settle(4)
      screen.open.value = true
      await settle(6)
      expect(picked()).toEqual(['Annotation.Read', 'Configuration.Read'])
      button('Create').click()
      await settle(6)
      expect(bodies[0]?.Scopes).toEqual(['AnnotationRead', 'ConfigurationRead'])
      screen.host.unmount()
    })

    it('disables the management scopes and the presets with one below Administrator', async () => {
      const { body, text } = await create(
        policy,
        async () => {
          // A click on a disabled checkbox or button picks nothing.
          await click(checkbox('Annotation.Manage'))
          await click(button('Backup'))
          expect(picked()).toEqual(['Annotation.Read', 'Configuration.Read'])
          expect(managementScopes.every((scope) => checkbox(scopeLabels[scope]).disabled)).toBe(true)
          expect(runtimeScopes.every((scope) => !checkbox(scopeLabels[scope]).disabled)).toBe(true)
          expect(scopePresets.filter((preset) => button(preset.name).disabled).map((preset) => preset.name)).toEqual([
            'Git sync',
            'Backup',
            'Schema deployer',
          ])
          await click(button('Runtime with secrets'))
        },
        'ContributorWithSecrets',
      )

      expect(text).toContain('Only Administrators give management scopes.')
      expect(text).not.toContain('Only users with secret rights give secrets scopes.')
      expect(body?.Scopes).toEqual(['AnnotationRead', 'ConfigurationRead', 'SecretsRead'])
    })

    it('disables the secrets scopes and their preset without secret rights', async () => {
      const { body, text } = await create(
        policy,
        async () => {
          // A click on a disabled checkbox or button picks nothing.
          await click(checkbox('Secrets.Read'))
          await click(button('Runtime with secrets'))
          expect(picked()).toEqual(['Annotation.Read', 'Configuration.Read'])
          expect(checkbox('Secrets.Read').disabled).toBe(true)
          expect(checkbox('Secrets.Write').disabled).toBe(true)
          expect(checkbox('Configuration.Write').disabled).toBe(false)
          expect(button('Runtime with secrets').disabled).toBe(true)
          await click(button('CI writer'))
        },
        'Contributor',
      )

      expect(text).toContain('Only users with secret rights give secrets scopes.')
      expect(text).toContain('Only Administrators give management scopes.')
      expect(body?.Scopes).toEqual(['AnnotationRead', 'AnnotationWrite', 'ConfigurationRead', 'ConfigurationWrite'])
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

  it('writes the .env snippet of an API key with the canonical variable names of sform and the SDK', async () => {
    const view = mountInApp(OneTimeSecretDialog, {
      secret: { title: 'Created', apiKey: 'st_secret', credentialKind: 'ApiKey', baseUrl: 'https://storyteller.example.com/api' },
      onClose: () => {},
    })
    await settle()

    const text = document.body.textContent ?? ''
    expect(text).toContain('STORYTELLER_ENDPOINT=https://storyteller.example.com/api')
    expect(text).toContain('STORYTELLER_API_KEY=st_secret')
    expect(text).not.toContain('STORYTELLER_API_URL')
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
    expect(text).toContain('a reset issues a new API key, and the certificate stays valid')
    view.host.unmount()
  })

  it('does not promise a reset for a Certificate machine, which has none', async () => {
    const view = mountInApp(OneTimeSecretDialog, {
      secret: {
        title: 'Created',
        credentialKind: 'Certificate',
        certificate: btoa('pfx-bytes'),
        certificatePassword: 'pw',
      },
      onClose: () => {},
    })
    await settle()

    const text = document.body.textContent ?? ''
    expect(text).toContain('to replace the certificate, create a new machine')
    expect(text).not.toContain('a reset issues')
    view.host.unmount()
  })

  it('tells that a reset of a CertificateAndApiKey machine issues a new API key but not its certificate', async () => {
    const view = mountInApp(OneTimeSecretDialog, {
      secret: {
        title: 'Created',
        credentialKind: 'CertificateAndApiKey',
        apiKey: 'st_secret',
        certificate: btoa('pfx-bytes'),
        certificatePassword: 'pw',
      },
      onClose: () => {},
    })
    await settle()

    const text = document.body.textContent ?? ''
    expect(text).toContain('a reset issues a new API key but not a new certificate')
    expect(text).not.toContain('a reset issues new ones')
    view.host.unmount()
  })

  it('tells that a shared certificate is replaced by issuing another one', async () => {
    const view = mountInApp(OneTimeSecretDialog, {
      secret: { title: 'Shared certificate issued', certificate: btoa('pfx-bytes'), certificatePassword: 'pw' },
      onClose: () => {},
    })
    await settle()

    const text = document.body.textContent ?? ''
    expect(text).toContain('to replace the certificate, issue another one')
    expect(text).not.toContain('a reset issues')
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

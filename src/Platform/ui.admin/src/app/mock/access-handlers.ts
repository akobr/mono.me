import { http, HttpResponse, type HttpHandler } from 'msw'
import type {
  AccessPointMember,
  AccountRole,
  Invitation,
  InvitationCreate,
  MachineAccess,
  MachineAccessCreate,
  MachineAuthenticationPolicy,
  MachineCredentialKind,
  MemberRoleUpdate,
  SharedCertificate,
  SharedCertificateCreate,
} from '@42for.net/2splatform.sdk'
import { roleRank } from '@42for.net/2splatform.sdk'
import { mockDirectory, type MockDb } from './db'
import { errorResponse, requireRole, roleOf, withDb } from './handlers'

// Members, invitations, machines, shared certificates, the machine policy and the CA of the mock API, with the
// rules of companion spec E2/E3 (last owner, elevated roles, self changes, pending invitations).

const api = '*/api/v1'
const random = (length: number) =>
  Array.from({ length }, () => Math.floor(Math.random() * 16).toString(16))
    .join('')
    .toUpperCase()
const fakeSecret = () => btoa(String.fromCharCode(...Array.from({ length: 48 }, () => Math.floor(Math.random() * 256))))

function members(db: MockDb, key: string): AccessPointMember[] {
  return Object.entries(db.accessPoints[key]?.AccessMap ?? {}).map(([accountId, role]) => ({
    AccountId: accountId,
    Name: mockDirectory[accountId]?.name,
    UserName: mockDirectory[accountId]?.email,
    Role: role,
  }))
}

/** Keeps the account's AccessMap in sync with the access points it is a member of. */
function syncAccount(db: MockDb) {
  if (!db.account) {
    return
  }
  db.account.AccessMap = Object.fromEntries(
    Object.entries(db.accessPoints)
      .map(([key, point]) => [key, point.AccessMap?.[db.user.id]] as const)
      .filter((entry): entry is [string, AccountRole] => !!entry[1]),
  )
}

const ownerCount = (db: MockDb, key: string) =>
  Object.values(db.accessPoints[key]?.AccessMap ?? {}).filter((role) => role === 'Owner').length

const machineScope = (params: { org: string; project: string }) => `${params.org}.${params.project}`

const credentialKinds: MachineCredentialKind[] = ['ApiKey', 'Certificate', 'CertificateAndApiKey', 'ClientCredentials']
const deploymentLifetime = { min: 1, max: 3650, default: 365 }

/** The policy as the API resolves it (424-14): the enabled kinds in enum order, a default among them, a lifetime. */
function effectivePolicy(stored: MachineAuthenticationPolicy | undefined): MachineAuthenticationPolicy {
  const listed = stored?.EnabledCredentialKinds?.length
    ? stored.EnabledCredentialKinds
    : stored?.CredentialKind
      ? [stored.CredentialKind]
      : ['ApiKey' as const]
  const enabled = credentialKinds.filter((kind) => listed.includes(kind))
  const defaultKind =
    stored?.CredentialKind && enabled.includes(stored.CredentialKind) ? stored.CredentialKind : listed[0]
  return {
    EnabledCredentialKinds: enabled,
    CredentialKind: defaultKind,
    CertificateLifetimeDays: Math.min(
      Math.max(stored?.CertificateLifetimeDays ?? deploymentLifetime.default, deploymentLifetime.min),
      deploymentLifetime.max,
    ),
  }
}

/** The 400 messages of PUT …/machine-authentication. */
function policyError(policy: MachineAuthenticationPolicy): string | undefined {
  const kinds = policy.EnabledCredentialKinds?.length
    ? policy.EnabledCredentialKinds
    : policy.CredentialKind
      ? [policy.CredentialKind]
      : []
  const unknown = kinds.find((kind) => !credentialKinds.includes(kind))
  const days = policy.CertificateLifetimeDays
  if (kinds.length === 0) {
    return 'Enable at least one machine credential kind.'
  }
  if (unknown) {
    return `The machine credential kind ${unknown} doesn't exist.`
  }
  if (policy.CredentialKind && !kinds.includes(policy.CredentialKind)) {
    return `The default credential kind ${policy.CredentialKind} is not one of the enabled kinds.`
  }
  if (days != null && (days < deploymentLifetime.min || days > deploymentLifetime.max)) {
    return `The certificate lifetime must be between ${deploymentLifetime.min} and ${deploymentLifetime.max} days.`
  }
  return undefined
}

function withCredentials(
  machine: MachineAccess,
  kind: MachineCredentialKind,
  lifetimeDays?: number | null,
): MachineAccess {
  const certificate = kind === 'Certificate' || kind === 'CertificateAndApiKey'
  const lifetime = certificate
    ? (lifetimeDays ?? machine.CertificateLifetimeDays ?? deploymentLifetime.default)
    : undefined
  return {
    ...machine,
    CredentialKind: kind,
    AccessKey: kind === 'ApiKey' || kind === 'CertificateAndApiKey' ? `st_${random(40).toLowerCase()}` : undefined,
    Certificate: certificate ? fakeSecret() : undefined,
    CertificatePassword: certificate ? random(16) : undefined,
    CertificateThumbprint: certificate ? random(40) : undefined,
    CertificateLifetimeDays: lifetime,
    CertificateNotAfter: lifetime ? new Date(Date.now() + lifetime * 86_400_000).toISOString() : undefined,
    TokenEndpoint: kind === 'ClientCredentials' ? 'https://auth.example.com/oauth2/token' : undefined,
    TokenScope: kind === 'ClientCredentials' ? `storyteller:${machine.Scope}` : undefined,
    LastRenewalAt: new Date().toISOString(),
  }
}

/** Secrets are returned once; stored machines keep only the public fields. */
const stripSecrets = (machine: MachineAccess): MachineAccess => ({
  ...machine,
  AccessKey: undefined,
  Certificate: undefined,
  CertificatePassword: undefined,
})

export const accessManagementHandlers: HttpHandler[] = [
  http.get<{ key: string }>(
    `${api}/access/points/:key/members`,
    withDb(
      (db, { params }) => requireRole(db, params.key, 'Administrator') ?? HttpResponse.json(members(db, params.key)),
    ),
  ),

  http.put<{ key: string; accountId: string }>(
    `${api}/access/points/:key/members/:accountId`,
    withDb(async (db, { params, request }) => {
      const denied = requireRole(db, params.key, 'Administrator')
      if (denied) {
        return denied
      }
      const { Role } = (await request.json()) as MemberRoleUpdate
      const point = db.accessPoints[params.key]
      const current = point?.AccessMap?.[params.accountId]
      if (!point || !current) {
        return errorResponse(404, 'The member does not exist.', 'MemberNotFound')
      }
      const callerOwner = roleOf(db, params.key) === 'Owner'
      if ((Role === 'Owner' || current === 'Owner') && !callerOwner) {
        return errorResponse(403, 'Only Owners can grant or revoke Owner.', 'ElevatedRole')
      }
      if (params.accountId === db.user.id) {
        return errorResponse(409, "Members can't change their own role.", 'SelfRoleChange')
      }
      if (current === 'Owner' && Role !== 'Owner' && ownerCount(db, params.key) <= 1) {
        return errorResponse(409, 'The last Owner cannot be demoted.', 'LastOwner')
      }
      point.AccessMap = { ...point.AccessMap, [params.accountId]: Role! }
      syncAccount(db)
      return HttpResponse.json(members(db, params.key).find((member) => member.AccountId === params.accountId))
    }),
  ),

  http.delete<{ key: string; accountId: string }>(
    `${api}/access/points/:key/members/:accountId`,
    withDb((db, { params }) => {
      const denied = requireRole(db, params.key, 'Administrator')
      if (denied) {
        return denied
      }
      const point = db.accessPoints[params.key]
      const current = point?.AccessMap?.[params.accountId]
      if (!point || !current) {
        return errorResponse(404, 'The member does not exist.', 'MemberNotFound')
      }
      if (current === 'Owner' && ownerCount(db, params.key) <= 1) {
        return errorResponse(409, 'The last Owner cannot be removed.', 'LastOwner')
      }
      const { [params.accountId]: _removed, ...rest } = point.AccessMap ?? {}
      point.AccessMap = rest
      syncAccount(db)
      return new HttpResponse(null, { status: 204 })
    }),
  ),

  http.get<{ key: string }>(
    `${api}/access/points/:key/invitations`,
    withDb(
      (db, { params }) =>
        requireRole(db, params.key, 'Administrator') ??
        HttpResponse.json((db.invitations ?? []).filter((invitation) => invitation.AccessPointKey === params.key)),
    ),
  ),

  http.post<{ key: string }>(
    `${api}/access/points/:key/invitations`,
    withDb(async (db, { params, request }) => {
      const denied = requireRole(db, params.key, 'Administrator')
      if (denied) {
        return denied
      }
      const body = (await request.json()) as InvitationCreate
      if (body.Role === 'Owner' && roleOf(db, params.key) !== 'Owner') {
        return errorResponse(403, 'Only Owners can invite Owners.', 'ElevatedRole')
      }
      const email = (body.Email ?? '').trim().toLowerCase()
      if (
        (db.invitations ?? []).some(
          (entry) => entry.AccessPointKey === params.key && entry.Email === email && entry.Status === 'Pending',
        )
      ) {
        return errorResponse(409, `A pending invitation for ${email} already exists.`, 'InvitationExists')
      }
      const invitation: Invitation = {
        Id: `inv_${random(12)}`,
        AccessPointKey: params.key,
        Email: email,
        Role: body.Role,
        Status: 'Pending',
        InvitedById: db.user.id,
        InvitedByName: db.user.name,
        CreatedAt: new Date().toISOString(),
        ExpiresAt: new Date(Date.now() + (body.ExpiresInDays ?? 7) * 86_400_000).toISOString(),
        IsEmailSent: true,
      }
      ;(db.invitations ??= []).push(invitation)
      return HttpResponse.json(invitation, { status: 201 })
    }),
  ),

  http.post<{ key: string; id: string }>(
    `${api}/access/points/:key/invitations/:id/resend`,
    withDb((db, { params }) => {
      const denied = requireRole(db, params.key, 'Administrator')
      const invitation = db.invitations?.find((entry) => entry.Id === params.id)
      return (
        denied ??
        (invitation
          ? HttpResponse.json({ ...invitation, IsEmailSent: true })
          : errorResponse(404, 'Not found.', 'NotFound'))
      )
    }),
  ),

  http.delete<{ key: string; id: string }>(
    `${api}/access/points/:key/invitations/:id`,
    withDb((db, { params }) => {
      const denied = requireRole(db, params.key, 'Administrator')
      const invitation = db.invitations?.find((entry) => entry.Id === params.id)
      if (denied || !invitation) {
        return denied ?? errorResponse(404, 'Not found.', 'NotFound')
      }
      invitation.Status = 'Revoked'
      invitation.RespondedAt = new Date().toISOString()
      return HttpResponse.json(invitation)
    }),
  ),

  http.get(
    `${api}/access/invitations/mine`,
    withDb((db) =>
      HttpResponse.json((db.invitations ?? []).filter((invitation) => invitation.Email === db.user.email)),
    ),
  ),

  http.post<{ id: string }>(
    `${api}/access/invitations/:id/accept`,
    withDb((db, { params }) => {
      const invitation = db.invitations?.find((entry) => entry.Id === params.id && entry.Email === db.user.email)
      if (!invitation) {
        return errorResponse(404, 'The invitation does not exist.', 'NotFound')
      }
      if (invitation.Status !== 'Pending') {
        return errorResponse(409, 'The invitation is not pending.', 'InvitationNotPending')
      }
      invitation.Status = 'Accepted'
      invitation.AcceptedById = db.user.id
      invitation.RespondedAt = new Date().toISOString()
      const point = (db.accessPoints[invitation.AccessPointKey!] ??= { Key: invitation.AccessPointKey, AccessMap: {} })
      const current = point.AccessMap?.[db.user.id]
      if (!current || roleRank(current) < roleRank(invitation.Role!)) {
        point.AccessMap = { ...point.AccessMap, [db.user.id]: invitation.Role! }
      }
      db.account ??= { Id: db.user.id, UserName: db.user.email, Name: db.user.name, AccessMap: {} }
      syncAccount(db)
      return HttpResponse.json(db.account)
    }),
  ),

  http.post<{ id: string }>(
    `${api}/access/invitations/:id/decline`,
    withDb((db, { params }) => {
      const invitation = db.invitations?.find((entry) => entry.Id === params.id && entry.Email === db.user.email)
      if (!invitation) {
        return errorResponse(404, 'The invitation does not exist.', 'NotFound')
      }
      invitation.Status = 'Declined'
      invitation.RespondedAt = new Date().toISOString()
      return HttpResponse.json(invitation)
    }),
  ),

  http.get<{ key: string }>(
    `${api}/access/points/:key/machine-authentication`,
    withDb((db, { params }) => {
      return (
        requireRole(db, params.key, 'Contributor') ??
        HttpResponse.json(effectivePolicy(db.accessPoints[params.key]?.MachineAuthentication))
      )
    }),
  ),

  http.put<{ key: string }>(
    `${api}/access/points/:key/machine-authentication`,
    withDb(async (db, { params, request }) => {
      const denied = requireRole(db, params.key, 'Administrator')
      if (denied) {
        return denied
      }
      const policy = (await request.json()) as MachineAuthenticationPolicy
      const invalid = policyError(policy)
      if (invalid) {
        return errorResponse(400, invalid)
      }
      const stored = { ...effectivePolicy(policy), CertificateLifetimeDays: policy.CertificateLifetimeDays }
      db.accessPoints[params.key] = { ...db.accessPoints[params.key], MachineAuthentication: stored }
      return HttpResponse.json(effectivePolicy(stored))
    }),
  ),

  http.get(
    `${api}/access/certificate-authority`,
    () =>
      new HttpResponse(
        '-----BEGIN CERTIFICATE-----\nMIIBmockStorytellerCertificateAuthority\n-----END CERTIFICATE-----\n',
        {
          headers: { 'Content-Type': 'application/x-pem-file' },
        },
      ),
  ),

  http.get<{ org: string; project: string }>(
    `${api}/:org/:project/access/machines`,
    withDb((db, { params }) => {
      const key = machineScope(params)
      return requireRole(db, key, 'Contributor') ?? HttpResponse.json((db.machines?.[key] ?? []).map(stripSecrets))
    }),
  ),

  http.post<{ org: string; project: string }>(
    `${api}/:org/:project/access/machines`,
    withDb(async (db, { params, request }) => {
      const key = machineScope(params)
      const denied = requireRole(db, key, 'Contributor')
      if (denied) {
        return denied
      }
      const body = (await request.json()) as MachineAccessCreate
      const policy = effectivePolicy(db.accessPoints[key]?.MachineAuthentication)
      const kind = body.CredentialKind ?? policy.CredentialKind!
      if (!policy.EnabledCredentialKinds!.includes(kind)) {
        return errorResponse(400, `The credential kind ${kind} is not enabled in the project ${key}.`)
      }
      const machine = withCredentials(
        {
          Id: `mach-${random(6).toLowerCase()}`,
          ObjectId: `obj_${random(8)}`,
          Scope: body.Scope,
          AnnotationKey: body.AnnotationKey,
        },
        kind,
        body.CertificateLifetimeDays ?? policy.CertificateLifetimeDays,
      )
      ;((db.machines ??= {})[key] ??= []).push(stripSecrets(machine))
      return HttpResponse.json(machine, { status: 201 })
    }),
  ),

  http.get<{ org: string; project: string; id: string }>(
    `${api}/:org/:project/access/machines/:id`,
    withDb((db, { params }) => {
      const key = machineScope(params)
      const machine = db.machines?.[key]?.find((entry) => entry.Id === params.id)
      return (
        requireRole(db, key, 'Contributor') ??
        (machine ? HttpResponse.json(machine) : errorResponse(404, 'Not found.', 'NotFound'))
      )
    }),
  ),

  http.put<{ org: string; project: string; id: string }>(
    `${api}/:org/:project/access/machines/:id`,
    withDb((db, { params }) => {
      const key = machineScope(params)
      const denied = requireRole(db, key, 'Contributor')
      const list = db.machines?.[key] ?? []
      const index = list.findIndex((entry) => entry.Id === params.id)
      if (denied || index < 0) {
        return denied ?? errorResponse(404, 'Not found.', 'NotFound')
      }
      // A reset follows the kind the machine was created with, as the API does.
      const machine = withCredentials(list[index]!, list[index]!.CredentialKind ?? 'ApiKey')
      list[index] = stripSecrets(machine)
      return HttpResponse.json(machine)
    }),
  ),

  http.delete<{ org: string; project: string; id: string }>(
    `${api}/:org/:project/access/machines/:id`,
    withDb((db, { params }) => {
      const key = machineScope(params)
      const denied = requireRole(db, key, 'Contributor')
      if (denied) {
        return denied
      }
      ;(db.machines ??= {})[key] = (db.machines[key] ?? []).filter((entry) => entry.Id !== params.id)
      return new HttpResponse(null, { status: 204 })
    }),
  ),

  http.get<{ org: string; project: string }>(
    `${api}/:org/:project/access/certificates`,
    withDb((db, { params }) => {
      const key = machineScope(params)
      return requireRole(db, key, 'Administrator') ?? HttpResponse.json(db.certificates?.[key] ?? [])
    }),
  ),

  http.post<{ org: string; project: string }>(
    `${api}/:org/:project/access/certificates`,
    withDb(async (db, { params, request }) => {
      const key = machineScope(params)
      const denied = requireRole(db, key, 'Administrator')
      if (denied) {
        return denied
      }
      const body = (await request.json()) as SharedCertificateCreate
      const certificate: SharedCertificate = {
        Thumbprint: random(40),
        Label: body.Label,
        NotBefore: new Date().toISOString(),
        NotAfter: new Date(Date.now() + (body.LifetimeDays ?? 365) * 86_400_000).toISOString(),
        IsRevoked: false,
      }
      ;((db.certificates ??= {})[key] ??= []).push(certificate)
      return HttpResponse.json(
        { ...certificate, Certificate: fakeSecret(), CertificatePassword: random(16) },
        { status: 201 },
      )
    }),
  ),

  http.delete<{ org: string; project: string; thumbprint: string }>(
    `${api}/:org/:project/access/certificates/:thumbprint`,
    withDb((db, { params }) => {
      const key = machineScope(params)
      const denied = requireRole(db, key, 'Administrator')
      const certificate = db.certificates?.[key]?.find((entry) => entry.Thumbprint === params.thumbprint)
      if (denied || !certificate) {
        return denied ?? errorResponse(404, 'Not found.', 'NotFound')
      }
      certificate.IsRevoked = true
      return new HttpResponse(null, { status: 204 })
    }),
  ),
]

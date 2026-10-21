import { http, HttpResponse, type HttpHandler } from 'msw'
import type {
  AccessPointMember,
  AccountRole,
  FederatedIssuer,
  FederationRule,
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
import { MachineScope, roleRank } from '@42for.net/2splatform.sdk'
import { mockDirectory, type MockDb } from './db'
import {
  issuerError,
  issuerImmutableMessage,
  maxIssuersPerProject,
  resetNotSupportedMessage,
  rulesError,
  rulesOnOtherKindMessage,
  tooManyIssuersMessage,
} from './federation'
import { errorResponse, requireRole, roleOf, withDb } from './handlers'

// Members, invitations, machines, shared certificates, the machine policy, the trusted issuers and federation rules
// (424-46) and the CA of the mock API, with the rules of companion spec E2/E3 (last owner, elevated roles, self
// changes, pending invitations).

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

const credentialKinds: MachineCredentialKind[] = [
  'ApiKey',
  'Certificate',
  'CertificateAndApiKey',
  'ClientCredentials',
  'Federated',
]
const deploymentLifetime = { min: 1, max: 3650, default: 365 }

/** The machine scopes in the order of the API's enum (424-79): the runtime scopes, then the management scopes. */
const machineScopes = Object.values(MachineScope)
const managementScopes: MachineScope[] = [
  'ConfigurationTransfer',
  'SchemaWrite',
  'TemplateWrite',
  'AnnotationManage',
  'KeyValueManage',
]
const secretsScopes: MachineScope[] = ['SecretsRead', 'SecretsWrite']
const impliedReads: Partial<Record<MachineScope, MachineScope>> = {
  AnnotationWrite: 'AnnotationRead',
  ConfigurationWrite: 'ConfigurationRead',
  SecretsWrite: 'SecretsRead',
}

/**
 * A value of the scope list as the API's JsonStringEnumConverter reads it: a name in any case, or an integer, also in a
 * string, as an enum value (the integer itself when no scope has it); undefined when it is neither. Names joined by
 * commas, which the converter ORs, are not read.
 */
function readScope(value: unknown): MachineScope | number | undefined {
  if (typeof value === 'string') {
    const text = value.trim()
    return /^[+-]?\d+$/.test(text)
      ? readScope(Number(text))
      : machineScopes.find((scope) => scope.toLowerCase() === text.toLowerCase())
  }
  return Number.isInteger(value) ? (machineScopes[value as number] ?? (value as number)) : undefined
}

/**
 * The scopes of a new machine as the API reads them. The API reads the whole body first, so a value it cannot read
 * fails the body wherever it is, before an undefined integer is named; otherwise the list completed with the read
 * scope of every write scope, distinct, in the order of the enum.
 */
function machineScopesOf(body: MachineAccessCreate): { scopes: MachineScope[]; invalid?: string } {
  const values: unknown = body.Scopes ?? []
  if (!Array.isArray(values)) {
    return { scopes: [], invalid: 'The request body is not a machine (at $.Scopes).' }
  }
  const read = values.map(readScope)
  const unreadable = read.indexOf(undefined)
  if (unreadable >= 0) {
    return { scopes: [], invalid: `The request body is not a machine (at $.Scopes[${unreadable}]).` }
  }
  const undefinedScope = read.find((scope) => typeof scope === 'number')
  if (undefinedScope !== undefined) {
    return { scopes: [], invalid: `The machine scope ${undefinedScope} doesn't exist.` }
  }
  const scopes = read as MachineScope[]
  const completed = [...scopes, ...scopes.flatMap((scope) => impliedReads[scope] ?? [])]
  return { scopes: machineScopes.filter((scope) => completed.includes(scope)) }
}

/**
 * The creator of a machine can use its credentials, so its creation, a reset and new federation rules need the role its
 * scopes need (424-79): an Administrator for a management scope, ContributorWithSecrets for a secrets scope; the route
 * checks the Contributor. With the message of the API.
 */
function requireRoleForScopes(db: MockDb, key: string, scopes?: MachineScope[]) {
  const role = scopes?.some((scope) => managementScopes.includes(scope))
    ? 'Administrator'
    : scopes?.some((scope) => secretsScopes.includes(scope))
      ? 'ContributorWithSecrets'
      : undefined
  return role && roleRank(roleOf(db, key)) < roleRank(role)
    ? errorResponse(403, `No ${role} access to '${key}'.`, 'AccessDenied')
    : undefined
}

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
  sharedCertificate = false,
): MachineAccess {
  // A Federated machine has no secret: an empty AccessKey, as for a certificate, and nothing to renew.
  if (kind === 'Federated') {
    return { ...machine, CredentialKind: kind, AccessKey: '' }
  }
  // A CertificateAndApiKey machine with a shared certificate gets only its API key.
  const certificate = (kind === 'Certificate' || kind === 'CertificateAndApiKey') && !sharedCertificate
  const lifetime = certificate
    ? (lifetimeDays ?? machine.CertificateLifetimeDays ?? deploymentLifetime.default)
    : undefined
  return {
    ...machine,
    CredentialKind: kind,
    UsesSharedCertificate: sharedCertificate || undefined,
    AccessKey: kind === 'ApiKey' || kind === 'CertificateAndApiKey' ? `st_${random(40).toLowerCase()}` : undefined,
    Certificate: certificate ? fakeSecret() : undefined,
    CertificatePassword: certificate ? random(16) : undefined,
    CertificateThumbprint: certificate ? random(40) : undefined,
    CertificateLifetimeDays: lifetime,
    CertificateNotAfter: lifetime ? new Date(Date.now() + lifetime * 86_400_000).toISOString() : undefined,
    TokenEndpoint: kind === 'ClientCredentials' ? 'https://auth.example.com/oauth2/token' : undefined,
    // As Entra ID answers: the .default scope of the API, whatever the scopes of the machine.
    TokenScope: kind === 'ClientCredentials' ? 'api://storyteller/.default' : undefined,
    LastRenewalAt: new Date().toISOString(),
  }
}

/**
 * The machine as the API returns it: a Federated machine gets the project's token endpoint, built from the request
 * (the API uses MachineAuth:Federation:PublicBaseUrl, or the request's scheme and host plus /api).
 */
function withTokenEndpoint(machine: MachineAccess, request: Request, org: string, project: string): MachineAccess {
  if (machine.CredentialKind !== 'Federated') {
    return machine
  }
  const base = request.url.slice(0, request.url.indexOf('/v1/'))
  return { ...machine, TokenEndpoint: `${base}/v1/${org}/${project}/access/token` }
}

const issuersOf = (db: MockDb, key: string) => db.issuers?.[key] ?? []

/** The trusted issuers in the order of the API (by name). */
const sortedIssuers = (issuers: FederatedIssuer[]) =>
  issuers.toSorted((left, right) => (left.Name ?? '').localeCompare(right.Name ?? ''))

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
    withDb((db, { params, request }) => {
      const key = machineScope(params)
      return (
        requireRole(db, key, 'Contributor') ??
        HttpResponse.json(
          (db.machines?.[key] ?? []).map((machine) =>
            withTokenEndpoint(stripSecrets(machine), request, params.org, params.project),
          ),
        )
      )
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
      const { scopes, invalid } = machineScopesOf(body)
      if (invalid) {
        return errorResponse(400, invalid)
      }
      // A missing list, an empty one and management scopes alone are refused, as in the API.
      if (scopes.every((scope) => managementScopes.includes(scope))) {
        return errorResponse(
          400,
          'A machine needs at least one runtime scope: AnnotationRead, AnnotationWrite, ConfigurationRead, ConfigurationWrite, SecretsRead or SecretsWrite.',
        )
      }
      const notAllowed = requireRoleForScopes(db, key, scopes)
      if (notAllowed) {
        return notAllowed
      }
      const policy = effectivePolicy(db.accessPoints[key]?.MachineAuthentication)
      const kind = body.CredentialKind ?? policy.CredentialKind!
      if (!policy.EnabledCredentialKinds!.includes(kind)) {
        return errorResponse(400, `The credential kind ${kind} is not enabled in the project ${key}.`)
      }
      if (body.UseSharedCertificate && kind !== 'CertificateAndApiKey') {
        return errorResponse(400, `Only CertificateAndApiKey machines can use a shared certificate, not ${kind}.`)
      }
      if (kind !== 'Federated' && body.FederationRules?.length) {
        return errorResponse(400, rulesOnOtherKindMessage, 'MachineNotFederated')
      }
      const invalidRules = kind === 'Federated' ? rulesError(body.FederationRules, issuersOf(db, key)) : undefined
      if (invalidRules) {
        return errorResponse(400, invalidRules, 'InvalidFederation')
      }
      const id = `mach-${random(6).toLowerCase()}`
      const machine = withCredentials(
        {
          Id: id,
          // A Federated machine exists only in Storyteller, like an API key machine in the API: ObjectId is its id.
          ObjectId: kind === 'Federated' ? id : `obj_${random(8)}`,
          Scopes: scopes,
          AnnotationKey: body.AnnotationKey,
          FederationRules: kind === 'Federated' ? body.FederationRules : undefined,
        },
        kind,
        body.CertificateLifetimeDays ?? policy.CertificateLifetimeDays,
        !!body.UseSharedCertificate,
      )
      ;((db.machines ??= {})[key] ??= []).push(stripSecrets(machine))
      return HttpResponse.json(withTokenEndpoint(machine, request, params.org, params.project), { status: 201 })
    }),
  ),

  http.get<{ org: string; project: string; id: string }>(
    `${api}/:org/:project/access/machines/:id`,
    withDb((db, { params, request }) => {
      const key = machineScope(params)
      const machine = db.machines?.[key]?.find((entry) => entry.Id === params.id)
      return (
        requireRole(db, key, 'Contributor') ??
        (machine
          ? HttpResponse.json(withTokenEndpoint(machine, request, params.org, params.project))
          : errorResponse(404, 'Not found.', 'NotFound'))
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
      // A new secret gives out the machine's scopes, so the reset needs the role they need.
      const notAllowed = requireRoleForScopes(db, key, list[index]!.Scopes)
      if (notAllowed) {
        return notAllowed
      }
      if (list[index]!.CredentialKind === 'Federated') {
        return errorResponse(400, resetNotSupportedMessage)
      }
      // A reset follows the kind the machine was created with, as the API does.
      const machine = withCredentials(
        list[index]!,
        list[index]!.CredentialKind ?? 'ApiKey',
        undefined,
        !!list[index]!.UsesSharedCertificate,
      )
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

  http.put<{ org: string; project: string; id: string }>(
    `${api}/:org/:project/access/machines/:id/federation-rules`,
    withDb(async (db, { params, request }) => {
      const key = machineScope(params)
      const machine = db.machines?.[key]?.find((entry) => entry.Id === params.id)
      // As in the API, the role comes before the body: the rules decide which workload gets the machine's tokens.
      const denied = requireRole(db, key, 'Contributor') ?? requireRoleForScopes(db, key, machine?.Scopes)
      if (denied) {
        return denied
      }
      const rules = (await request.json().catch(() => null)) as FederationRule[] | null
      const invalid = rulesError(rules, issuersOf(db, key))
      if (invalid) {
        return errorResponse(400, invalid, 'InvalidFederation')
      }
      if (!machine) {
        return errorResponse(404, 'Not found.', 'NotFound')
      }
      if (machine.CredentialKind !== 'Federated') {
        return errorResponse(400, rulesOnOtherKindMessage, 'MachineNotFederated')
      }
      machine.FederationRules = rules!
      return HttpResponse.json(withTokenEndpoint(stripSecrets(machine), request, params.org, params.project))
    }),
  ),

  http.get<{ org: string; project: string }>(
    `${api}/:org/:project/access/issuers`,
    withDb((db, { params }) => {
      const key = machineScope(params)
      return requireRole(db, key, 'Contributor') ?? HttpResponse.json(sortedIssuers(issuersOf(db, key)))
    }),
  ),

  http.get<{ org: string; project: string; name: string }>(
    `${api}/:org/:project/access/issuers/:name`,
    withDb((db, { params }) => {
      const key = machineScope(params)
      const issuer = issuersOf(db, key).find((entry) => entry.Name === params.name)
      // An unknown name is a 404 without a body, as in the API.
      return (
        requireRole(db, key, 'Contributor') ??
        (issuer ? HttpResponse.json(issuer) : new HttpResponse(null, { status: 404 }))
      )
    }),
  ),

  http.put<{ org: string; project: string; name: string }>(
    `${api}/:org/:project/access/issuers/:name`,
    withDb(async (db, { params, request }) => {
      const key = machineScope(params)
      const denied = requireRole(db, key, 'Administrator')
      if (denied) {
        return denied
      }
      // The route name wins over the body.
      const body = ((await request.json().catch(() => null)) ?? {}) as FederatedIssuer
      const requested: FederatedIssuer = { ...body, Name: params.name, KeySource: body.KeySource ?? 'Discovery' }
      const invalid = issuerError(requested)
      if (invalid) {
        return errorResponse(400, invalid, 'InvalidFederation')
      }
      const issuers = issuersOf(db, key)
      const existing = issuers.find((entry) => entry.Name === params.name)
      if (existing && existing.Issuer !== requested.Issuer) {
        return errorResponse(400, issuerImmutableMessage, 'InvalidFederation')
      }
      if (!existing) {
        // Rules left behind by a delete must not revive with another issuer of the same name.
        const machines = (db.machines?.[key] ?? [])
          .filter((machine) => machine.FederationRules?.some((rule) => rule.Issuer === params.name))
          .map((machine) => machine.Id)
          .slice(0, 10)
        if (machines.length) {
          const more = machines.length >= 10 ? ' and maybe more' : ''
          return errorResponse(
            409,
            `Federation rules still reference the trusted issuer '${params.name}' (machines ${machines.join(', ')}${more}); change those rules or delete the machines before creating it again.`,
            'FederatedIssuerInUse',
          )
        }
        if (issuers.length >= maxIssuersPerProject) {
          return errorResponse(400, tooManyIssuersMessage, 'InvalidFederation')
        }
      }
      const stored: FederatedIssuer = {
        Name: requested.Name,
        Issuer: requested.Issuer,
        KeySource: requested.KeySource,
        JwksUri: requested.JwksUri || undefined,
        Jwks: requested.Jwks || undefined,
        MaxTokenLifetimeSeconds: requested.MaxTokenLifetimeSeconds ?? undefined,
        RequiredClaims: Object.keys(requested.RequiredClaims ?? {}).length ? requested.RequiredClaims : undefined,
        Description: requested.Description || undefined,
      }
      ;(db.issuers ??= {})[key] = [...issuers.filter((entry) => entry.Name !== params.name), stored]
      return HttpResponse.json(stored)
    }),
  ),

  http.delete<{ org: string; project: string; name: string }>(
    `${api}/:org/:project/access/issuers/:name`,
    withDb((db, { params }) => {
      const key = machineScope(params)
      const denied = requireRole(db, key, 'Administrator')
      if (denied) {
        return denied
      }
      const issuers = issuersOf(db, key)
      if (!issuers.some((entry) => entry.Name === params.name)) {
        return new HttpResponse(null, { status: 404 })
      }
      // The kill switch: allowed while rules reference the issuer; those rules stop matching.
      ;(db.issuers ??= {})[key] = issuers.filter((entry) => entry.Name !== params.name)
      return new HttpResponse(null, { status: 200 })
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
      // Shared certificates follow CertificateAndApiKey, as in the API.
      if (
        !effectivePolicy(db.accessPoints[key]?.MachineAuthentication).EnabledCredentialKinds!.includes(
          'CertificateAndApiKey',
        )
      ) {
        return errorResponse(
          400,
          `The project ${key} does not enable CertificateAndApiKey, which shared certificates belong to.`,
        )
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

import type {
  AccessPoint,
  AccountRole,
  AnnotationAuditEntry,
  AnnotationDeletion,
  Invitation,
  MachineCredentialKind,
  View,
} from '@42for.net/2splatform.sdk'
import type { AnnotationOverrides, MockDb, MockUser } from '../db'
import { personActor } from './history'
import { people, type PersonaId } from './people'
import { showcaseOrganizations, showcaseProjects, showcaseScope } from './index'
import type { Members, ShowcaseProject } from './types'

// The MockDb of the showcase dataset: access points and the account from one table of roles, the registered views,
// the deletions the default views start with, and two pending invitations. Machines, issuers, certificates and the
// values store start empty, as in a new project.

const day = 86_400_000
/** Days a deleted annotation stays restorable (retentionDays of audit.ts, which this module must not import). */
const retentionDays = 90
const viewAges: Record<string, number> = {
  default: 300,
  development: 200,
  staging: 150,
  'release-candidate': 30,
  'feature-delivery1': 12,
}

const ago = (days: number, now: number) => new Date(now - days * day).toISOString()

function accessPoint(key: string, members: Members, kinds: MachineCredentialKind[]): AccessPoint {
  return {
    Key: key,
    AccessMap: Object.fromEntries(
      Object.entries(members).map(([persona, role]) => [people[persona as PersonaId].id, role as AccountRole]),
    ),
    MachineAuthentication: { EnabledCredentialKinds: kinds, CredentialKind: 'ApiKey', CertificateLifetimeDays: 90 },
  }
}

const firstOwner = (project: ShowcaseProject) =>
  (Object.entries(project.members).find(([, role]) => role === 'Owner')?.[0] ?? 'ada') as PersonaId

function viewsOf(project: ShowcaseProject, now: number): View[] {
  const by = personActor(firstOwner(project))
  return project.views.map((view) => ({
    Name: view.name,
    Description: view.description,
    IsDefault: view.name === 'default',
    IsRegistered: true,
    Created: { At: ago(viewAges[view.name] ?? 45, now), By: by },
  }))
}

/** The deletions of the project's default view, in the shapes softDelete writes. */
function seededDeletions(project: ShowcaseProject): AnnotationOverrides | undefined {
  const trash = showcaseScope(project.org, project.project, 'default')?.trash ?? []
  if (trash.length === 0) {
    return undefined
  }

  const overrides: Required<AnnotationOverrides> = { upserts: {}, deleted: [], trash: {}, audit: {} }
  trash.forEach((tree, index) => {
    const deletedAt = Date.parse(tree.deletedAt)
    const descendants = tree.annotations.length - 1
    const deletion: AnnotationDeletion = {
      DeletionId: `del_seed_${project.org}_${project.project}_${index + 1}`,
      RootKey: tree.rootKey,
      DeletedAt: tree.deletedAt,
      DeletedBy: personActor(tree.by),
      IsRoot: false,
      DescendantCount: null,
      PurgeAt: new Date(deletedAt + retentionDays * day).toISOString(),
    }

    for (const annotation of tree.annotations) {
      const key = annotation.AnnotationKey
      const isRoot = key === tree.rootKey
      overrides.trash[key] = {
        ...annotation,
        Deletion: { ...deletion, IsRoot: isRoot, DescendantCount: isRoot ? descendants : null },
      }
      overrides.deleted.push(key)
      const entries: AnnotationAuditEntry[] = [
        { Id: `adt_seed_created_${key}`, At: annotation.Created?.At, Action: 'Created', Actor: annotation.Created?.By },
        {
          Id: `adt_seed_deleted_${key}`,
          At: tree.deletedAt,
          Action: 'Deleted',
          Actor: personActor(tree.by),
          DeletionId: deletion.DeletionId,
          Cause: isRoot ? undefined : { Kind: 'Cascade', Key: tree.rootKey },
          DescendantCount: isRoot ? descendants : undefined,
        },
      ]
      overrides.audit[key] = entries
    }
  })
  return overrides
}

function invitation(
  id: string,
  accessPointKey: string,
  email: string,
  role: AccountRole,
  by: PersonaId,
  daysAgo: number,
  now: number,
): Invitation {
  return {
    Id: id,
    AccessPointKey: accessPointKey,
    Email: email,
    Role: role,
    Status: 'Pending',
    InvitedById: people[by].id,
    InvitedByName: people[by].name,
    CreatedAt: ago(daysAgo, now),
    ExpiresAt: ago(daysAgo - 7, now),
    IsEmailSent: true,
  }
}

export function createShowcaseSeed(user: MockUser): MockDb {
  const now = Date.now()
  const accessPoints: Record<string, AccessPoint> = {}
  for (const organization of showcaseOrganizations) {
    accessPoints[organization.org] = accessPoint(organization.org, organization.members, ['ApiKey', 'Federated'])
  }
  for (const project of showcaseProjects) {
    const key = `${project.org}.${project.project}`
    const kinds: MachineCredentialKind[] = ['fitness-cloud', 'streaming-platform'].includes(project.project)
      ? ['ApiKey', 'Certificate', 'Federated']
      : ['ApiKey', 'Federated']
    accessPoints[key] = accessPoint(key, project.members, kinds)
  }

  // The account's map is derived from the access points, as syncAccount keeps it (access-handlers.ts).
  const accessMap = Object.fromEntries(
    Object.entries(accessPoints).flatMap(([key, point]) => {
      const role = point.AccessMap?.[user.id]
      return role ? [[key, role]] : []
    }),
  )

  const annotations: Record<string, AnnotationOverrides> = {}
  for (const project of showcaseProjects) {
    const overrides = seededDeletions(project)
    if (overrides) {
      annotations[`${project.org}/${project.project}/default`] = overrides
    }
  }

  return {
    scenario: 'showcase',
    user,
    account: { Id: user.id, UserName: user.email, Name: user.name, AccessMap: accessMap },
    accessPoints,
    views: Object.fromEntries(
      showcaseProjects.map((project) => [`${project.org}.${project.project}`, viewsOf(project, now)]),
    ),
    annotations,
    invitations: [
      invitation(
        'inv_01SHOWFITNESS',
        'fabrikam.fitness-cloud',
        'support@fitfactory.example',
        'Reader',
        'margaret',
        2,
        now,
      ),
      invitation(
        'inv_01SHOWHOTELS',
        'seabreeze-hotels.guest-platform',
        'revenue@seabreeze.example',
        'Contributor',
        'grace',
        4,
        now,
      ),
    ],
  }
}

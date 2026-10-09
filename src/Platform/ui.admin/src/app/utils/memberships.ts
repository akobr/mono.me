import { roleRank, type AccountRoleName } from '@42for.net/2splatform.sdk'

export interface ProjectMembership {
  org: string
  name: string
  key: string
  role: AccountRoleName
}

export interface OrganizationMembership {
  name: string
  /** Role on the organization access point; undefined when the user is a member of some of its projects only. */
  role?: AccountRoleName
  projects: ProjectMembership[]
}

const asRole = (value: string | undefined): AccountRoleName =>
  roleRank(value) > 0 ? (value as AccountRoleName) : 'None'

/**
 * Groups Account.AccessMap keys: a key without a dot is an organization, "{org}.{project}" a project.
 * An organization role does not grant access to its projects, so both lists come from the map only.
 */
export function groupMemberships(accessMap: Record<string, string> | undefined | null): OrganizationMembership[] {
  const organizations = new Map<string, OrganizationMembership>()
  const organization = (name: string) => {
    let entry = organizations.get(name)
    if (!entry) {
      entry = { name, projects: [] }
      organizations.set(name, entry)
    }
    return entry
  }

  for (const [key, role] of Object.entries(accessMap ?? {})) {
    const dot = key.indexOf('.')

    if (dot < 0) {
      organization(key).role = asRole(role)
    } else {
      const org = key.slice(0, dot)
      organization(org).projects.push({ org, name: key.slice(dot + 1), key, role: asRole(role) })
    }
  }

  return [...organizations.values()]
    .map((entry) => ({ ...entry, projects: entry.projects.toSorted((a, b) => a.name.localeCompare(b.name)) }))
    .toSorted((a, b) => a.name.localeCompare(b.name))
}

/** True when the user has any membership in the organization: its access point or one of its projects. */
export function hasOrganization(accessMap: Record<string, string> | undefined | null, org: string): boolean {
  return Object.keys(accessMap ?? {}).some((key) => key === org || key.startsWith(`${org}.`))
}

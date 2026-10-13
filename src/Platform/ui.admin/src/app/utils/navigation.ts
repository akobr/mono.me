import { hasRole, roleRank, type AccountRoleName } from '@42for.net/2splatform.sdk'
import type { Scope } from '@/stores/context'
import { hasOrganization } from '@/utils/memberships'

export interface NavigationEntry {
  label: string
  icon: string
  to: string
  /** Active only on this exact path (not on the pages below it). */
  exact?: boolean
  /** Hidden below this role. */
  minRole?: AccountRoleName
  /** Shown with a lock icon below this role (read-only for the caller). */
  editRole?: AccountRoleName
}

export interface NavigationSection {
  id: 'view' | 'project' | 'org' | 'global'
  label?: string
  entries: NavigationEntry[]
}

export interface NavigationContext {
  org?: string
  project?: string
  view?: string
  projectRole: AccountRoleName
  orgRole: AccountRoleName
}

export interface NavigationItem extends NavigationEntry {
  locked: boolean
}

/**
 * The current context completed by the remembered one: its project while the organization is the same, and its view
 * while the project is the same too. Outside organizations, the remembered context stays as it is.
 */
export function mergeContext(current: Partial<Scope>, remembered: Partial<Scope> | undefined): Partial<Scope> {
  if (!current.org) {
    return { ...remembered }
  }

  const sameOrg = remembered?.org === current.org
  const project = current.project ?? (sameOrg ? remembered?.project : undefined)
  const view = current.view ?? (sameOrg && remembered?.project === project ? remembered?.view : undefined)
  return { org: current.org, project, view }
}

/** The remembered context without the organization or project the caller is no longer a member of. */
export function accessibleContext(
  remembered: Partial<Scope> | undefined,
  accessMap: Record<string, string> | undefined | null,
): Partial<Scope> | undefined {
  if (!remembered?.org || !hasOrganization(accessMap, remembered.org)) {
    return undefined
  }

  return remembered.project && roleRank(accessMap?.[`${remembered.org}.${remembered.project}`]) > 0
    ? { org: remembered.org, project: remembered.project, view: remembered.view }
    : { org: remembered.org }
}

/**
 * The context the navigation shows (424-17): the route's context completed by the remembered one, so a page of a
 * less specific context keeps the sections of the project and view the user worked in. The route's own parts are
 * always kept; a project without a known view shows its default view.
 */
export function navigationScope(
  current: Partial<Scope>,
  remembered: Partial<Scope> | undefined,
  accessMap: Record<string, string> | undefined | null,
): Partial<Scope> {
  const scope = mergeContext(current, accessibleContext(remembered, accessMap))
  return scope.project ? { ...scope, view: scope.view ?? 'default' } : scope
}

/**
 * Sidebar sections (spec section 4): view scope, project scope and organization scope, plus global entries.
 * Entries the caller's role cannot use are hidden; read-only ones are marked as locked.
 */
export function buildNavigation(
  context: NavigationContext,
): { id: NavigationSection['id']; label?: string; items: NavigationItem[] }[] {
  const sections: NavigationSection[] = []
  const { org, project, view } = context

  if (org && project && view) {
    const base = `/${org}/${project}/${view}`
    sections.push({
      id: 'view',
      label: `View · ${view}`,
      entries: [
        { label: 'Annotations', icon: 'i-lucide-tags', to: `${base}/annotations`, editRole: 'Contributor' },
        { label: 'Configurations', icon: 'i-lucide-settings-2', to: `${base}/configurations`, editRole: 'Contributor' },
        { label: 'Graph', icon: 'i-lucide-waypoints', to: `${base}/graph` },
        { label: 'Templates', icon: 'i-lucide-layout-template', to: `${base}/templates`, editRole: 'Contributor' },
        { label: 'Schemas', icon: 'i-lucide-braces', to: `${base}/schemas`, editRole: 'Contributor' },
        { label: 'Compare views', icon: 'i-lucide-git-compare', to: `${base}/compare` },
        { label: 'Deleted', icon: 'i-lucide-archive-restore', to: `${base}/deleted`, editRole: 'Contributor' },
      ],
    })
  }

  if (org && project) {
    const base = `/${org}/${project}`
    sections.push({
      id: 'project',
      label: 'Project',
      entries: [
        { label: 'Views', icon: 'i-lucide-eye', to: `${base}/views` },
        { label: 'Members', icon: 'i-lucide-users', to: `${base}/members`, minRole: 'Administrator' },
        { label: 'Invitations', icon: 'i-lucide-mail', to: `${base}/invitations`, minRole: 'Administrator' },
        { label: 'Machines', icon: 'i-lucide-computer', to: `${base}/machines`, minRole: 'Contributor' },
        { label: 'Certificates', icon: 'i-lucide-shield-check', to: `${base}/certificates`, minRole: 'Administrator' },
        { label: 'Settings', icon: 'i-lucide-settings', to: `${base}/settings` },
      ],
    })
  }

  if (org) {
    sections.push({
      id: 'org',
      label: 'Organization',
      entries: [
        { label: 'Overview', icon: 'i-lucide-building-2', to: `/${org}`, exact: true },
        { label: 'Members', icon: 'i-lucide-users', to: `/${org}/members`, minRole: 'Administrator' },
      ],
    })
  }

  sections.push({
    id: 'global',
    entries: [
      { label: 'Organizations', icon: 'i-lucide-layout-grid', to: '/orgs' },
      { label: 'Account', icon: 'i-lucide-user-round', to: '/account' },
    ],
  })

  return sections
    .map((section) => {
      const role = section.id === 'org' ? context.orgRole : context.projectRole
      return {
        id: section.id,
        label: section.label,
        items: section.entries
          .filter((entry) => !entry.minRole || hasRole(role, entry.minRole))
          .map((entry) => ({ ...entry, locked: !!entry.editRole && !hasRole(role, entry.editRole) })),
      }
    })
    .filter((section) => section.items.length > 0)
}

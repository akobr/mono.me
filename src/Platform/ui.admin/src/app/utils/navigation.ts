import { hasRole, type AccountRoleName } from '@42for.net/2splatform.sdk'

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
        { label: 'Machines', icon: 'i-lucide-cpu', to: `${base}/machines`, minRole: 'Contributor' },
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

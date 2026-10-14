import type { Scope } from '@/stores/context'

/** Sections that live under /{org}/{project}/{view}/… */
export const viewSections = ['annotations', 'configurations', 'graph', 'templates', 'schemas', 'compare'] as const

/** Sections that live under /{org}/{project}/… */
export const projectSections = ['views', 'members', 'invitations', 'machines', 'certificates', 'settings'] as const

export type ViewSection = (typeof viewSections)[number]
export type ProjectSection = (typeof projectSections)[number]

export type CurrentSection =
  | { kind: 'view'; name: ViewSection }
  | { kind: 'project'; name: ProjectSection }
  | { kind: 'org'; name?: string }
  | { kind: 'none' }

/** Whether two scopes are the same view of the same project. */
export const sameView = (a: Scope, b: Scope) => a.org === b.org && a.project === b.project && a.view === b.view

const isViewSection = (value: string | undefined): value is ViewSection =>
  (viewSections as readonly string[]).includes(value ?? '')
const isProjectSection = (value: string | undefined): value is ProjectSection =>
  (projectSections as readonly string[]).includes(value ?? '')

/** Which section a path shows, so the context switcher can keep it when the organization, project or view changes. */
export function currentSection(path: string): CurrentSection {
  const segments = path.split('?')[0]!.split('/').filter(Boolean)

  if (segments.length === 0 || ['orgs', 'account', 'onboarding', 'invitations', 'login'].includes(segments[0]!)) {
    return { kind: 'none' }
  }

  if (isProjectSection(segments[2])) {
    return { kind: 'project', name: segments[2] }
  }

  if (segments.length >= 4 && isViewSection(segments[3])) {
    return { kind: 'view', name: segments[3] }
  }

  return { kind: 'org', name: segments.length === 2 ? segments[1] : undefined }
}

export interface ViewLocation {
  scope: Scope
  section: ViewSection
  /** The section's list itself, without a selected detail. */
  root: boolean
}

/** The view and section of a path under /{org}/{project}/{view}/{section}/…, or undefined for any other path. */
export function viewLocation(path: string): ViewLocation | undefined {
  const section = currentSection(path)
  if (section.kind !== 'view') {
    return undefined
  }
  const [org, project, view, , ...rest] = path.split('?')[0]!.split('/').filter(Boolean)
  return { scope: { org: org!, project: project!, view: view! }, section: section.name, root: rest.length === 0 }
}

/** Target of switching to another project: the same section, in the default view; the detail (key) is dropped. */
export function projectTarget(path: string, org: string, project: string): string {
  const section = currentSection(path)

  switch (section.kind) {
    case 'view':
      return `/${org}/${project}/default/${section.name}`
    case 'project':
      return `/${org}/${project}/${section.name}`
    default:
      return `/${org}/${project}/default/annotations`
  }
}

/** Target of switching the view: the same view section without the selected detail. */
export function viewTarget(path: string, org: string, project: string, view: string): string {
  const section = currentSection(path)
  return `/${org}/${project}/${view}/${section.kind === 'view' ? section.name : 'annotations'}`
}

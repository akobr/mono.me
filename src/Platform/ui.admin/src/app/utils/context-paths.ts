import type { Scope } from '@/stores/context'
import { modelingTab } from '@/utils/modeling'

/**
 * Sections that live under /{org}/{project}/{view}/…, in the order of the menu; Values store is `key-values` (424-68).
 * The old schemas, templates, compare and export-import pages are tabs of modeling now (424-38); their URLs only
 * redirect, so they are never the current path.
 */
export const viewSections = ['annotations', 'configurations', 'key-values', 'graph', 'modeling'] as const

/** Sections that live under /{org}/{project}/… */
export const projectSections = ['views', 'members', 'machines', 'settings', 'monitor'] as const

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

/**
 * The project sections with pages below them (`machines/{id}`). The others are single pages: `monitor` is not a
 * reserved view name (424-35), so /{org}/{project}/monitor/graph is the graph of a view named monitor.
 */
const nestedProjectSections: readonly ProjectSection[] = ['machines']

/** Which section a path shows, so the context switcher can keep it when the organization, project or view changes. */
export function currentSection(path: string): CurrentSection {
  const segments = path.split('?')[0]!.split('/').filter(Boolean)

  if (segments.length === 0 || ['orgs', 'account', 'onboarding', 'invitations', 'login'].includes(segments[0]!)) {
    return { kind: 'none' }
  }

  if (isProjectSection(segments[2]) && (segments.length === 3 || nestedProjectSections.includes(segments[2]))) {
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

/** A view section as the path below the view: on Modeling, with its tab (424-38). */
function viewSectionPath(path: string, section: ViewSection): string {
  const tab = section === 'modeling' ? modelingTab(path) : undefined
  return tab ? `${section}/${tab}` : section
}

/** Target of switching to another project: the same section, in the default view; the detail (key) is dropped. */
export function projectTarget(path: string, org: string, project: string): string {
  const section = currentSection(path)

  switch (section.kind) {
    case 'view':
      return `/${org}/${project}/default/${viewSectionPath(path, section.name)}`
    case 'project':
      return `/${org}/${project}/${section.name}`
    default:
      return `/${org}/${project}/default/annotations`
  }
}

/** Target of switching the view: the same view section (and Modeling tab) without the selected detail. */
export function viewTarget(path: string, org: string, project: string, view: string): string {
  const section = currentSection(path)
  return `/${org}/${project}/${view}/${section.kind === 'view' ? viewSectionPath(path, section.name) : 'annotations'}`
}

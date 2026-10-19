import { hasRole, type AccountRoleName, type View } from '@42for.net/2splatform.sdk'
import type { Scope } from '@/stores/context'

interface ModelingTabEntry {
  label: string
  value: string
  icon: string
  /** Hidden below this role (424-66); the other tabs follow the lock of the whole page. */
  minRole?: AccountRoleName
}

/** The tabs of the Modeling page of a view (424-38), in their order; each is a page below `modeling/`. */
export const modelingTabs = [
  { label: 'Schemas', value: 'schemas', icon: 'i-lucide-braces' },
  { label: 'Templates', value: 'templates', icon: 'i-lucide-layout-template' },
  { label: 'Export & Import', value: 'export-import', icon: 'i-lucide-arrow-down-up', minRole: 'Administrator' },
  { label: 'Deleted', value: 'deleted', icon: 'i-lucide-archive-restore' },
  { label: 'Compare views', value: 'compare', icon: 'i-lucide-git-compare' },
] as const satisfies readonly ModelingTabEntry[]

export type ModelingTab = (typeof modelingTabs)[number]['value']

const isModelingTab = (value: string | undefined): value is ModelingTab =>
  modelingTabs.some((tab) => tab.value === value)

/** The tabs the role sees, in their order: Export & Import only from Administrator up (424-66). */
export function modelingTabsFor(role: AccountRoleName) {
  return modelingTabs.filter((tab: ModelingTabEntry) => !tab.minRole || hasRole(role, tab.minRole))
}

/** /{org}/{project}/{view}/modeling, or one of its tabs. */
export function modelingPath(scope: Scope, tab?: ModelingTab): string {
  const base = `/${scope.org}/${scope.project}/${scope.view}/modeling`
  return tab ? `${base}/${tab}` : base
}

/** The tab of a path under /{org}/{project}/{view}/modeling/{tab}/…, or undefined for any other path. */
export function modelingTab(path: string): ModelingTab | undefined {
  const segments = path.split(/[?#]/)[0]!.split('/').filter(Boolean)
  return segments[3] === 'modeling' && isModelingTab(segments[4]) ? segments[4] : undefined
}

/**
 * An old path of a view page (/{org}/{project}/{view}/schemas/…) moved below modeling/ (424-38). It keeps the rest
 * of the path as it is, encoded; the redirect stubs return it as a string, so the router keeps the query and the hash.
 */
export function movedToModeling(path: string): string {
  const segments = path.split('/')
  return [...segments.slice(0, 4), 'modeling', ...segments.slice(4)].join('/')
}

/** Whether the project has a view other than the current one, the views Compare views offers. */
export function hasOtherView(views: View[] | undefined, current: string | undefined): boolean {
  return (views ?? []).some((view) => !!view.Name && view.Name !== current)
}

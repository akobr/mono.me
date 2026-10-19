import { describe, expect, it } from 'vitest'
import { buildNavigation, type NavigationContext } from '@/utils/navigation'
import { typeMeta } from '@/utils/annotation-key'
import { shortcutGroups } from '@/composables/useShortcutsHelp'

// Templates, Schemas, Export & Import, Deleted and Compare views are the tabs of one Modeling entry (424-38).
describe('Modeling entry', () => {
  const sections = (projectRole: NavigationContext['projectRole']) =>
    buildNavigation({ org: 'acme', project: 'billing', view: 'staging', projectRole, orgRole: 'None' })
  const viewItems = (projectRole: NavigationContext['projectRole']) => sections(projectRole)[0]!.items
  const modeling = (projectRole: NavigationContext['projectRole']) =>
    viewItems(projectRole).find((item) => item.label === 'Modeling')

  it.each(['Owner', 'Contributor', 'Reader'] as const)('ends the view section of %s with Modeling', (role) => {
    expect(viewItems(role).map((item) => item.label)).toEqual([
      'Annotations',
      'Configurations',
      'Values store',
      'Graph',
      'Modeling',
    ])
  })

  it('opens the Modeling page with the puzzle icon and lists the tabs of a Contributor in order', () => {
    const base = '/acme/billing/staging/modeling'

    expect(modeling('Contributor')).toEqual({
      label: 'Modeling',
      icon: 'i-lucide-puzzle',
      to: base,
      editRole: 'Contributor',
      lockedHint: 'Contributors only',
      tabs: [
        { label: 'Schemas', icon: 'i-lucide-braces', to: `${base}/schemas` },
        { label: 'Templates', icon: 'i-lucide-layout-template', to: `${base}/templates` },
        { label: 'Deleted', icon: 'i-lucide-archive-restore', to: `${base}/deleted` },
        { label: 'Compare views', icon: 'i-lucide-git-compare', to: `${base}/compare` },
      ],
      locked: false,
    })
  })

  // Export & Import needs the Administrator role (424-66), so the command palette lists it from Administrator up.
  it.each([
    ['Contributor', ['Schemas', 'Templates', 'Deleted', 'Compare views']],
    ['ContributorWithSecrets', ['Schemas', 'Templates', 'Deleted', 'Compare views']],
    ['Administrator', ['Schemas', 'Templates', 'Export & Import', 'Deleted', 'Compare views']],
    ['Owner', ['Schemas', 'Templates', 'Export & Import', 'Deleted', 'Compare views']],
  ] as const)('lists the tabs of Modeling that %s sees', (role, tabs) => {
    expect(modeling(role)?.tabs?.map((tab) => tab.label)).toEqual(tabs)
  })

  it('links Export & Import to its tab for an Administrator', () => {
    expect(modeling('Administrator')?.tabs).toContainEqual({
      label: 'Export & Import',
      icon: 'i-lucide-arrow-down-up',
      to: '/acme/billing/staging/modeling/export-import',
    })
  })

  it('is shown to a reader, locked, because the whole page is closed below Contributor', () => {
    expect(modeling('Reader')).toMatchObject({ locked: true, lockedHint: 'Contributors only' })
    expect(modeling('Owner')).toMatchObject({ locked: false })
  })

  it.each(['Owner', 'Contributor', 'Reader'] as const)('has no entry for a page that became a tab, for %s', (role) => {
    const paths = sections(role).flatMap((section) => section.items.map((item) => item.to))

    for (const moved of ['templates', 'schemas', 'compare', 'export-import', 'deleted']) {
      expect(paths.filter((path) => path.startsWith(`/acme/billing/staging/${moved}`))).toEqual([])
    }
  })

  it('keeps the shortcuts of the tabs, with unique keys', () => {
    const goTo = shortcutGroups.find((group) => group.title === 'Go to')!.shortcuts
    // g t, g s and g e open the tabs, which keep the names of the pages
    expect(goTo).toContainEqual({ keys: ['G', 'T'], description: 'Templates' })
    expect(goTo).toContainEqual({ keys: ['G', 'S'], description: 'Schemas' })
    expect(goTo).toContainEqual({ keys: ['G', 'E'], description: 'Export & Import' })
    // no other shortcut uses the same keys
    const keys = shortcutGroups.flatMap((group) => group.shortcuts.map((shortcut) => shortcut.keys.join('+')))
    expect(new Set(keys).size).toBe(keys.length)
  })
})

const entries = () =>
  buildNavigation({
    org: 'acme',
    project: 'billing',
    view: 'default',
    projectRole: 'Owner',
    orgRole: 'Owner',
  }).flatMap((section) => section.items)

describe('navigation icons', () => {
  it('does not use the icon of an annotation type for an entry', () => {
    const items = entries()
    const typeIcons = Object.values(typeMeta).map((meta) => meta.icon)

    expect(items.find((item) => item.label === 'Machines')).toMatchObject({ icon: 'i-lucide-computer' })
    expect(items.filter((item) => typeIcons.includes(item.icon))).toEqual([])
  })
})

describe('values store', () => {
  it('names the key values of a view Values store, with the vault icon', () => {
    const items = entries()

    expect(items.find((item) => item.label === 'Values store')).toMatchObject({
      icon: 'i-lucide-vault',
      to: '/acme/billing/default/key-values',
      editRole: 'Contributor',
    })
    expect(items.find((item) => item.label === 'Key values')).toBeUndefined()
  })
})

describe('project section', () => {
  const projectEntries = (projectRole: 'Owner' | 'Administrator' | 'Contributor' | 'Reader') =>
    buildNavigation({ org: 'acme', project: 'billing', view: 'default', projectRole, orgRole: 'None' })
      .find((section) => section.id === 'project')!
      .items.map((item) => item.label)

  it.each([
    ['Owner', ['Views', 'Members', 'Machines', 'Settings', 'Monitor']],
    ['Administrator', ['Views', 'Members', 'Machines', 'Settings', 'Monitor']],
    ['Contributor', ['Views', 'Machines', 'Settings']],
    ['Reader', ['Views', 'Settings']],
  ] as const)('shows %s the merged pages only', (role, labels) => {
    expect(projectEntries(role)).toEqual(labels)
  })

  it('combines Usage and Audit into Monitor, with the activity icon', () => {
    expect(entries().find((item) => item.label === 'Monitor')).toMatchObject({
      icon: 'i-lucide-activity',
      to: '/acme/billing/monitor',
      minRole: 'Administrator',
    })
  })

  it('has no entry for a page that another one took over', () => {
    const paths = entries().map((item) => item.to.split('/').at(-1))

    for (const moved of ['invitations', 'certificates', 'secrets', 'audit', 'usage']) {
      expect(paths).not.toContain(moved)
    }
  })
})

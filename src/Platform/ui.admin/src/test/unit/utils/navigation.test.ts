import { describe, expect, it } from 'vitest'
import { buildNavigation, type NavigationContext } from '@/utils/navigation'
import { typeMeta } from '@/utils/annotation-key'
import { currentSection, viewTarget } from '@/utils/context-paths'
import { shortcutGroups } from '@/composables/useShortcutsHelp'

describe('Export & Import entry', () => {
  const viewItems = (projectRole: NavigationContext['projectRole']) =>
    buildNavigation({ org: 'acme', project: 'billing', view: 'staging', projectRole, orgRole: 'None' })[0]!.items

  it('follows Compare views in the view section, with its own icon', () => {
    const items = viewItems('Contributor')
    const index = items.findIndex((item) => item.label === 'Export & Import')

    expect(items[index - 1]?.label).toBe('Compare views')
    expect(items[index]).toEqual({
      label: 'Export & Import',
      icon: 'i-lucide-arrow-down-up',
      to: '/acme/billing/staging/export-import',
      editRole: 'Contributor',
      locked: false,
    })
  })

  it('is shown to a reader, locked, because a reader can still export', () => {
    expect(viewItems('Reader').find((item) => item.label === 'Export & Import')).toMatchObject({ locked: true })
  })

  it('is a section of the view, kept when the view changes, and listed with its shortcut', () => {
    expect(currentSection('/acme/billing/default/export-import')).toEqual({ kind: 'view', name: 'export-import' })
    expect(viewTarget('/acme/billing/default/export-import', 'acme', 'billing', 'staging')).toBe(
      '/acme/billing/staging/export-import',
    )
    const goTo = shortcutGroups.find((group) => group.title === 'Go to')!.shortcuts
    expect(goTo).toContainEqual({ keys: ['G', 'E'], description: 'Export & Import' })
    // no other shortcut uses the same keys
    const keys = shortcutGroups.flatMap((group) => group.shortcuts.map((shortcut) => shortcut.keys.join('+')))
    expect(new Set(keys).size).toBe(keys.length)
  })
})

describe('navigation icons', () => {
  it('does not use the icon of an annotation type for an entry', () => {
    const items = buildNavigation({
      org: 'acme',
      project: 'billing',
      view: 'default',
      projectRole: 'Owner',
      orgRole: 'Owner',
    }).flatMap((section) => section.items)
    const typeIcons = Object.values(typeMeta).map((meta) => meta.icon)

    expect(items.find((item) => item.label === 'Machines')).toMatchObject({ icon: 'i-lucide-computer' })
    expect(items.filter((item) => typeIcons.includes(item.icon))).toEqual([])
  })
})

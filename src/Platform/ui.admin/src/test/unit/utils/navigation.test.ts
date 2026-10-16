import { describe, expect, it } from 'vitest'
import { buildNavigation } from '@/utils/navigation'
import { typeMeta } from '@/utils/annotation-key'

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

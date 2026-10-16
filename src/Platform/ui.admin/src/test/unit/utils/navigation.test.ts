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

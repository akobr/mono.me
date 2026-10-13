import { describe, expect, it } from 'vitest'
import { buildNavigation } from '@/utils/navigation'
import { typeMeta } from '@/utils/annotation-key'

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

import { describe, expect, it } from 'vitest'
import { defaultViewBadge, defaultViewName, isDefaultView } from '@/utils/views'

// The default view is the main (production) view of a project; the API names it "default" (424-43).
describe('default view', () => {
  it('takes only the view named default for the default view', () => {
    expect(defaultViewName).toBe('default')
    expect(isDefaultView('default')).toBe(true)
  })

  it.each(['staging', 'Default', 'DEFAULT', 'prod', 'production', '', null, undefined])(
    'does not take %s for the default view',
    (view) => {
      expect(isDefaultView(view)).toBe(false)
    },
  )

  it('marks the default view with a prod badge in the accent', () => {
    expect(defaultViewBadge).toEqual({ label: 'prod', color: 'primary', variant: 'subtle', size: 'sm' })
  })
})

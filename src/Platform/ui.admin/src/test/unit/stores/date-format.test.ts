import { afterEach, describe, expect, it } from 'vitest'
import { nextTick } from 'vue'
import { applyDateFormat, dateFormat, defaultDateFormat, formatDate } from '@/utils/dates'
import { useContextStore } from '@/stores/context'
import { useDateFormatStore } from '@/stores/date-format'
import { usePreferencesStore } from '@/stores/preferences'
import { useProjectSettingsStore } from '@/stores/project-settings'

const instant = new Date('2026-10-03T15:04:00Z')

describe('date format store (424-23)', () => {
  afterEach(() => {
    // The format in effect is shared by the whole module graph: stop this test's store and put the default back.
    useDateFormatStore().$dispose()
    applyDateFormat(defaultDateFormat)
  })

  it('shows yyyy-mm-dd without settings', async () => {
    const store = useDateFormatStore()
    await nextTick()

    expect(store.effective).toBe('yyyy-mm-dd')
    expect(formatDate(instant, 'UTC')).toBe('2026-10-03')
  })

  it('applies the format of the project of the context', async () => {
    const context = useContextStore()
    useProjectSettingsStore().update('acme.billing', { dateFormat: 'dd.mm.yyyy' })
    useDateFormatStore()

    context.set({ org: 'acme', project: 'billing', view: 'default' })
    await nextTick()
    expect(formatDate(instant, 'UTC')).toBe('03.10.2026')

    context.set({ org: 'acme', project: 'shipping', view: 'default' })
    await nextTick()
    expect(dateFormat.value).toBe('yyyy-mm-dd')
  })

  it('lets the personal format win until Preferences are reset', async () => {
    const context = useContextStore()
    useProjectSettingsStore().update('acme.billing', { dateFormat: 'dd.mm.yyyy' })
    const preferences = usePreferencesStore()
    preferences.setDateFormat('mm/dd/yyyy')
    const store = useDateFormatStore()

    context.set({ org: 'acme', project: 'billing', view: 'default' })
    await nextTick()

    expect(store.project).toBe('dd.mm.yyyy')
    expect(store.personal).toBe('mm/dd/yyyy')
    expect(formatDate(instant, 'UTC')).toBe('10/03/2026')

    preferences.setDateFormat(undefined)
    await nextTick()
    expect(formatDate(instant, 'UTC')).toBe('03.10.2026')

    preferences.setDateFormat('d mmm yyyy')
    preferences.reset()
    await nextTick()
    expect(preferences.state.dateFormat).toBeUndefined()
    expect(formatDate(instant, 'UTC')).toBe('03.10.2026')
  })

  it('ignores a personal format it does not know', () => {
    localStorage.setItem('2s-admin:preferences', JSON.stringify({ dateFormat: 'yy.m.d' }))
    const preferences = usePreferencesStore()

    expect(preferences.dateFormat).toBeUndefined()

    preferences.setDateFormat('dd/mm/yyyy')
    preferences.setDateFormat('nope' as never)
    expect(preferences.dateFormat).toBe('dd/mm/yyyy')
  })
})

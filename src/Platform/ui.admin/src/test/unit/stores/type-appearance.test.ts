import { afterEach, describe, expect, it } from 'vitest'
import { nextTick } from 'vue'
import { metaOf, typeMeta } from '@/utils/annotation-key'
import { applyTypeAppearance } from '@/utils/type-appearance'
import { useContextStore } from '@/stores/context'
import { usePreferencesStore } from '@/stores/preferences'
import { useProjectSettingsStore } from '@/stores/project-settings'
import { typeColorsStyleId, useTypeAppearanceStore } from '@/stores/type-appearance'

const styleText = () => document.getElementById(typeColorsStyleId)?.textContent ?? ''

describe('type appearance store', () => {
  afterEach(() => {
    // typeMeta is shared by the whole module graph: stop this test's store, so it cannot write into the next test,
    // and put the built-in values back.
    useTypeAppearanceStore().$dispose()
    applyTypeAppearance({})
    document.getElementById(typeColorsStyleId)?.remove()
  })

  it('shows the built-in names, icons and colors without overrides', async () => {
    const appearance = useTypeAppearanceStore()
    await nextTick()

    expect(appearance.effective).toEqual({})
    expect(typeMeta.exe.label).toBe('Execution')
    expect(typeMeta.exe.icon).toBe('i-lucide-play')
    expect(styleText()).toBe('')
  })

  it('applies the project settings of the context to typeMeta and the color tokens', async () => {
    const context = useContextStore()
    const projectSettings = useProjectSettingsStore()
    projectSettings.receive('acme.billing', {
      Types: { exe: { Label: 'Job', Color: 'red', Icon: 'i-lucide-rocket' } },
      Version: 1,
    })
    useTypeAppearanceStore()

    context.set({ org: 'acme', project: 'billing', view: 'default' })
    await nextTick()

    expect(metaOf('Execution')).toMatchObject({ label: 'Job', plural: 'Jobs', icon: 'i-lucide-rocket' })
    expect(metaOf('exe')?.code).toBe('exe')
    expect(styleText()).toContain('--st-exe: #b91c1c;')

    context.set({ org: 'acme', project: 'shipping', view: 'default' })
    await nextTick()

    expect(typeMeta.exe.label).toBe('Execution')
    expect(styleText()).toBe('')
  })

  it('lets Preferences win over the project, per field', async () => {
    const context = useContextStore()
    useProjectSettingsStore().receive('acme.billing', { Types: { exe: { Label: 'Job', Color: 'red' } }, Version: 1 })
    const preferences = usePreferencesStore()
    preferences.setTypes({ exe: { label: 'Run' } })
    const appearance = useTypeAppearanceStore()

    context.set({ org: 'acme', project: 'billing', view: 'default' })
    await nextTick()

    // per field: the personal name, the project's color, the built-in icon
    expect(typeMeta.exe).toMatchObject({ label: 'Run', plural: 'Runs', icon: 'i-lucide-play' })
    expect(appearance.resolved.exe.color).toBe('red')

    preferences.reset()
    await nextTick()

    expect(typeMeta.exe.label).toBe('Job')
  })
})

import { computed, watchEffect } from 'vue'
import { defineStore } from 'pinia'
import {
  applyTypeAppearance,
  mergeTypeAppearances,
  resolveTypeAppearances,
  typeColorsCss,
} from '@/utils/type-appearance'
import { usePreferencesStore } from './preferences'
import { useProjectSettingsStore } from './project-settings'

export const typeColorsStyleId = 'st-type-colors'

/**
 * The names, colors and icons of the annotation types in effect (424-13): personal Preferences over the project's
 * settings over the built-in ones, per field. While the store lives, it writes the names and icons into `typeMeta`
 * and the colors into a style element that overrides the --st-* tokens. App.vue creates it once.
 */
export const useTypeAppearanceStore = defineStore('type-appearance', () => {
  const projectSettings = useProjectSettingsStore()
  const preferences = usePreferencesStore()

  const project = computed(() => projectSettings.current.types)
  const personal = computed(() => preferences.types)
  const effective = computed(() => mergeTypeAppearances(personal.value, project.value))
  const resolved = computed(() => resolveTypeAppearances(personal.value, project.value))
  const css = computed(() => typeColorsCss(effective.value))

  watchEffect(() => applyTypeAppearance(effective.value))

  watchEffect(() => {
    if (typeof document === 'undefined') {
      return
    }
    let style = document.getElementById(typeColorsStyleId)
    if (!style) {
      style = document.createElement('style')
      style.id = typeColorsStyleId
      document.head.append(style)
    }
    style.textContent = css.value
  })

  return { project, personal, effective, resolved, css }
})

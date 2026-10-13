import { computed, watchEffect } from 'vue'
import { defineStore } from 'pinia'
import { applyDateFormat } from '@/utils/dates'
import { usePreferencesStore } from './preferences'
import { useProjectSettingsStore } from './project-settings'

/**
 * The date format in effect (424-23): the personal one from Preferences, or the project's, which is `yyyy-mm-dd`
 * until someone changes it. While the store lives, it applies the format to the formatters of `@/utils/dates`.
 * App.vue creates it once.
 */
export const useDateFormatStore = defineStore('date-format', () => {
  const projectSettings = useProjectSettingsStore()
  const preferences = usePreferencesStore()

  const project = computed(() => projectSettings.dateFormat)
  const personal = computed(() => preferences.dateFormat)
  const effective = computed(() => personal.value ?? project.value)

  watchEffect(() => applyDateFormat(effective.value))

  return { project, personal, effective }
})

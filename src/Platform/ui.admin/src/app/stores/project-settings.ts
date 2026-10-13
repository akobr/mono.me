import { computed } from 'vue'
import { defineStore } from 'pinia'
import { useStorage } from '@vueuse/core'
import { isValidTimeZone } from '@/forms/annotation.schema'
import { sanitizeTypeAppearances, type TypeAppearances } from '@/utils/type-appearance'
import { useContextStore } from './context'

export interface ProjectSettings {
  /** IANA time zone that new annotations of the project start in. */
  defaultTimeZone: string
  /** Names, colors and icons of the annotation types in this project; Preferences can override them (424-13). */
  types: TypeAppearances
}

export const defaultProjectSettings: ProjectSettings = {
  defaultTimeZone: 'UTC',
  types: {},
}

/**
 * Settings of a project, per access point key (`org.project`). The API has no project document to keep them in, so
 * they live in this browser's localStorage; each entry holds only what was changed, the rest comes from the defaults.
 */
export const useProjectSettingsStore = defineStore('project-settings', () => {
  const context = useContextStore()
  // No storage argument: VueUse resolves localStorage itself and reports a blocked one to onError, keeping the defaults.
  const state = useStorage<Record<string, Partial<ProjectSettings>>>('2s-admin:project-settings', {}, undefined, {
    onError: (error) => console.warn('Project settings: localStorage access failed.', error),
  })

  function settingsOf(projectKey: string | undefined): ProjectSettings {
    const stored = projectKey ? state.value[projectKey] : undefined
    return {
      defaultTimeZone:
        stored?.defaultTimeZone && isValidTimeZone(stored.defaultTimeZone)
          ? stored.defaultTimeZone
          : defaultProjectSettings.defaultTimeZone,
      types: sanitizeTypeAppearances(stored?.types),
    }
  }

  const current = computed(() => settingsOf(context.projectKey))
  const defaultTimeZone = computed(() => current.value.defaultTimeZone)

  /**
   * Merges the change into the project's entry; a change to a time zone Intl does not know is ignored. Types are
   * stored sanitized, and empty types leave the entry, so it keeps only what was changed.
   */
  function update(projectKey: string, patch: Partial<ProjectSettings>): void {
    if (patch.defaultTimeZone !== undefined && !isValidTimeZone(patch.defaultTimeZone)) {
      return
    }

    const { types, ...rest } = { ...state.value[projectKey], ...patch }
    const cleanTypes = sanitizeTypeAppearances(types)
    state.value = {
      ...state.value,
      [projectKey]: Object.keys(cleanTypes).length > 0 ? { ...rest, types: cleanTypes } : rest,
    }
  }

  function reset(projectKey: string): void {
    const { [projectKey]: _removed, ...others } = state.value
    state.value = others
  }

  return { state, settingsOf, current, defaultTimeZone, update, reset }
})

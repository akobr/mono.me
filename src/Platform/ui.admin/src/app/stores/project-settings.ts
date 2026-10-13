import { computed } from 'vue'
import { defineStore } from 'pinia'
import { useStorage } from '@vueuse/core'
import { isValidTimeZone } from '@/forms/annotation.schema'
import { useContextStore } from './context'

export interface ProjectSettings {
  /** IANA time zone that new annotations of the project start in. */
  defaultTimeZone: string
}

export const defaultProjectSettings: ProjectSettings = {
  defaultTimeZone: 'UTC',
}

/**
 * Settings of a project, per access point key (`org.project`). The API has no project document to keep them in, so
 * they live in this browser's localStorage; each entry holds only what was changed, the rest comes from the defaults.
 */
export const useProjectSettingsStore = defineStore('project-settings', () => {
  const context = useContextStore()
  const state = useStorage<Record<string, Partial<ProjectSettings>>>('2s-admin:project-settings', {}, localStorage)

  function settingsOf(projectKey: string | undefined): ProjectSettings {
    const stored = projectKey ? state.value[projectKey] : undefined
    return {
      defaultTimeZone:
        stored?.defaultTimeZone && isValidTimeZone(stored.defaultTimeZone)
          ? stored.defaultTimeZone
          : defaultProjectSettings.defaultTimeZone,
    }
  }

  const current = computed(() => settingsOf(context.projectKey))
  const defaultTimeZone = computed(() => current.value.defaultTimeZone)

  /** Merges the change into the project's entry; a change to a time zone Intl does not know is ignored. */
  function update(projectKey: string, patch: Partial<ProjectSettings>): void {
    if (patch.defaultTimeZone !== undefined && !isValidTimeZone(patch.defaultTimeZone)) {
      return
    }
    state.value = { ...state.value, [projectKey]: { ...state.value[projectKey], ...patch } }
  }

  function reset(projectKey: string): void {
    const { [projectKey]: _removed, ...others } = state.value
    state.value = others
  }

  return { state, settingsOf, current, defaultTimeZone, update, reset }
})

import { computed, shallowRef } from 'vue'
import { defineStore } from 'pinia'
import type { ProjectSettings } from '@42for.net/2splatform.sdk'
import { defaultProjectSettings, fromApi, usableTimeZone, type ProjectSettingsState } from '@/utils/project-settings'
import { useContextStore } from './context'
import { onSignOut } from './session'

/**
 * The settings of every project the app has read, per access point key (`org.project`), as the API stores them for
 * everyone in the project (424-266). The query of the current project (useCurrentProjectSettings, mounted once in
 * App.vue) feeds every answer in with `receive`; a project whose answer has not arrived shows the defaults. Writes
 * are calls of the API, whose answers come back through the query.
 */
export const useProjectSettingsStore = defineStore('project-settings', () => {
  const context = useContextStore()
  const state = shallowRef<Record<string, ProjectSettingsState>>({})
  // The answer each entry was made from, so a refetch with the same settings changes nothing downstream.
  const sources = new Map<string, string>()

  /**
   * Takes an answer of the API for a project: sanitized, with the defaults for what it leaves out. An answer older than
   * the settings the app has (a lower Version, such as a slow read that arrives after the answer of a write) is ignored,
   * as the version only rises.
   */
  function receive(projectKey: string, settings: ProjectSettings): void {
    const source = JSON.stringify(settings)
    if (sources.get(projectKey) === source && projectKey in state.value) {
      return
    }
    const received = fromApi(settings)
    const stored = state.value[projectKey]
    if (stored && received.version < stored.version) {
      return
    }
    sources.set(projectKey, source)
    state.value = { ...state.value, [projectKey]: received }
  }

  function settingsOf(projectKey: string | undefined): ProjectSettingsState {
    return (projectKey ? state.value[projectKey] : undefined) ?? defaultProjectSettings
  }

  /** Whether an answer for the project has arrived (until then it shows the defaults). */
  function loaded(projectKey: string | undefined): boolean {
    return !!projectKey && projectKey in state.value
  }

  /** The version of the settings the app has, for If-Match; 0 before the first answer and before the first change. */
  function version(projectKey: string | undefined): number {
    return settingsOf(projectKey).version
  }

  function clear(): void {
    sources.clear()
    state.value = {}
  }

  const current = computed(() => settingsOf(context.projectKey))
  // The zone new annotations start in, one this browser's Intl knows (UTC for a zone newer than the browser).
  const defaultTimeZone = computed(() => usableTimeZone(current.value.defaultTimeZone))
  const dateFormat = computed(() => current.value.dateFormat)

  return { state, receive, settingsOf, loaded, version, clear, current, defaultTimeZone, dateFormat }
})

// The next user never sees the settings of the previous one's projects.
onSignOut(() => useProjectSettingsStore().clear())

import { computed } from 'vue'
import { defineStore } from 'pinia'
import { useStorage } from '@vueuse/core'
import type { Scope } from './context'

export type DocumentFormat = 'json' | 'yaml'
export type EditorMode = 'form' | DocumentFormat
export type Density = 'compact' | 'comfortable'

export interface GridPreferences {
  columnVisibility?: Record<string, boolean>
  columnOrder?: string[]
  columnSizing?: Record<string, number>
}

export interface Preferences {
  sidebarPinned: boolean
  editorFormat: DocumentFormat
  annotationMode: EditorMode
  density: Density
  lastContext?: Scope
  recentKeys: string[]
  grids: Record<string, GridPreferences>
}

export const defaultPreferences: Preferences = {
  sidebarPinned: true,
  editorFormat: 'json',
  annotationMode: 'form',
  density: 'compact',
  recentKeys: [],
  grids: {},
}

/** UI state only; nothing secret is ever stored here. Panel sizes are persisted by UDashboardGroup itself. */
export const usePreferencesStore = defineStore('preferences', () => {
  // A copy: useStorage would otherwise mutate the shared defaults when nothing is stored yet.
  const state = useStorage<Preferences>('2s-admin:preferences', structuredClone(defaultPreferences), localStorage, {
    mergeDefaults: true,
  })

  const sidebarPinned = computed({
    get: () => state.value.sidebarPinned,
    set: (value: boolean) => (state.value.sidebarPinned = value),
  })

  function rememberContext(scope: Scope): void {
    state.value.lastContext = scope
  }

  function rememberKey(key: string): void {
    state.value.recentKeys = [key, ...state.value.recentKeys.filter((recent) => recent !== key)].slice(0, 10)
  }

  function grid(id: string): GridPreferences {
    return (state.value.grids[id] ??= {})
  }

  function reset(): void {
    state.value = structuredClone(defaultPreferences)
  }

  return { state, sidebarPinned, rememberContext, rememberKey, grid, reset }
})

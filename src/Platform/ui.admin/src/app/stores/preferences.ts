import { computed } from 'vue'
import { defineStore } from 'pinia'
import { useStorage } from '@vueuse/core'
import { detailWidth } from '@/utils/detail-panel'
import { sanitizeTypeAppearances, type TypeAppearances } from '@/utils/type-appearance'
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
  /** The detail is docked beside the list; otherwise it opens as an overlay over the list. */
  detailPinned: boolean
  detailFullscreen: boolean
  /** Width of the overlay or pinned detail, in percent of the window. */
  detailWidth: number
  editorFormat: DocumentFormat
  annotationMode: EditorMode
  density: Density
  lastContext?: Scope
  recentKeys: string[]
  grids: Record<string, GridPreferences>
  /** Personal names, colors and icons of the annotation types, in every project; they win over the project's. */
  types: TypeAppearances
}

export const defaultPreferences: Preferences = {
  sidebarPinned: true,
  detailPinned: false,
  detailFullscreen: false,
  detailWidth: detailWidth.default,
  editorFormat: 'json',
  annotationMode: 'form',
  density: 'compact',
  recentKeys: [],
  grids: {},
  types: {},
}

/**
 * UI state only; nothing secret is ever stored here. The sidebar size is persisted by UDashboardGroup itself; the
 * detail panel keeps its mode and width here.
 */
export const usePreferencesStore = defineStore('preferences', () => {
  // A copy: useStorage would otherwise mutate the shared defaults when nothing is stored yet.
  const state = useStorage<Preferences>('2s-admin:preferences', structuredClone(defaultPreferences), localStorage, {
    mergeDefaults: true,
  })

  const sidebarPinned = computed({
    get: () => state.value.sidebarPinned,
    set: (value: boolean) => (state.value.sidebarPinned = value),
  })

  const types = computed(() => sanitizeTypeAppearances(state.value.types))

  function setTypes(value: TypeAppearances): void {
    state.value.types = sanitizeTypeAppearances(value)
  }

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

  return { state, sidebarPinned, types, setTypes, rememberContext, rememberKey, grid, reset }
})

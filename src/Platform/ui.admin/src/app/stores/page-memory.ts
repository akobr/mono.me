import { reactive } from 'vue'
import { defineStore } from 'pinia'
import type { RowSelectionState, SortingState } from '@tanstack/vue-table'
import type { AnnotationTypeCode } from '@/utils/annotation-key'
import type { Scope } from './context'
import { onSignOut } from './session'

/** The view sections that come back as the user left them. */
export const rememberedSections = ['annotations', 'configurations', 'graph'] as const

export type RememberedSection = (typeof rememberedSections)[number]

export const isRememberedSection = (value: string): value is RememberedSection =>
  (rememberedSections as readonly string[]).includes(value)

export interface AnnotationsMemory {
  /** Row ids: in the tree, an annotation with several parents has a row (and an expansion) per path. */
  expanded: Set<string>
  /** By annotation key, so every copy of a selected annotation stays selected. */
  selection: RowSelectionState
  sorting: SortingState
  /** Row id of the keyboard cursor. */
  activeId?: string
  scrollTop: number
}

export interface ConfigurationsMemory {
  scrollTop: number
}

/** A graph layout as it was left: positions by key, the alpha it had cooled to and the dragged nodes. */
export interface GraphLayoutMemory {
  keys: readonly string[]
  /** Interleaved x/y per key. */
  positions: Float32Array
  alpha: number
  pinned: readonly string[]
}

export interface GraphTransform {
  k: number
  x: number
  y: number
}

export interface GraphMemory {
  hidden: Set<AnnotationTypeCode>
  transform?: GraphTransform
  /** Stored with markRaw: up to 20,000 positions are not made reactive. */
  layout?: GraphLayoutMemory
}

export interface SectionMemories {
  annotations: AnnotationsMemory
  configurations: ConfigurationsMemory
  graph: GraphMemory
}

const empty: { [S in RememberedSection]: () => SectionMemories[S] } = {
  annotations: () => ({ expanded: new Set(), selection: {}, sorting: [], scrollTop: 0 }),
  configurations: () => ({ scrollTop: 0 }),
  graph: () => ({ hidden: new Set() }),
}

const memoryKey = (scope: Scope, section: RememberedSection) => `${scope.org}/${scope.project}/${scope.view}/${section}`

/**
 * What the annotations, configurations and graph pages need to come back as the user left them, per view: the last
 * location (path and query) and the state the URL does not hold. Kept in memory only, so a reload, a typed URL or
 * a new tab starts from the URL alone (spec "Remember the State of the Annotations, Configurations and Graph Pages").
 */
export const usePageMemoryStore = defineStore('page-memory', () => {
  const locations = new Map<string, string>()
  // The map itself is not reactive: an entry is created once per view and section, and only its fields change.
  const pages = new Map<string, SectionMemories[RememberedSection]>()

  function location(scope: Scope, section: RememberedSection): string | undefined {
    return locations.get(memoryKey(scope, section))
  }

  function rememberLocation(scope: Scope, section: RememberedSection, fullPath: string): void {
    locations.set(memoryKey(scope, section), fullPath)
  }

  function page<S extends RememberedSection>(scope: Scope, section: S): SectionMemories[S] {
    const key = memoryKey(scope, section)
    let entry = pages.get(key)
    if (!entry) {
      entry = reactive(empty[section]()) as SectionMemories[S]
      pages.set(key, entry)
    }
    return entry as SectionMemories[S]
  }

  function clear(): void {
    locations.clear()
    pages.clear()
  }

  return { location, rememberLocation, page, clear }
})

// The next user never sees where the previous one was.
onSignOut(() => usePageMemoryStore().clear())

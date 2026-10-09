import { storeToRefs } from 'pinia'
import { useContextStore } from '@/stores/context'

/** `{ org, project, view }` refs of the current route, for the generated composables (they accept refs). */
export function useAppContext() {
  const { org, project, view, projectKey, scope } = storeToRefs(useContextStore())
  return { org, project, view, projectKey, scope }
}

/** The same refs, typed as strings, for screens that only render inside a view scope. */
export function useScope() {
  const { org, project, view } = useAppContext()
  return {
    org: () => org.value ?? '',
    project: () => project.value ?? '',
    view: () => view.value ?? '',
  }
}

import { computed, ref } from 'vue'
import { defineStore } from 'pinia'

export interface Scope {
  org: string
  project: string
  view: string
}

export type ContextAccess = 'ok' | 'forbidden'

/** The organization, project and view of the current route, set by the context guard. */
export const useContextStore = defineStore('context', () => {
  const org = ref<string>()
  const project = ref<string>()
  const view = ref<string>()
  const access = ref<ContextAccess>('ok')

  const projectKey = computed(() => (org.value && project.value ? `${org.value}.${project.value}` : undefined))
  const scope = computed<Scope | undefined>(() =>
    org.value && project.value && view.value ? { org: org.value, project: project.value, view: view.value } : undefined,
  )

  function set(next: { org?: string; project?: string; view?: string }, nextAccess: ContextAccess = 'ok'): void {
    org.value = next.org
    project.value = next.project
    view.value = next.view
    access.value = nextAccess
  }

  return { org, project, view, access, projectKey, scope, set }
})

import { computed, inject, provide, toValue, type ComputedRef, type InjectionKey, type MaybeRefOrGetter } from 'vue'
import { useQueryClient } from '@tanstack/vue-query'
import { getAnnotation } from '@42for.net/2splatform.sdk'
import { getGetAnnotationQueryKey, useGetBindingCatalog } from '@42for.net/2splatform.sdk/vue-query'
import { useScope } from './useAppContext'
import {
  ancestorKeyOf,
  ancestorTypesOf,
  ancestorTypesOfType,
  catalogView,
  pointersOf,
  type BindingCatalogView,
} from '@/utils/binding-language'

/**
 * Everything the "fx" surfaces need (424-10): the catalog of binding functions and sources, and the paths a
 * function can read. The CodeMirror extension calls the functions when it completes, so they are not reactive;
 * `catalog` is, for the components.
 */
export interface BindingAssist {
  catalog: ComputedRef<BindingCatalogView>
  /** JSON Pointers of the configuration `@config("…")` reads. */
  configPointers: () => string[]
  /** JSON Pointers of the `Values` `@annotation("…")` reads: of the annotation, or of its ancestor of `type`. */
  annotationPointers: (type?: string) => Promise<string[]>
  /** The types `@annotation("…", "<type>")` accepts. */
  annotationTypes: () => string[]
}

export interface BindingAssistOptions {
  /** The annotation whose configuration is edited; none for a template. */
  annotationKey?: MaybeRefOrGetter<string | undefined>
  /** The annotation type of a template, for the types `@annotation` accepts when there is no key. */
  annotationType?: MaybeRefOrGetter<string | undefined>
  /** The documents whose paths `@config` can read: the effective document and the one being edited. */
  documents?: () => unknown[]
}

/** The catalog of the server (cached for 10 minutes) with the documents of the edited configuration. */
export function useBindingAssist(options: BindingAssistOptions = {}): BindingAssist {
  const scope = useScope()
  const queryClient = useQueryClient()
  const query = useGetBindingCatalog(scope.org, scope.project, {
    query: { enabled: () => !!scope.project(), staleTime: 10 * 60_000, retry: false, meta: { silent: true } },
  })

  return {
    catalog: computed(() => catalogView(query.data.value)),
    configPointers: () => [...new Set((options.documents?.() ?? []).flatMap((document) => pointersOf(document)))],
    annotationPointers: async (type) => {
      const key = toValue(options.annotationKey)
      const target = key && (type ? ancestorKeyOf(key, type) : key)
      if (!target || !scope.view()) {
        return []
      }
      const [org, project, view] = [scope.org(), scope.project(), scope.view()]
      // The annotation detail already holds the own annotation; an ancestor is fetched once and cached.
      const annotation = await queryClient
        .fetchQuery({
          queryKey: getGetAnnotationQueryKey(org, project, view, target, { includeInactive: true }),
          queryFn: ({ signal }) => getAnnotation(org, project, view, target, { includeInactive: true }, { signal }),
          staleTime: 30_000,
          meta: { silent: true },
        })
        .catch(() => undefined)
      return pointersOf(annotation?.Values)
    },
    annotationTypes: () => {
      const key = toValue(options.annotationKey)
      const type = toValue(options.annotationType)
      return !key && type ? ancestorTypesOfType(type) : ancestorTypesOf(key)
    },
  }
}

/** The fallback catalog without documents, for a form mounted outside an editor. */
export const staticBindingAssist: BindingAssist = {
  catalog: computed(() => catalogView()),
  configPointers: () => [],
  annotationPointers: async () => [],
  annotationTypes: () => ancestorTypesOf(undefined),
}

const key: InjectionKey<BindingAssist> = Symbol('binding-assist')

export function provideBindingAssist(assist: BindingAssist): void {
  provide(key, assist)
}

export function injectBindingAssist(): BindingAssist {
  return inject(key, staticBindingAssist)
}

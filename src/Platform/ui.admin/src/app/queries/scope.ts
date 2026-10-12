import { computed, toValue, type MaybeRefOrGetter } from 'vue'
import {
  useGetAnnotation,
  useGetAnnotationHistory,
  useGetDeletedAnnotation,
  useGetTemplates,
} from '@42for.net/2splatform.sdk/vue-query'
import { useScope } from '@/composables/useAppContext'
import { metaOf, type AnnotationTypeCode } from '@/utils/annotation-key'

// Thin wrappers that bind org, project and view of the current context to the generated composables, so
// screens pass only the key (spec section 6).

export function useAnnotation(key: MaybeRefOrGetter<string | undefined>) {
  const scope = useScope()
  return useGetAnnotation(scope.org, scope.project, scope.view, () => toValue(key) ?? '', {
    query: { enabled: () => !!toValue(key) && !!scope.view(), staleTime: 30_000, refetchOnWindowFocus: true },
  })
}

/** The change history of an annotation, active or deleted (404 for a purged or unknown key). */
export function useAnnotationHistory(key: MaybeRefOrGetter<string | undefined>) {
  const scope = useScope()
  return useGetAnnotationHistory(scope.org, scope.project, scope.view, () => toValue(key) ?? '', {
    query: { enabled: () => !!toValue(key) && !!scope.view(), staleTime: 10_000, meta: { silent: true } },
  })
}

/** A deleted annotation with its Deletion; asked only when `enabled` (for example after a 404 of GetAnnotation). */
export function useDeletedAnnotation(key: MaybeRefOrGetter<string | undefined>, enabled: MaybeRefOrGetter<boolean>) {
  const scope = useScope()
  return useGetDeletedAnnotation(scope.org, scope.project, scope.view, () => toValue(key) ?? '', {
    query: {
      enabled: () => !!toValue(key) && !!scope.view() && toValue(enabled),
      retry: false,
      meta: { silent: true },
    },
  })
}

/** The annotation types that have a template in the view. */
export function useTemplateTypes() {
  const scope = useScope()
  const templates = useGetTemplates(scope.org, scope.project, scope.view, {
    query: { enabled: () => !!scope.view(), staleTime: 60_000, meta: { silent: true } },
  })
  return computed<ReadonlySet<AnnotationTypeCode> | undefined>(() =>
    templates.data.value
      ? new Set(
          templates.data.value
            .map((summary) => metaOf(summary.AnnotationType)?.code)
            .filter((code): code is AnnotationTypeCode => !!code),
        )
      : undefined,
  )
}

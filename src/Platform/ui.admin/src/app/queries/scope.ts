import { computed, toValue, type MaybeRefOrGetter } from 'vue'
import type { GetKeyValueAuditParams, GetKeyValuesParams, GetKeyValueUsageParams } from '@42for.net/2splatform.sdk'
import {
  useGetAnnotation,
  useGetAnnotationHistory,
  useGetDeletedAnnotation,
  useGetKeyValue,
  useGetKeyValueAudit,
  useGetKeyValuePolicy,
  useGetKeyValues,
  useGetKeyValueUsage,
  useGetKeyValueVersions,
  useGetRecipients,
  useGetTemplates,
} from '@42for.net/2splatform.sdk/vue-query'
import { useAppContext } from '@/composables/useAppContext'
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

// --- key/value pairs (spec section 29.1) ---

export function useKeyValues(params: MaybeRefOrGetter<GetKeyValuesParams>) {
  const scope = useScope()
  return useGetKeyValues(scope.org, scope.project, scope.view, () => toValue(params), {
    query: { enabled: () => !!scope.view(), staleTime: 15_000, refetchOnWindowFocus: true },
  })
}

/** One pair without its secret value; reveals go through useRevealedValue and are never cached. */
export function useKeyValue(key: MaybeRefOrGetter<string | undefined>, includeDeleted = true) {
  const scope = useScope()
  return useGetKeyValue(scope.org, scope.project, scope.view, () => toValue(key) ?? '', { includeDeleted }, {
    query: { enabled: () => !!toValue(key) && !!scope.view(), staleTime: 15_000, meta: { silent: true } },
  })
}

/** The versions of a key, loaded only while the Versions tab is open (spec R6). */
export function useKeyValueVersions(key: MaybeRefOrGetter<string | undefined>, enabled: MaybeRefOrGetter<boolean>) {
  const scope = useScope()
  return useGetKeyValueVersions(scope.org, scope.project, scope.view, () => toValue(key) ?? '', {
    query: { enabled: () => toValue(enabled) && !!toValue(key) && !!scope.view(), staleTime: 15_000 },
  })
}

export function useKeyValuePolicy() {
  const { org, project } = useAppContext()
  return useGetKeyValuePolicy(() => org.value ?? '', () => project.value ?? '', {
    query: { enabled: () => !!project.value, staleTime: 60_000, meta: { silent: true } },
  })
}

export function useRecipients() {
  const { org, project } = useAppContext()
  return useGetRecipients(() => org.value ?? '', () => project.value ?? '', {
    query: { enabled: () => !!project.value, staleTime: 60_000, meta: { silent: true } },
  })
}

export function useKeyValueAudit(params: MaybeRefOrGetter<GetKeyValueAuditParams>, enabled: MaybeRefOrGetter<boolean> = true) {
  const { org, project } = useAppContext()
  return useGetKeyValueAudit(() => org.value ?? '', () => project.value ?? '', () => toValue(params), {
    query: { enabled: () => !!project.value && toValue(enabled), staleTime: 15_000, meta: { silent: true } },
  })
}

export function useKeyValueUsage(params: MaybeRefOrGetter<GetKeyValueUsageParams>, enabled: MaybeRefOrGetter<boolean> = true) {
  const { org, project } = useAppContext()
  return useGetKeyValueUsage(() => org.value ?? '', () => project.value ?? '', () => toValue(params), {
    query: { enabled: () => !!project.value && toValue(enabled), staleTime: 60_000, meta: { silent: true } },
  })
}

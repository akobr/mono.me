import type { QueryClient, QueryKey } from '@tanstack/vue-query'

// Generated query keys are the path segments of the request, for example
//   GET /v1/acme/billing/default/configuration/exe.a.b.c → ['v1', 'acme', 'billing', 'default', 'configuration', 'exe.a.b.c']
// followed by the query parameters object when there is one. TanStack Query matches keys by prefix,
// so invalidating ['v1', 'acme', 'billing', 'default', 'configuration', 'exe.a.b.c'] also covers
// '/resolved', '/versions' and every diff of that configuration.

/** Splits an API path ("/v1/acme/billing/default/configuration/exe.a.b.c") into a query key prefix. */
export function pathToQueryKey(path: string): string[] {
  const withoutQuery = path.split('?')[0] ?? ''
  return withoutQuery.split('/').filter((segment) => segment.length > 0)
}

/** True when the key starts with the segments of the path. */
export function queryKeyStartsWith(queryKey: QueryKey, path: string): boolean {
  const prefix = pathToQueryKey(path)
  return prefix.length <= queryKey.length && prefix.every((segment, index) => queryKey[index] === segment)
}

/** Invalidates every cached query whose key starts with the path, for example after a mutation. */
export function invalidateByPath(queryClient: QueryClient, path: string): Promise<void> {
  return queryClient.invalidateQueries({ queryKey: pathToQueryKey(path) })
}

/** Removes cached queries under the path without refetching, for example secrets after leaving a page. */
export function removeByPath(queryClient: QueryClient, path: string): void {
  queryClient.removeQueries({ queryKey: pathToQueryKey(path) })
}

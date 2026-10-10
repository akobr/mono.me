import type { ConfigurationVersion, DiffResult } from '@42for.net/2splatform.sdk'

/**
 * Where a versioned document (configuration, template, any schema kind) gets its history from (spec section 11.3).
 * `queryKey` starts with the resource's path segments, so the path-prefix invalidation after a save covers it.
 */
export interface VersionSource {
  queryKey: readonly unknown[]
  list: (signal?: AbortSignal) => Promise<ConfigurationVersion[]>
  content: (version: number, signal?: AbortSignal) => Promise<unknown>
  diff: (version: number, from: number, signal?: AbortSignal) => Promise<DiffResult>
  /** The current version (marked in the timeline). */
  current?: number
}

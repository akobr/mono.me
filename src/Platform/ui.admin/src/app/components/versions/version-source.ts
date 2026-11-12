import type { ConfigurationVersion, DiffResult, VersionRestoreResult } from '@42for.net/2splatform.sdk'

/** The kinds of versioned documents, for the wording of the history. */
export type VersionedDocument = 'configuration' | 'template' | 'schema'

/**
 * Where a versioned document (configuration, template, any schema kind) gets its history from (spec section 11.3).
 * `queryKey` starts with the resource's path segments, so the path-prefix invalidation after a save covers it.
 */
export interface VersionSource {
  document: VersionedDocument
  queryKey: readonly unknown[]
  list: (signal?: AbortSignal) => Promise<ConfigurationVersion[]>
  content: (version: number, signal?: AbortSignal) => Promise<unknown>
  diff: (version: number, from: number, signal?: AbortSignal) => Promise<DiffResult>
  /** The current version (marked in the timeline). */
  current?: number
  /**
   * Stores the content of a version as the document's new version (424-221); `force` stores it although a schema check
   * fails. Without it, the history offers no restore (the document cannot have one now).
   */
  restore?: (version: number, force?: boolean) => Promise<VersionRestoreResult>
  /** Refreshes what a restore changed, as after a save of the document (with the versions). */
  afterRestore?: (result: VersionRestoreResult) => Promise<void>
  /** The host has unsaved edits of the document, which a restore discards (its afterRestore resets the editor). */
  unsavedChanges?: boolean
}

/** The query key of the version list of a source; the history and its host share the one query. */
export const versionsQueryKey = (source: VersionSource) => [...source.queryKey, 'versions']

/** Has the version expired? Its document may no longer load, and it cannot be restored. */
export const versionExpired = (entry: ConfigurationVersion, now = Date.now()) =>
  !!entry.ExpirationTime && Date.parse(entry.ExpirationTime) <= now

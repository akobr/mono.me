import type { ConfigurationImport } from '@42for.net/2splatform.sdk'

export function plural(count: number, noun: string): string {
  return `${count} ${noun}${count === 1 ? '' : 's'}`
}

/** "2 created, 1 updated, 5 unchanged, 1 failed": the counts that are not zero, unchanged always. */
export function importCounts(state: ConfigurationImport): string {
  const summary = state.Summary ?? {}
  const parts = [
    summary.Created ? `${summary.Created} ${state.IsDryRun ? 'to create' : 'created'}` : '',
    summary.Updated ? `${summary.Updated} ${state.IsDryRun ? 'to update' : 'updated'}` : '',
    `${summary.Unchanged ?? 0} unchanged`,
    summary.Skipped ? `${summary.Skipped} skipped` : '',
    summary.Failed ? `${summary.Failed} failed` : '',
  ]

  return parts.filter(Boolean).join(', ')
}

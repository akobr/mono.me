import { projectSettingsKey } from '@/stores/project-settings'
import { showcaseProjects } from './index'

/**
 * Writes the settings of every showcase project (type names, colors and icons, time zone, date format) into the
 * project settings of this browser, keeping the other projects' entries. It runs when the showcase dataset starts
 * fresh (browser.ts), so the appearance is part of the dataset: edits in Settings survive reloads, and a reset of the
 * dataset restores the showcase.
 */
export function writeShowcaseSettings(storage: Storage = localStorage): void {
  try {
    const entries: Record<string, unknown> = { ...storedEntries(storage.getItem(projectSettingsKey)) }
    for (const project of showcaseProjects) {
      entries[`${project.org}.${project.project}`] = structuredClone(project.settings)
    }
    storage.setItem(projectSettingsKey, JSON.stringify(entries))
  } catch (error) {
    console.warn('Showcase: the project settings could not be written.', error)
  }
}

/** The stored entries; nothing when the value is missing or not an object. */
function storedEntries(stored: string | null): Record<string, unknown> {
  try {
    const parsed: unknown = JSON.parse(stored ?? '{}')
    return parsed && typeof parsed === 'object' && !Array.isArray(parsed) ? (parsed as Record<string, unknown>) : {}
  } catch {
    return {}
  }
}

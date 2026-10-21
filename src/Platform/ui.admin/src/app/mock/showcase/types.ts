import type { AccountRole } from '@42for.net/2splatform.sdk'
import type { DateFormat } from '@/utils/dates'
import type { TypeAppearances } from '@/utils/type-appearance'
import type { ScopeBuilder } from './builder'
import type { PersonaId } from './people'

export type Members = Partial<Record<PersonaId, Exclude<AccountRole, 'None'>>>

export interface ShowcaseView {
  name: string
  description: string
}

export interface ShowcaseOrganization {
  org: string
  members: Members
}

/** The project settings the showcase writes into this browser when the dataset starts (settings.ts). */
export interface ShowcaseSettings {
  types: TypeAppearances
  defaultTimeZone: string
  dateFormat: DateFormat
}

export interface ShowcaseProject {
  org: string
  project: string
  /** The description of the project, shown in the admin UI. */
  description: string
  /** The project's access point; Ada included. */
  members: Members
  settings: ShowcaseSettings
  /** The registered views, `default` first. */
  views: ShowcaseView[]
  /** Fills the builder of one of the views; `builder.view` says which. */
  build(builder: ScopeBuilder): void
  /** Own configurations that fail their combined schema on purpose, to show the non-compliance screens. */
  nonCompliant?: string[]
}

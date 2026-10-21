import { roleRank } from '@42for.net/2splatform.sdk'
import { ScopeBuilder, type ScopeBuild } from './builder'
import type { PersonaId } from './people'
import type { ShowcaseOrganization, ShowcaseProject } from './types'
import { clinicalIntegrations } from './projects/clinical-integrations'
import { conferences } from './projects/conferences'
import { erp } from './projects/erp'
import { financeCloud } from './projects/finance-cloud'
import { fitnessCloud } from './projects/fitness-cloud'
import { guestPlatform } from './projects/guest-platform'
import { invoicingSuite } from './projects/invoicing-suite'
import { liveOps } from './projects/live-ops'
import { moneySuite } from './projects/money-suite'
import { plantFleet } from './projects/plant-fleet'
import { storefronts } from './projects/storefronts'
import { streamingPlatform } from './projects/streaming-platform'
import { vehiclePlatform } from './projects/vehicle-platform'
import { whiteLabelApp } from './projects/white-label-app'

// The showcase dataset (424-77): fourteen projects that model the documentation's examples, the examples of the
// Miro board and three new ones, each in its own organization or next to the products of the same company. A scope
// is built on first use and kept for the page's lifetime, like the generated catalogs of the other datasets.

export const showcaseOrganizations: ShowcaseOrganization[] = [
  { org: 'fabrikam', members: { ada: 'Owner', grace: 'Administrator', margaret: 'Contributor' } },
  { org: 'bluefin', members: { frances: 'Owner', hedy: 'Administrator' } },
  { org: 'streamwave', members: { ken: 'Owner', ada: 'Administrator', radia: 'Contributor' } },
  { org: 'ledgerline', members: { ada: 'Owner', barbara: 'Administrator' } },
  { org: 'aurora-motors', members: { hedy: 'Owner', ada: 'Contributor', annie: 'ContributorWithSecrets' } },
  { org: 'harbor-industrial', members: { radia: 'Owner', ken: 'Reader' } },
  { org: 'meridian-health', members: { annie: 'Owner', barbara: 'Administrator' } },
  { org: 'appforge', members: { edsger: 'Owner', ada: 'ContributorWithSecrets', hedy: 'Contributor' } },
  { org: 'gatherly', members: { ada: 'Owner', donald: 'Administrator' } },
  { org: 'pixel-forge', members: { alan: 'Owner', ada: 'Administrator', ken: 'Contributor' } },
  { org: 'velvet-retail', members: { margaret: 'Owner', ada: 'Contributor', edsger: 'Administrator' } },
  { org: 'seabreeze-hotels', members: { grace: 'Owner', frances: 'Reader' } },
]

export const showcaseProjects: ShowcaseProject[] = [
  financeCloud,
  fitnessCloud,
  invoicingSuite,
  moneySuite,
  streamingPlatform,
  erp,
  vehiclePlatform,
  plantFleet,
  clinicalIntegrations,
  whiteLabelApp,
  conferences,
  liveOps,
  storefronts,
  guestPlatform,
]

export function showcaseProject(org: string, project: string): ShowcaseProject | undefined {
  return showcaseProjects.find((candidate) => candidate.org === org && candidate.project === project)
}

/** The members who may edit, in the order of the project's members: the authors of the seeded versions. */
export function editorsOf(project: ShowcaseProject): PersonaId[] {
  return Object.entries(project.members)
    .filter(([, role]) => roleRank(role) >= roleRank('Contributor'))
    .map(([persona]) => persona as PersonaId)
}

/** Builds one scope from its definition; undefined for an unknown project or view. Not memoized (tests). */
export function buildShowcaseScope(org: string, project: string, view: string): ScopeBuild | undefined {
  const definition = showcaseProject(org, project)
  if (!definition?.views.some((candidate) => candidate.name === view)) {
    return undefined
  }
  const builder = new ScopeBuilder({
    project,
    view,
    timeZone: definition.settings.defaultTimeZone,
    editors: editorsOf(definition),
  })
  definition.build(builder)
  return builder.build()
}

const scopes = new Map<string, ScopeBuild | undefined>()

/** The annotations, documents and seeded deletions of a scope, built once. */
export function showcaseScope(org: string, project: string, view: string): ScopeBuild | undefined {
  const id = `${org}/${project}/${view}`
  if (!scopes.has(id)) {
    scopes.set(id, buildShowcaseScope(org, project, view))
  }
  return scopes.get(id)
}

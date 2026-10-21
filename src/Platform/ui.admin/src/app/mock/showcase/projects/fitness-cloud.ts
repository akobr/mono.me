import type { ShowcaseProject } from '../types'
import { environmentViews } from '../views'

export const fitnessCloud: ShowcaseProject = {
  org: 'fabrikam',
  project: 'fitness-cloud',
  summary: 'Multi-tenant B2B SaaS: gym chains, their members and the applications each member is assigned.',
  members: { margaret: 'Owner', ada: 'Administrator', barbara: 'Contributor', annie: 'Reader' },
  settings: { types: {}, defaultTimeZone: 'UTC', dateFormat: 'yyyy-mm-dd' },
  views: environmentViews(),
  build: () => {},
}

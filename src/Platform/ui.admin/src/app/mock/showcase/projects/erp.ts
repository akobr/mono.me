import type { ShowcaseProject } from '../types'
import { environmentViews } from '../views'

export const erp: ShowcaseProject = {
  org: 'ledgerline',
  project: 'erp',
  summary: 'The versioned monolith of the documentation: the versions in production, their channels and modules.',
  members: { ada: 'Owner', barbara: 'Administrator', edsger: 'Contributor', alan: 'Reader' },
  settings: { types: {}, defaultTimeZone: 'UTC', dateFormat: 'yyyy-mm-dd' },
  views: environmentViews(),
  build: () => {},
}

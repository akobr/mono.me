import type { ShowcaseProject } from '../types'
import { environmentViews } from '../views'

export const financeCloud: ShowcaseProject = {
  org: 'fabrikam',
  project: 'finance-cloud',
  summary:
    'Single-tenant B2B SaaS: customers, their instances, the applications they bought and the jobs that run in each instance.',
  members: {
    ada: 'Owner',
    grace: 'Administrator',
    alan: 'Contributor',
    margaret: 'ContributorWithSecrets',
    ken: 'Reader',
  },
  settings: { types: {}, defaultTimeZone: 'UTC', dateFormat: 'yyyy-mm-dd' },
  views: environmentViews(),
  build: () => {},
}

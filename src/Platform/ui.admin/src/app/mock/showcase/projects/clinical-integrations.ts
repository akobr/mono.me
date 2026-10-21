import type { ShowcaseProject } from '../types'
import { environmentViews } from '../views'

export const clinicalIntegrations: ShowcaseProject = {
  org: 'meridian-health',
  project: 'clinical-integrations',
  summary: 'Hospital network integrations: hospitals, departments and the clinical systems integrated in each.',
  members: {
    annie: 'Owner',
    barbara: 'Administrator',
    donald: 'ContributorWithSecrets',
    grace: 'Contributor',
    ada: 'Reader',
  },
  settings: { types: {}, defaultTimeZone: 'UTC', dateFormat: 'yyyy-mm-dd' },
  views: environmentViews(),
  build: () => {},
}

import type { ShowcaseProject } from '../types'
import { environmentViews } from '../views'

export const conferences: ShowcaseProject = {
  org: 'gatherly',
  project: 'conferences',
  summary: 'A conference series and its editions: the services booked for each edition and the jobs around them.',
  members: { ada: 'Owner', donald: 'Administrator', frances: 'Contributor' },
  settings: { types: {}, defaultTimeZone: 'UTC', dateFormat: 'yyyy-mm-dd' },
  views: environmentViews(),
  build: () => {},
}

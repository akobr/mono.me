import type { ShowcaseProject } from '../types'
import { environmentViews } from '../views'

export const streamingPlatform: ShowcaseProject = {
  org: 'streamwave',
  project: 'streaming-platform',
  summary:
    'The distributed platform of the documentation: markets, their cells and the services deployed in each cell.',
  members: { ken: 'Owner', ada: 'Administrator', radia: 'Administrator', donald: 'Contributor', grace: 'Reader' },
  settings: { types: {}, defaultTimeZone: 'UTC', dateFormat: 'yyyy-mm-dd' },
  views: environmentViews(),
  build: () => {},
}

import type { ShowcaseProject } from '../types'
import { environmentViews } from '../views'

export const whiteLabelApp: ShowcaseProject = {
  org: 'appforge',
  project: 'white-label-app',
  summary: 'A white-label mobile app: retail brands, their platforms and the features licensed to each brand.',
  members: { edsger: 'Owner', hedy: 'Administrator', ada: 'Contributor', alan: 'Reader' },
  settings: { types: {}, defaultTimeZone: 'UTC', dateFormat: 'yyyy-mm-dd' },
  views: environmentViews(),
  build: () => {},
}

import type { ShowcaseProject } from '../types'
import { environmentViews } from '../views'

export const vehiclePlatform: ShowcaseProject = {
  org: 'aurora-motors',
  project: 'vehicle-platform',
  summary: 'The physical product of the documentation: vehicle models, trims and VINs, and the parts fitted to them.',
  members: { hedy: 'Owner', annie: 'ContributorWithSecrets', ada: 'Contributor', frances: 'Reader' },
  settings: { types: {}, defaultTimeZone: 'UTC', dateFormat: 'yyyy-mm-dd' },
  views: environmentViews(),
  build: () => {},
}

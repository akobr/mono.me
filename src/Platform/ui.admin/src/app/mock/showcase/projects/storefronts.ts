import type { ShowcaseProject } from '../types'
import { environmentViews } from '../views'

export const storefronts: ShowcaseProject = {
  org: 'velvet-retail',
  project: 'storefronts',
  summary: 'Multi-brand storefronts: brand storefronts, their markets and the commerce capabilities set up per market.',
  members: { margaret: 'Owner', edsger: 'Administrator', ada: 'Contributor', radia: 'Reader' },
  settings: { types: {}, defaultTimeZone: 'UTC', dateFormat: 'yyyy-mm-dd' },
  views: environmentViews(),
  build: () => {},
}

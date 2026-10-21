import type { ShowcaseProject } from '../types'
import { environmentViews } from '../views'

export const moneySuite: ShowcaseProject = {
  org: 'bluefin',
  project: 'money-suite',
  summary:
    'The consumer finance suite of the documentation: people, the legal entities they act as, and the products they use.',
  members: { frances: 'Owner', hedy: 'ContributorWithSecrets', ada: 'Contributor', radia: 'Reader' },
  settings: { types: {}, defaultTimeZone: 'UTC', dateFormat: 'yyyy-mm-dd' },
  views: environmentViews(),
  build: () => {},
}

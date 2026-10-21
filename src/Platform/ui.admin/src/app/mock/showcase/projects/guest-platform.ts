import type { ShowcaseProject } from '../types'
import { environmentViews } from '../views'

export const guestPlatform: ShowcaseProject = {
  org: 'seabreeze-hotels',
  project: 'guest-platform',
  summary: 'Hotel distribution: hotels, their sales channels and the guest-platform modules set up per channel.',
  members: {
    grace: 'Owner',
    frances: 'Administrator',
    ada: 'ContributorWithSecrets',
    hedy: 'Contributor',
    annie: 'Reader',
  },
  settings: { types: {}, defaultTimeZone: 'UTC', dateFormat: 'yyyy-mm-dd' },
  views: environmentViews(),
  build: () => {},
}

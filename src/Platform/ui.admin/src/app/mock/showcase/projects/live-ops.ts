import type { ShowcaseProject } from '../types'
import { environmentViews } from '../views'

export const liveOps: ShowcaseProject = {
  org: 'pixel-forge',
  project: 'live-ops',
  summary: 'Game live operations: games, platform regions, backend services and seasonal live events.',
  members: { alan: 'Owner', ada: 'Administrator', ken: 'ContributorWithSecrets', barbara: 'Contributor' },
  settings: { types: {}, defaultTimeZone: 'UTC', dateFormat: 'yyyy-mm-dd' },
  views: environmentViews(),
  build: () => {},
}

import type { ShowcaseProject } from '../types'
import { environmentViews } from '../views'

export const plantFleet: ShowcaseProject = {
  org: 'harbor-industrial',
  project: 'plant-fleet',
  summary: 'The device fleet of the documentation: sites, their devices and the capabilities each device runs.',
  members: { radia: 'Owner', ada: 'ContributorWithSecrets', ken: 'Contributor', margaret: 'Reader' },
  settings: { types: {}, defaultTimeZone: 'UTC', dateFormat: 'yyyy-mm-dd' },
  views: environmentViews(),
  build: () => {},
}

// @42for.net/2splatform.sdk/msw: MSW request handlers with Faker data for tests. Needs the peers msw and @faker-js/faker.
// Handlers match any origin with the "/api" prefix ("*/api/v1/…").
import { getAccessMock } from './generated/vue-query/access/access.msw'
import { getAnnotationsMock } from './generated/vue-query/annotations/annotations.msw'
import { getConfigurationsMock } from './generated/vue-query/configurations/configurations.msw'
import { getSchemasMock } from './generated/vue-query/schemas/schemas.msw'
import { getTemplatesMock } from './generated/vue-query/templates/templates.msw'

export * from './generated/vue-query/access/access.msw'
export * from './generated/vue-query/annotations/annotations.msw'
export * from './generated/vue-query/configurations/configurations.msw'
export * from './generated/vue-query/schemas/schemas.msw'
export * from './generated/vue-query/templates/templates.msw'

/** Every generated handler; prepend your own handlers to override single operations. */
export const getStorytellerMock = () => [
  ...getAccessMock(),
  ...getAnnotationsMock(),
  ...getConfigurationsMock(),
  ...getSchemasMock(),
  ...getTemplatesMock(),
]

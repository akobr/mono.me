import type { HttpHandler } from 'msw'
import { annotationHandlers } from './annotation-handlers'
import { configurationHandlers } from './configuration-handlers'
import { accessHandlers, viewHandlers } from './handlers'
import { templateSchemaHandlers } from './template-schema-handlers'

/** Every hand-written mock handler, in matching order (before the generated Faker handlers). */
export const mockHandlers: HttpHandler[] = [
  ...accessHandlers,
  ...viewHandlers,
  ...annotationHandlers,
  // The definition route (configuration-schema/{key}/definition) must win over configuration-schema/{key}/…
  ...configurationHandlers,
  ...templateSchemaHandlers,
]

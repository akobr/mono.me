import type { HttpHandler } from 'msw'
import { accessManagementHandlers } from './access-handlers'
import { annotationHandlers } from './annotation-handlers'
import { configurationHandlers } from './configuration-handlers'
import { keyValueHandlers } from './key-value-handlers'
import { accessHandlers, bindingHandlers, viewHandlers } from './handlers'
import { transferHandlers } from './transfer-handlers'
import { templateSchemaHandlers } from './template-schema-handlers'

/** Every hand-written mock handler, in matching order (before the generated Faker handlers). */
export const mockHandlers: HttpHandler[] = [
  ...accessHandlers,
  ...accessManagementHandlers,
  ...viewHandlers,
  ...bindingHandlers,
  ...annotationHandlers,
  ...transferHandlers,
  // The definition route (configuration-schema/{key}/definition) must win over configuration-schema/{key}/…
  ...configurationHandlers,
  ...templateSchemaHandlers,
  ...keyValueHandlers,
]

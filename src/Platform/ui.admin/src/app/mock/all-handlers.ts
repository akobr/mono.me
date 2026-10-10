import type { HttpHandler } from 'msw'
import { annotationHandlers } from './annotation-handlers'
import { configurationHandlers } from './configuration-handlers'
import { accessHandlers, viewHandlers } from './handlers'

/** Every hand-written mock handler, in matching order (before the generated Faker handlers). */
export const mockHandlers: HttpHandler[] = [
  ...accessHandlers,
  ...viewHandlers,
  ...annotationHandlers,
  ...configurationHandlers,
]

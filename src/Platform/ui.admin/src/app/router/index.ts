import { createRouter, createWebHistory, type Router } from 'vue-router'
import { handleHotUpdate, routes } from 'vue-router/auto-routes'
import { installAuthGuard } from './guards/auth'
import { installContextGuard } from './guards/context'
import { installPageMemoryGuard } from './guards/page-memory'

/** Routes come from app/pages (Vue Router 5 file-based routing); guards run in registration order. */
export function createAppRouter(): Router {
  const router = createRouter({ history: createWebHistory(import.meta.env.BASE_URL), routes })

  installAuthGuard(router)
  installContextGuard(router)
  installPageMemoryGuard(router)

  if (import.meta.hot) {
    handleHotUpdate(router)
  }

  return router
}

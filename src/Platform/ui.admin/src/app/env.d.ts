/// <reference types="vite/client" />

interface ImportMetaEnv {
  readonly VITE_API_BASE_URL?: string
  readonly VITE_AUTH_API_HOSTNAME?: string
  readonly VITE_ENVIRONMENT_NAME?: string
  readonly VITE_SENTRY_DSN?: string
  readonly VITE_POSTHOG_PROJECT_TOKEN?: string
  readonly VITE_POSTHOG_HOST?: string
  /** The mock dataset `npm run dev:mock` picked (build/dev-mock.ts); the e2e build reads it at start-up. */
  readonly VITE_MOCK_DATASET?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}

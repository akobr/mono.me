import { shallowRef } from 'vue'
import type { AppConfig } from './app-config'
import type { AuthBoot, AuthService } from './auth'

export type BootState = AuthBoot & { config?: AppConfig }

const state = shallowRef<BootState>({ status: 'error', message: 'The application has not started yet.' })

/** The result of the boot sequence: the configuration and the auth service, or why the app cannot sign in. */
export function setBootState(value: BootState): void {
  state.value = value
}

export function useBootState() {
  return state
}

export function getAuthService(): AuthService | undefined {
  return state.value.status === 'ready' ? state.value.auth : undefined
}

/** The default window of the API's step-up (Auth:StepUp:MaxAge), when the discovery does not name one. */
export const defaultStepUpMaxAge = 600

export interface StepUpSettings {
  /** Whether the API asks for a recent sign-in (StepUp.Enabled of GET v1/auth/configuration). */
  enabled: boolean
  /** The oldest sign-in in seconds that still counts. */
  maxAge: number
}

/** The step-up of the API's discovery; off when the API does not report it (an older API, or no discovery yet). */
export function getStepUpSettings(): StepUpSettings {
  const stepUp = state.value.status === 'ready' ? state.value.authConfiguration?.StepUp : undefined
  const maxAge = stepUp?.MaxAge

  return {
    enabled: stepUp?.Enabled === true,
    maxAge: typeof maxAge === 'number' && Number.isFinite(maxAge) && maxAge > 0 ? maxAge : defaultStepUpMaxAge,
  }
}

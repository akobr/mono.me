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

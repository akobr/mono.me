import { storeToRefs } from 'pinia'
import { useSessionStore } from '@/stores/session'

/** The signed-in user and the sign-in/out actions. */
export function useAuth() {
  const session = useSessionStore()
  const { user, status, account } = storeToRefs(session)
  return { user, status, account, signIn: session.signIn, signOut: session.signOut }
}

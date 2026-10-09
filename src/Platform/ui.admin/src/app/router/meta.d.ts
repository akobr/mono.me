import 'vue-router'

declare module 'vue-router' {
  interface RouteMeta {
    /** Reachable without signing in (login, callback, unsupported, invitation landing). */
    public?: boolean
    /** 'bare' renders a centered card without the dashboard shell. */
    layout?: 'default' | 'bare'
    /** Reachable by a signed-in user who has no Storyteller account yet (onboarding, invitations). */
    allowWithoutAccount?: boolean
  }
}

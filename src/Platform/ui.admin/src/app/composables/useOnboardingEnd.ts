import { ref, shallowRef } from 'vue'
import { useRouter } from 'vue-router'
import { createAccount, getAccount, joinExhibition, type Account, type Exhibition } from '@42for.net/2splatform.sdk'
import type { OrganizationForm } from '@/forms/names.schema'
import { useSessionStore } from '@/stores/session'
import { onboardingTexts as texts } from '@/utils/onboarding'

/** `working`: a request of the end is in flight. `joinFailed`: the account exists, the join did not work. */
export type OnboardingEndState = 'idle' | 'working' | 'joinFailed'

const projectPath = (organization: string, project: string) => `/${organization}/${project}/default/annotations`

/**
 * The end of the onboarding wizard (424-267, spec 3.6): an own organization, an own organization with the exhibition,
 * or the exhibition only. The account that the API answers stays here until the wizard leaves: an account in the
 * session makes the auth guard send every further navigation of /onboarding to "/".
 */
export function useOnboardingEnd(exhibition: () => Exhibition | undefined) {
  const router = useRouter()
  const session = useSessionStore()
  const toast = useToast()

  const state = ref<OnboardingEndState>('idle')
  /** Why the join failed once the account existed; the page shows it in place of the steps. */
  const joinError = shallowRef<unknown>()
  /** Why "Continue with the exhibition only" failed without leaving an account. */
  const exhibitionOnlyError = shallowRef<unknown>()
  /** In joinFailed: Try again is in flight, or the wizard is leaving. */
  const retrying = ref(false)

  let account: Account | undefined
  /** The project the user created, when they did. */
  let ownPath: string | undefined

  /** The session gets the account immediately before the navigation that leaves the wizard, and only here. */
  async function leave(path: string, joined?: Exhibition): Promise<void> {
    if (joined) {
      // With an own project the wizard ends there, so the toast is the way to the exhibition.
      const open = ownPath
        ? [
            {
              label: texts.end.open,
              to: projectPath(joined.Organization, joined.Project),
              color: 'neutral' as const,
              variant: 'outline' as const,
            },
          ]
        : undefined
      toast.add({
        title: texts.end.joined,
        color: 'success',
        icon: 'i-lucide-party-popper',
        ...(open ? { duration: 10_000, actions: open } : {}),
      })
    }

    session.setAccount(account!)
    try {
      await router.replace(path)
    } catch {
      // The navigation itself failed (a page that did not load, for one). The account exists and is in the session, so
      // the wizard cannot go on; a full load of the target ends it as well.
      window.location.replace(router.resolve(path).href)
    }
  }

  /** The join of a user who has an account by now. Its failure is never thrown: it belongs to the page, not the form. */
  async function join(target: Exhibition): Promise<void> {
    try {
      account = await joinExhibition()
    } catch (error) {
      joinError.value = error
      state.value = 'joinFailed'
      retrying.value = false
      return
    }

    await leave(ownPath ?? projectPath(target.Organization, target.Project), target)
  }

  /**
   * The submit of the organization form. createAccount comes first: the join would create a bare account, and
   * createAccount refuses an existing one. Its failure is thrown for the form to show.
   */
  async function createOrganization(values: OrganizationForm, withExhibition: boolean): Promise<void> {
    if (state.value !== 'idle') {
      return
    }

    const target = withExhibition ? exhibition() : undefined
    state.value = 'working'
    exhibitionOnlyError.value = undefined

    try {
      account = await createAccount({
        Organization: values.organization,
        Project: values.project,
        ProjectDescription: values.description?.trim() || undefined,
      })
    } catch (error) {
      state.value = 'idle'
      throw error
    }

    ownPath = projectPath(values.organization, values.project)
    await (target ? join(target) : leave(ownPath))
  }

  /** "Continue with the exhibition only": the join creates the account. */
  async function joinOnly(): Promise<void> {
    const target = exhibition()
    if (state.value !== 'idle' || !target) {
      return
    }

    state.value = 'working'
    exhibitionOnlyError.value = undefined

    try {
      account = await joinExhibition()
    } catch (error) {
      // The API creates the account before it joins. With an account the form could create nothing any more
      // (createAccount refuses it), so the wizard goes on from joinFailed; without one the button tries again.
      account = await getAccount().catch(() => undefined)
      if (account) {
        joinError.value = error
        state.value = 'joinFailed'
      } else {
        exhibitionOnlyError.value = error
        state.value = 'idle'
      }
      return
    }

    await leave(projectPath(target.Organization, target.Project), target)
  }

  /** "Try again" of joinFailed. */
  async function retryJoin(): Promise<void> {
    const target = exhibition()
    if (state.value !== 'joinFailed' || retrying.value || !target) {
      return
    }

    retrying.value = true
    await join(target)
  }

  /** "Continue without the exhibition" of joinFailed: to the new project, or to /orgs after a join only. */
  async function continueWithout(): Promise<void> {
    if (state.value !== 'joinFailed' || retrying.value) {
      return
    }

    retrying.value = true
    await leave(ownPath ?? '/orgs')
  }

  return { state, joinError, exhibitionOnlyError, retrying, createOrganization, joinOnly, retryJoin, continueWithout }
}

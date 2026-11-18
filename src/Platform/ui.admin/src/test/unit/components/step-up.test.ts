/* eslint-disable vue/one-component-per-file -- the screens are mounted next to the dialog in small hosts */
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { createApp, defineComponent, h, ref, type Component, type SetupContext } from 'vue'
import { useToast } from '@nuxt/ui/composables'
import { http, HttpResponse } from 'msw'
import { setupServer } from 'msw/node'
import { configureStoryteller, deleteKeyValue, getKeyValue, setMemberRole } from '@42for.net/2splatform.sdk'
import type { AuthService, StepUpReturn } from '@/boot/auth'
import { setBootState } from '@/boot/state'
import MembersTable from '@/components/access/MembersTable.vue'
import OneTimeSecretDialog from '@/components/access/OneTimeSecretDialog.vue'
import TrustedIssuerDialog from '@/components/access/TrustedIssuerDialog.vue'
import ConfigurationResolved from '@/components/configuration/ConfigurationResolved.vue'
import KeyValueBulkBar from '@/components/key-values/KeyValueBulkBar.vue'
import KeyValueCreateDialog from '@/components/key-values/KeyValueCreateDialog.vue'
import KeyValueDetail from '@/components/key-values/KeyValueDetail.vue'
import KeyValueImportDialog from '@/components/key-values/KeyValueImportDialog.vue'
import KeyValueVersions from '@/components/key-values/KeyValueVersions.vue'
import StepUpDialog from '@/components/shell/StepUpDialog.vue'
import { isSignInStale, isSignInTooOld, signedInSince, useStepUpStore } from '@/composables/useStepUp'
import { hasUnsavedChanges, leaveWithoutPrompt } from '@/composables/useUnsavedChanges'
import { mockHandlers } from '@/mock/all-handlers'
import { createSeed, loadDb } from '@/mock/db'
import { setStaleSignIn } from '@/mock/step-up'
import ProjectMachinesPage from '@/pages/[org]/[project]/machines.vue'
import ProjectMembersPage from '@/pages/[org]/[project]/members.vue'
import OrganizationMembersPage from '@/pages/[org]/members.vue'
import { mountScreen, settle } from '../../helpers'

// The step-up of the admin UI (424-264): one confirmation however many requests failed, max_age=0 back to the same
// route, the loop guard, the client hint, the Reveal repeated once after the return, and the bulk items.

// A page with unsaved changes, or what the mounted editors and dialogs report themselves.
const unsaved = vi.hoisted(() => ({ value: false }))
vi.mock('@/composables/useUnsavedChanges', async (importOriginal) => {
  const original = await importOriginal<typeof import('@/composables/useUnsavedChanges')>()
  return { ...original, hasUnsavedChanges: () => unsaved.value || original.hasUnsavedChanges() }
})

// the navbar needs the dashboard shell; the stand-in shows what the pages put on its right (New machine, Invite)
vi.mock('@/components/shell/AppNavbar.vue', async () => {
  const vue = await import('vue')
  return {
    default: {
      name: 'AppNavbar',
      props: ['title'],
      setup:
        (props: { title?: string }, { slots }: SetupContext) =>
        () =>
          vue.h('header', { 'data-navbar': props.title }, slots.right?.()),
    },
  }
})

const server = setupServer(...mockHandlers)
beforeAll(() => server.listen({ onUnhandledRequest: 'bypass' }))
afterAll(() => server.close())

const minute = 60_000

interface FakeAuth extends AuthService {
  authTime: number | undefined
  stepUpReturn: StepUpReturn | undefined
  signIn: ReturnType<typeof vi.fn<AuthService['signIn']>>
  signOut: ReturnType<typeof vi.fn<AuthService['signOut']>>
}

function fakeAuth(): FakeAuth {
  const auth: FakeAuth = {
    kind: 'mock',
    authTime: Date.now(),
    stepUpReturn: undefined,
    getUser: () => ({ id: 'user_1', email: 'ada@example.com', name: 'Ada' }),
    getAccessToken: async () => 'token',
    getAuthTime: async () => auth.authTime,
    signIn: vi.fn<AuthService['signIn']>(async () => {}),
    signOut: vi.fn<AuthService['signOut']>(async () => {}),
    consumeReturnTo: () => undefined,
    getStepUpReturn: () => auth.stepUpReturn,
  }
  return auth
}

let auth: FakeAuth
const mounted: { host: { unmount: () => void } }[] = []

function boot(stepUp: { Enabled?: boolean; MaxAge?: number } = { Enabled: true, MaxAge: 600 }) {
  setBootState({ status: 'ready', auth, authConfiguration: { Provider: 'AuthKit', StepUp: stepUp } })
}

beforeEach(() => {
  auth = fakeAuth()
  boot()
  unsaved.value = false
  // as the app does (boot/api.ts): a step-up 401 asks the coordinator
  configureStoryteller({
    baseUrl: 'http://api.test/api',
    getAccessToken: () => 'test-token',
    onStepUpRequired: () => void useStepUpStore().ask(),
  })
})

afterEach(() => {
  mounted.splice(0).forEach((view) => view.host.unmount())
  server.resetHandlers()
  setStaleSignIn(false)
  document.body.innerHTML = ''
})

/** The dialog next to a screen, as App.vue mounts it. */
const withDialog = (screen?: Component, props: Record<string, unknown> = {}) =>
  defineComponent({
    setup() {
      return () => h('div', [h(StepUpDialog), screen ? h(screen, props) : null])
    },
  })

async function mountDialog(
  screen?: Component,
  props: Record<string, unknown> = {},
  path = '/acme/billing/default/key-values?key=db.password',
  accessMap?: Record<string, string>,
) {
  const view = await mountScreen(withDialog(screen, props), {}, { path, accessMap })
  mounted.push(view)
  await settle()
  return view
}

const dialogs = () => [...document.body.querySelectorAll('[role="dialog"]')]
const buttons = (label: string) =>
  [...document.body.querySelectorAll('button')].filter((candidate) => candidate.textContent?.trim() === label)
const button = (label: string) => buttons(label)[0]
/** A button of the step-up's confirmation, not of the dialog or the screen below it. */
const confirmationButton = (label: string) =>
  [...(dialogs().find((dialog) => dialog.textContent?.includes("Confirm it's you"))?.querySelectorAll('button') ?? [])].find(
    (candidate) => candidate.textContent?.trim() === label,
  )
const text = () => document.body.textContent ?? ''

/** Types into a field as a person does. */
async function type(field: HTMLInputElement | HTMLTextAreaElement | null, value: string) {
  field!.value = value
  field!.dispatchEvent(new Event('input'))
  await settle()
}

const revealKey = '2s-admin:step-up-reveal'
const lostChanges = 'Your unsaved changes on this page are lost.'

/** The return of a step-up whose sign-in moved: the token's auth_time (now, by default) is newer than the one before. */
const recentReturn = (): StepUpReturn => ({
  startedAt: Date.now() - minute,
  previousAuthTime: Date.now() - 60 * minute,
  returnedAt: Date.now(),
})

/** The page loads again: the screens go, and the dialog and the screen mount anew. */
async function reload(screen?: Component, props: Record<string, unknown> = {}) {
  mounted.splice(0).forEach((view) => view.host.unmount())
  document.body.innerHTML = ''
  return await mountDialog(screen, props)
}

// The toasts are global state of Nuxt UI.
const returnToasts = () =>
  createApp({}).runWithContext(() => useToast().toasts.value.filter((toast) => toast.id === 'step-up-return'))
const clearToasts = () => createApp({}).runWithContext(() => useToast().clear())

/** A dialog that opens after it mounted, as its page opens it. */
function opened(dialog: Component, props: Record<string, unknown> = {}) {
  const open = ref(false)
  const host = defineComponent({
    setup: () => () => h(dialog, { ...props, open: open.value, 'onUpdate:open': (value: boolean) => (open.value = value) }),
  })
  return { host, open }
}

describe('the step-up coordinator', () => {
  it('opens one confirmation however many requests failed, and Cancel closes it without a sign-in', async () => {
    setStaleSignIn(true)
    await mountDialog()

    const failures = await Promise.allSettled([
      getKeyValue('acme', 'billing', 'default', 'db.password', { reveal: true }),
      getKeyValue('acme', 'billing', 'default', 'db.password', { reveal: true }),
      setMemberRole('acme.billing', 'user_01MOCKGRACE', { Role: 'Reader' }),
    ])
    await settle()

    expect(failures.map((failure) => failure.status)).toEqual(['rejected', 'rejected', 'rejected'])
    expect(dialogs()).toHaveLength(1)
    expect(document.body.textContent).toContain("Confirm it's you")
    expect(document.body.textContent).toContain(
      'For your security, this needs a recent sign-in. Sign in again to continue; you come back to this page.',
    )
    expect(document.body.textContent).not.toContain('unsaved changes')

    button('Cancel')!.click()
    await settle()

    expect(useStepUpStore().open).toBe(false)
    expect(auth.signIn).not.toHaveBeenCalled()
  })

  it('signs in again with max_age=0, the current route as returnTo and the email as login hint', async () => {
    unsaved.value = true
    await mountDialog()

    await useStepUpStore().ask()
    await settle()
    expect(document.body.textContent).toContain('Your unsaved changes on this page are lost.')

    button('Sign in again')!.click()
    await settle()

    expect(auth.signIn).toHaveBeenCalledExactlyOnceWith({
      returnTo: '/acme/billing/default/key-values?key=db.password',
      loginHint: 'ada@example.com',
      maxAge: 0,
    })
  })

  it('shows the error of a failed sign-in and lets the person try again', async () => {
    auth.signIn.mockRejectedValueOnce(new Error('The sign-in could not start.'))
    await mountDialog()

    await useStepUpStore().ask()
    await settle()
    button('Sign in again')!.click()
    await settle()

    expect(document.body.textContent).toContain('The sign-in could not start.')
    expect(useStepUpStore().redirecting).toBe(false)
    expect(button('Sign in again')).toBeDefined()
  })

  it('signs in also when the browser refuses to keep the Reveal for the return', async () => {
    const setItem = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new DOMException('full', 'QuotaExceededError')
    })
    try {
      await mountDialog()
      const stepUp = useStepUpStore()
      stepUp.rememberReveal({ scope: 'acme/billing/default', key: 'db.password' })

      await stepUp.ask()
      await settle()
      button('Sign in again')!.click()
      await settle()

      expect(auth.signIn).toHaveBeenCalledOnce()
      expect(stepUp.failure).toBeUndefined()
    } finally {
      setItem.mockRestore()
      // the sign-in never leaves the page here: the browser asks before unsaved changes go again
      leaveWithoutPrompt(false)
    }
  })

  // The loop guard: back from a step-up whose sign-in did not move, another redirect would not help.
  it('says the sign-in could not be confirmed when the return brought no newer sign-in', async () => {
    const before = Date.now() - 30 * minute
    auth.stepUpReturn = { startedAt: Date.now() - minute, previousAuthTime: before, returnedAt: Date.now() }
    auth.authTime = before
    await mountDialog()

    await useStepUpStore().ask()
    await settle()

    expect(useStepUpStore().mode).toBe('failed')
    expect(document.body.textContent).toContain('Your sign-in could not be confirmed')
    expect(document.body.textContent).toContain('Sign out and sign in again.')
    expect(button('Sign in again')).toBeUndefined()

    button('Sign out')!.click()
    await settle()
    expect(auth.signOut).toHaveBeenCalledOnce()
  })

  it('asks again after a return whose sign-in moved, for a window that ran out since', async () => {
    const before = Date.now() - 30 * minute
    auth.stepUpReturn = { startedAt: Date.now() - minute, previousAuthTime: before, returnedAt: Date.now() }
    auth.authTime = Date.now() - 20 * minute
    await mountDialog()

    await useStepUpStore().ask()
    await settle()

    expect(useStepUpStore().mode).toBe('confirm')
    expect(button('Sign in again')).toBeDefined()
  })

  it('compares with the start when the sign-in before the step-up is not known', () => {
    const returned = { startedAt: 10_000, returnedAt: 20_000 }

    expect(signedInSince(returned, 9_000)).toBe(false)
    expect(signedInSince(returned, 11_000)).toBe(true)
    expect(signedInSince({ ...returned, previousAuthTime: 5_000 }, 6_000)).toBe(true)
    expect(signedInSince(returned, undefined)).toBe(false)
  })

  it('says after the return that a change is not repeated, once', async () => {
    auth.stepUpReturn = {
      startedAt: Date.now() - minute,
      previousAuthTime: Date.now() - 30 * minute,
      returnedAt: Date.now(),
    }
    clearToasts()
    await mountDialog()

    expect(document.body.textContent).toContain('You signed in again')
    expect(document.body.textContent).toContain('Repeat the change if it did not finish.')

    // the app mounts the dialog again (another layout): no second toast
    await mountDialog()
    expect(returnToasts()).toHaveLength(1)
    expect(await useStepUpStore().takeReturnNotice()).toBe(false)
  })

  // The loop guard's error says what happened when the API asks again; a toast that the person signed in would not.
  it('says nothing after a return that brought no newer sign-in', async () => {
    const before = Date.now() - 30 * minute
    auth.stepUpReturn = { startedAt: Date.now() - minute, previousAuthTime: before, returnedAt: Date.now() }
    auth.authTime = before
    clearToasts()
    await mountDialog()

    expect(returnToasts()).toHaveLength(0)
    expect(text()).not.toContain('You signed in again')
  })

  // Back on the provider's page may bring the page back from the back/forward cache as the redirect left it.
  it('works again when Back restores the page from the back/forward cache after Sign in again', async () => {
    await mountDialog(KeyValueDetail, { keyName: 'db.password' })
    button('Set new value')!.click()
    await settle()
    await type(document.body.querySelector('textarea'), 'n3w-s3cret')
    const stepUp = useStepUpStore()
    const unloadPrompted = () => {
      const event = new Event('beforeunload', { cancelable: true })
      window.dispatchEvent(event)
      return event.defaultPrevented
    }
    expect(unloadPrompted()).toBe(true)

    stepUp.rememberReveal({ scope: 'acme/billing/default', key: 'db.password' })
    await stepUp.ask()
    await settle()
    expect(text()).toContain(lostChanges)
    button('Sign in again')!.click()
    await settle()

    // the redirect is under way: the confirmation waits, and the browser does not ask before the page goes
    expect(stepUp.redirecting).toBe(true)
    expect(confirmationButton('Cancel')!.disabled).toBe(true)
    expect(sessionStorage.getItem(revealKey)).not.toBeNull()
    expect(unloadPrompted()).toBe(false)

    // a page shown again from the cache, not one loaded anew
    window.dispatchEvent(Object.assign(new Event('pageshow'), { persisted: false }))
    await settle()
    expect(stepUp.redirecting).toBe(true)

    window.dispatchEvent(Object.assign(new Event('pageshow'), { persisted: true }))
    await settle()

    expect(stepUp.redirecting).toBe(false)
    expect(confirmationButton('Cancel')!.disabled).toBe(false)
    expect(confirmationButton('Sign in again')!.disabled).toBe(false)
    expect(sessionStorage.getItem(revealKey)).toBeNull()
    expect(unloadPrompted()).toBe(true)

    confirmationButton('Cancel')!.click()
    await settle()
    expect(stepUp.open).toBe(false)
  })
})

describe('the client hint', () => {
  it('finds a sign-in stale a minute before the window ends, only while the API has a step-up', async () => {
    const now = Date.now()

    auth.authTime = now - 8 * minute
    expect(await isSignInStale(now)).toBe(false)
    auth.authTime = now - 9 * minute - 1
    expect(await isSignInStale(now)).toBe(true)
    auth.authTime = undefined
    expect(await isSignInStale(now)).toBe(true)

    boot({ Enabled: false, MaxAge: 600 })
    expect(await isSignInStale(now)).toBe(false)
    // an API without the step-up in its discovery
    setBootState({ status: 'ready', auth, authConfiguration: { Provider: 'AuthKit' } })
    expect(await isSignInStale(now)).toBe(false)
  })

  it('keeps half of a short window, and counts the return of a step-up whose sign-in moved', async () => {
    const now = Date.now()
    boot({ Enabled: true, MaxAge: 60 })

    auth.authTime = now - 29_000
    expect(await isSignInStale(now)).toBe(false)
    auth.authTime = now - 31_000
    expect(await isSignInStale(now)).toBe(true)

    // the provider's clock behind the browser's: the fresh sign-in looks old, the return does not
    boot()
    auth.authTime = now - 15 * minute
    auth.stepUpReturn = { startedAt: now - minute, previousAuthTime: now - 60 * minute, returnedAt: now - 10_000 }
    expect(await isSignInStale(now)).toBe(false)
  })

  it('finds a sign-in too old for a read only past the window itself, without the margin', async () => {
    const now = Date.now()

    auth.authTime = now - 9.5 * minute
    expect(await isSignInStale(now)).toBe(true)
    expect(await isSignInTooOld(now)).toBe(false)
    auth.authTime = now - 10 * minute
    expect(await isSignInTooOld(now)).toBe(false)
    auth.authTime = now - 10 * minute - 1
    expect(await isSignInTooOld(now)).toBe(true)
    auth.authTime = undefined
    expect(await isSignInTooOld(now)).toBe(true)

    boot({ Enabled: false, MaxAge: 600 })
    expect(await isSignInTooOld(now)).toBe(false)
  })

  it('runs the confirmation first when the sign-in is stale, and lets a recent one go on', async () => {
    await mountDialog()
    const stepUp = useStepUpStore()

    expect(await stepUp.ensureRecentSignIn()).toBe(true)
    expect(stepUp.open).toBe(false)

    auth.authTime = Date.now() - 20 * minute
    expect(await stepUp.ensureRecentSignIn()).toBe(false)
    await settle()
    expect(dialogs()).toHaveLength(1)
  })
})

describe('the screens', () => {
  it('repeats the Reveal of a secret once after the return, never before', async () => {
    setStaleSignIn(true)
    await mountDialog(KeyValueDetail, { keyName: 'db.password' })

    button('Reveal')!.click()
    await vi.waitFor(() => expect(useStepUpStore().open).toBe(true))
    await settle()
    expect(document.body.textContent).toContain('Sign in again to continue')

    button('Sign in again')!.click()
    await settle()
    expect(auth.signIn).toHaveBeenCalledOnce()
    expect(sessionStorage.getItem('2s-admin:step-up-reveal')).toBe(
      JSON.stringify({ scope: 'acme/billing/default', key: 'db.password' }),
    )

    // the return: the mock's step-up made the sign-in recent, and the page loads again
    mounted.splice(0).forEach((view) => view.host.unmount())
    document.body.innerHTML = ''
    setStaleSignIn(false)
    auth.stepUpReturn = {
      startedAt: Date.now() - minute,
      previousAuthTime: Date.now() - 60 * minute,
      returnedAt: Date.now(),
    }
    await mountDialog(KeyValueDetail, { keyName: 'db.password' })

    await vi.waitFor(() => expect(document.body.textContent).toContain('s3cret-billing-2'))
    expect(sessionStorage.getItem('2s-admin:step-up-reveal')).toBeNull()

    // once: the next visit masks the value again
    mounted.splice(0).forEach((view) => view.host.unmount())
    document.body.innerHTML = ''
    await mountDialog(KeyValueDetail, { keyName: 'db.password' })
    expect(document.body.textContent).not.toContain('s3cret-billing-2')
  })

  it('drops a pending Reveal on a visit that is not the return of the step-up', async () => {
    sessionStorage.setItem(
      '2s-admin:step-up-reveal',
      JSON.stringify({ scope: 'acme/billing/default', key: 'db.password' }),
    )
    await mountDialog(KeyValueDetail, { keyName: 'db.password' })

    expect(document.body.textContent).not.toContain('s3cret-billing-2')
    expect(sessionStorage.getItem('2s-admin:step-up-reveal')).toBeNull()
  })

  it('repeats the View of a secret version once after the return: that version only', async () => {
    const views: string[] = []
    server.use(
      // records the reads of a version and returns nothing, so the mock API behind it answers
      http.get('*/api/v1/:org/:project/:view/key-values/:key/versions/:version', ({ request }) => {
        const url = new URL(request.url)
        views.push(`${url.pathname}${url.search}`)
      }),
    )
    setStaleSignIn(true)
    await mountDialog(KeyValueVersions, { keyName: 'db.password' })
    await vi.waitFor(() => expect(buttons('Reveal')).toHaveLength(2))

    // the versions newest first: v2, then v1
    buttons('Reveal')[1]!.click()
    await vi.waitFor(() => expect(useStepUpStore().open).toBe(true))
    await settle()
    button('Sign in again')!.click()
    await settle()
    expect(sessionStorage.getItem(revealKey)).toBe(
      JSON.stringify({ scope: 'acme/billing/default', key: 'db.password', version: 1 }),
    )

    // the return
    setStaleSignIn(false)
    auth.stepUpReturn = recentReturn()
    views.length = 0
    await reload(KeyValueVersions, { keyName: 'db.password' })

    await vi.waitFor(() => expect(text()).toContain('s3cret-billing-1'))
    expect(text()).not.toContain('s3cret-billing-2')
    expect(views).toEqual(['/api/v1/acme/billing/default/key-values/db.password/versions/1?reveal=true'])
    expect(sessionStorage.getItem(revealKey)).toBeNull()

    // once: the next visit shows no version
    await reload(KeyValueVersions, { keyName: 'db.password' })
    await vi.waitFor(() => expect(buttons('Reveal')).toHaveLength(2))
    expect(text()).not.toContain('s3cret-billing-1')
    expect(views).toHaveLength(1)
  })

  // The first screen after the return takes the pending Reveal, its own or not: it never replays on a later one.
  it('drops a pending Reveal that the first screen after the return does not show', async () => {
    auth.stepUpReturn = recentReturn()
    sessionStorage.setItem(revealKey, JSON.stringify({ scope: 'acme/billing/default', key: 'db.password', version: 1 }))

    // the value of the key, not that version
    await mountDialog(KeyValueDetail, { keyName: 'db.password' })
    await vi.waitFor(() => expect(button('Reveal')).toBeDefined())
    expect(sessionStorage.getItem(revealKey)).toBeNull()
    expect(text()).not.toContain('s3cret-billing')

    // later in the same page load, the screen of that version
    await reload(KeyValueVersions, { keyName: 'db.password' })
    await vi.waitFor(() => expect(buttons('Reveal')).toHaveLength(2))
    await settle()
    expect(text()).not.toContain('s3cret-billing-1')
  })

  it('asks before an import with secret rows is checked or imported, and says the pasted document is lost', async () => {
    const imports: string[] = []
    server.use(
      http.post('*/api/v1/:org/:project/:view/key-values/import', ({ request }) => {
        imports.push(new URL(request.url).search)
      }),
    )
    auth.authTime = Date.now() - 20 * minute
    await mountDialog(KeyValueImportDialog, { open: true })

    // public rows go on with the stale sign-in
    await type(
      document.body.querySelector('textarea'),
      JSON.stringify({ Items: [{ Key: 'app.name', Protection: 'Public', Value: 'Billing 2' }] }),
    )
    button('Next')!.click()
    await settle()
    button('Check')!.click()
    await vi.waitFor(() => expect(imports).toHaveLength(1))
    expect(useStepUpStore().open).toBe(false)

    // a secret row asks first, and sends nothing
    button('Back')!.click()
    await settle()
    await type(
      document.body.querySelector('textarea'),
      JSON.stringify({ Items: [{ Key: 'db.password', Protection: 'Standard', Value: 'n3w-s3cret' }] }),
    )
    button('Next')!.click()
    await settle()
    button('Check')!.click()
    await settle()

    expect(useStepUpStore().open).toBe(true)
    expect(text()).toContain(lostChanges)
    expect(imports).toHaveLength(1)
    confirmationButton('Cancel')!.click()
    await settle()

    // checked with a recent sign-in, imported once it is about to be too old: asks again, and sends nothing
    auth.authTime = Date.now()
    button('Check')!.click()
    await vi.waitFor(() => expect(button('Import')?.disabled).toBe(false))
    expect(imports).toHaveLength(2)
    auth.authTime = Date.now() - 20 * minute
    button('Import')!.click()
    await settle()

    expect(useStepUpStore().open).toBe(true)
    expect(imports).toHaveLength(2)
  })

  it('keeps Undo deletes for the keys a restore left deleted for the step-up, and asks', async () => {
    // the import deleted a secret and a public pair; the mock API refuses only the secret for the stale sign-in
    await deleteKeyValue('acme', 'billing', 'default', 'db.password')
    await deleteKeyValue('acme', 'billing', 'default', 'db.host')
    server.use(
      http.post('*/api/v1/:org/:project/:view/key-values/import', ({ request }) =>
        HttpResponse.json({
          IsDryRun: new URL(request.url).searchParams.get('dryRun') === 'true',
          IsComplete: true,
          Items: [
            { Key: 'app.name', Action: 'Update', Version: 2 },
            { Key: 'db.password', Action: 'Delete' },
            { Key: 'db.host', Action: 'Delete' },
          ],
        }),
      ),
    )
    await mountDialog(KeyValueImportDialog, { open: true })
    await type(
      document.body.querySelector('textarea'),
      JSON.stringify({ Items: [{ Key: 'app.name', Protection: 'Public', Value: 'Billing 2' }] }),
    )
    button('Next')!.click()
    await settle()
    button('Check')!.click()
    await vi.waitFor(() => expect(button('Import')?.disabled).toBe(false))
    button('Import')!.click()
    await vi.waitFor(() => expect(button('Undo deletes')).toBeDefined())

    setStaleSignIn(true)
    clearToasts()
    button('Undo deletes')!.click()
    await vi.waitFor(() => expect(useStepUpStore().open).toBe(true))
    await settle()

    expect(text()).toContain('1 of 2 keys were not restored')
    expect(text()).toContain('db.password')
    expect(button('Undo deletes')).toBeDefined()
    expect(dialogs().length).toBeGreaterThanOrEqual(2)
    const pairs = loadDb().keyValues!['acme/billing/default']!.pairs
    expect(pairs['db.host']!.Deleted).toBeUndefined()
    expect(pairs['db.password']!.Deleted).toBeDefined()
  })

  it('asks when New key gets a secret level with a stale sign-in, and says the typed key is lost', async () => {
    auth.authTime = Date.now() - 20 * minute
    await mountDialog(KeyValueCreateDialog, { open: true })
    await type(document.body.querySelector('input[placeholder="db.password"]'), 'db.new')
    expect(useStepUpStore().open).toBe(false)

    // "Secret" picks the project's default secret level
    document.body.querySelector<HTMLButtonElement>('[role="switch"]')!.click()
    await settle()

    expect(useStepUpStore().open).toBe(true)
    expect(text()).toContain(lostChanges)
  })

  it('counts credentials shown once as unsaved changes while they are open', async () => {
    await mountDialog(OneTimeSecretDialog, { secret: { title: 'Machine created', id: 'machine-1', apiKey: 'k3y' } })

    expect(hasUnsavedChanges()).toBe(true)
  })

  it('counts what a dialog holds as unsaved changes, not what it opened with', async () => {
    const { host, open } = opened(TrustedIssuerDialog, { names: [] })
    await mountDialog(host)
    expect(hasUnsavedChanges()).toBe(false)

    // the template fills the fields: nothing typed yet
    open.value = true
    await settle()
    expect(text()).toContain('Add a trusted issuer')
    expect(hasUnsavedChanges()).toBe(false)

    // the name, the first field
    await type(document.body.querySelector('[role="dialog"] input'), 'my-issuer')
    expect(hasUnsavedChanges()).toBe(true)

    open.value = false
    await settle()
    expect(hasUnsavedChanges()).toBe(false)
  })

  it.each([
    { action: 'New machine', page: ProjectMachinesPage, path: '/acme/billing/machines', title: 'New machine access' },
    { action: 'Invite', page: ProjectMembersPage, path: '/acme/billing/members', title: 'Invite people' },
    { action: 'Invite', page: OrganizationMembersPage, path: '/acme/members', title: 'Invite people' },
  ])('asks before $action on $path opens its dialog when the sign-in is about to be too old', async ({ action, page, path, title }) => {
    auth.authTime = Date.now() - 20 * minute
    // Ada owns acme and acme/billing
    await mountDialog(page, {}, path, createSeed('default').account!.AccessMap as Record<string, string>)
    await settle(6)

    button(action)!.click()
    await settle()
    expect(useStepUpStore().open).toBe(true)
    expect(text()).not.toContain(title)

    button('Cancel')!.click()
    await settle()
    auth.authTime = Date.now()
    button(action)!.click()
    await settle()
    expect(text()).toContain(title)
  })

  it('asks before a secret gets a new value when the sign-in is about to be too old', async () => {
    auth.authTime = Date.now() - 20 * minute
    await mountDialog(KeyValueDetail, { keyName: 'db.password' })

    button('Set new value')!.click()
    await settle()

    expect(useStepUpStore().open).toBe(true)
    expect(document.body.querySelector('textarea')).toBeNull()

    button('Cancel')!.click()
    auth.authTime = Date.now()
    button('Set new value')!.click()
    await settle()
    expect(document.body.querySelector('textarea')).not.toBeNull()
  })

  it('asks for the keys a bulk restore left deleted for the step-up', async () => {
    // a deleted secret and a deleted public pair; the mock API refuses only the secret for the stale sign-in
    await deleteKeyValue('acme', 'billing', 'default', 'db.password')
    await deleteKeyValue('acme', 'billing', 'default', 'db.host')
    setStaleSignIn(true)
    const done = vi.fn()
    await mountDialog(KeyValueBulkBar, {
      keys: ['db.password', 'db.host'],
      state: 'Deleted',
      allowedLevels: ['Public', 'Standard'],
      onDone: done,
    })

    button('Restore')!.click()
    await vi.waitFor(() => expect(done).toHaveBeenCalledWith(['db.password']))
    await settle()

    expect(useStepUpStore().open).toBe(true)
    expect(dialogs()).toHaveLength(1)
    expect(document.body.textContent).toContain('1 of 2 keys were not done')
  })

  it('asks before a role change when the sign-in is about to be too old, and changes nothing', async () => {
    auth.authTime = Date.now() - 20 * minute
    const view = await mountDialog(MembersTable, { accessPointKey: 'acme.billing' })
    await settle(10)

    // Grace, a Contributor the Owner may change
    const grace = view.host
      .findAllComponents({ name: 'Select' })
      .find((select) => select.attributes('aria-label')?.includes('Grace') || select.html().includes('Grace'))
    expect(grace).toBeDefined()
    grace!.vm.$emit('update:modelValue', 'Reader')
    await settle()

    expect(useStepUpStore().open).toBe(true)
    expect(loadDb().accessPoints['acme.billing']?.AccessMap?.user_01MOCKGRACE).toBe('Contributor')
  })

  it('shows the step-up text when the API refuses a role change for the sign-in', async () => {
    setStaleSignIn(true)
    const view = await mountDialog(MembersTable, { accessPointKey: 'acme.billing' })
    await settle(10)

    const grace = view.host
      .findAllComponents({ name: 'Select' })
      .find((select) => select.attributes('aria-label')?.includes('Grace') || select.html().includes('Grace'))
    grace!.vm.$emit('update:modelValue', 'Reader')
    await vi.waitFor(() => expect(document.body.textContent).toContain('The role was not changed'))

    expect(document.body.textContent).toContain(
      'For your security, this needs a recent sign-in. Sign in again, then repeat it. A sign-in counts for 10 minutes.',
    )
    expect(useStepUpStore().open).toBe(true)
  })

  it('says on the Resolved tab that secret values are hidden until the person confirms', async () => {
    auth.authTime = Date.now() - 20 * minute
    await mountDialog(
      ConfigurationResolved,
      { annotationKey: 'exe.northwind.invoicing.prod' },
      '/acme/billing/default/annotations/exe.northwind.invoicing.prod',
    )

    expect(document.body.textContent).toContain('Secret values are hidden')
    expect(document.body.textContent).not.toContain('secret values are included')

    button("Confirm it's you")!.click()
    await settle()
    expect(useStepUpStore().open).toBe(true)
  })

  it('says on the Resolved tab that secret values are included with a recent sign-in', async () => {
    await mountDialog(
      ConfigurationResolved,
      { annotationKey: 'exe.northwind.invoicing.prod' },
      '/acme/billing/default/annotations/exe.northwind.invoicing.prod',
    )

    expect(document.body.textContent).toContain('secret values are included')
    expect(document.body.textContent).not.toContain('Secret values are hidden')
  })

  // The hint before a write asks a minute early; the API withholds only once the window itself ran out.
  it('says on the Resolved tab that secret values are included in the last minute of the window', async () => {
    auth.authTime = Date.now() - 9.25 * minute
    await mountDialog(
      ConfigurationResolved,
      { annotationKey: 'exe.northwind.invoicing.prod' },
      '/acme/billing/default/annotations/exe.northwind.invoicing.prod',
    )

    expect(await isSignInStale()).toBe(true)
    expect(text()).toContain('secret values are included')
    expect(text()).not.toContain('Secret values')
  })

  // The browser knows only when the response arrived: at the end of the window either answer may be wrong.
  it('says on the Resolved tab that secret values may be hidden at the end of the window', async () => {
    auth.authTime = Date.now() - 10 * minute
    await mountDialog(
      ConfigurationResolved,
      { annotationKey: 'exe.northwind.invoicing.prod' },
      '/acme/billing/default/annotations/exe.northwind.invoicing.prod',
    )

    await vi.waitFor(() => expect(text()).toContain('Secret values may be hidden'))
    expect(text()).not.toContain('secret values are included')
    button("Confirm it's you")!.click()
    await settle()
    expect(useStepUpStore().open).toBe(true)
  })

  it('tells again with every response of the Resolved tab, as the window runs out', async () => {
    const view = await mountDialog(
      ConfigurationResolved,
      { annotationKey: 'exe.northwind.invoicing.prod' },
      '/acme/billing/default/annotations/exe.northwind.invoicing.prod',
    )
    await vi.waitFor(() => expect(document.body.querySelector('[aria-label="Resolved configuration"]')).not.toBeNull())
    expect(text()).toContain('secret values are included')

    // the window ran out while the tab was open: the shown response still has the secrets
    auth.authTime = Date.now() - 20 * minute
    await settle()
    expect(text()).toContain('secret values are included')

    // the refetch has them withheld
    await view.queryClient.refetchQueries()
    await vi.waitFor(() => expect(text()).toContain('Secret values are hidden'))
  })
})

/* eslint-disable vue/one-component-per-file -- the screens are mounted next to the dialog in small hosts */
import { afterAll, afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { createApp, defineComponent, h, type Component } from 'vue'
import { useToast } from '@nuxt/ui/composables'
import { setupServer } from 'msw/node'
import { configureStoryteller, deleteKeyValue, getKeyValue, setMemberRole } from '@42for.net/2splatform.sdk'
import type { AuthService, StepUpReturn } from '@/boot/auth'
import { setBootState } from '@/boot/state'
import MembersTable from '@/components/access/MembersTable.vue'
import ConfigurationResolved from '@/components/configuration/ConfigurationResolved.vue'
import KeyValueBulkBar from '@/components/key-values/KeyValueBulkBar.vue'
import KeyValueDetail from '@/components/key-values/KeyValueDetail.vue'
import StepUpDialog from '@/components/shell/StepUpDialog.vue'
import { isSignInStale, signedInSince, useStepUpStore } from '@/composables/useStepUp'
import { mockHandlers } from '@/mock/all-handlers'
import { loadDb } from '@/mock/db'
import { setStaleSignIn } from '@/mock/step-up'
import { mountScreen, settle } from '../../helpers'

// The step-up of the admin UI (424-264): one confirmation however many requests failed, max_age=0 back to the same
// route, the loop guard, the client hint, the Reveal repeated once after the return, and the bulk items.

const unsaved = vi.hoisted(() => ({ value: false }))
vi.mock('@/composables/useUnsavedChanges', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/composables/useUnsavedChanges')>()),
  hasUnsavedChanges: () => unsaved.value,
}))

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
) {
  const view = await mountScreen(withDialog(screen, props), {}, { path })
  mounted.push(view)
  await settle()
  return view
}

const dialogs = () => [...document.body.querySelectorAll('[role="dialog"]')]
const button = (label: string) =>
  [...document.body.querySelectorAll('button')].find((candidate) => candidate.textContent?.trim() === label)

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
    // The toasts are global state of Nuxt UI.
    const returnToasts = () =>
      createApp({}).runWithContext(() => useToast().toasts.value.filter((toast) => toast.id === 'step-up-return'))
    createApp({}).runWithContext(() => useToast().clear())
    await mountDialog()

    expect(document.body.textContent).toContain('You signed in again')
    expect(document.body.textContent).toContain('Repeat the change if it did not finish.')

    // the app mounts the dialog again (another layout): no second toast
    await mountDialog()
    expect(returnToasts()).toHaveLength(1)
    expect(useStepUpStore().takeReturnNotice()).toBe(false)
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
})

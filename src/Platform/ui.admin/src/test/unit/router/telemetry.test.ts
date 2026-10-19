import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createMemoryHistory, createRouter, type Router } from 'vue-router'
import { capturePageview } from '@/boot/telemetry'
import { installTelemetryGuard } from '@/router/guards/telemetry'

vi.mock('@/boot/telemetry', () => ({ capturePageview: vi.fn() }))

describe('telemetry guard', () => {
  let router: Router

  beforeEach(() => {
    vi.mocked(capturePageview).mockClear()
    router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/:pathMatch(.*)*', component: { render: () => null } }],
    })
    installTelemetryGuard(router)
  })

  it('counts the first navigation, also to "/"', async () => {
    await router.push('/')

    expect(capturePageview).toHaveBeenCalledOnce()
  })

  it('counts every new path, but not a change of the query or the hash', async () => {
    await router.push('/orgs')
    await router.push('/acme/billing/default/annotations')
    await router.push('/acme/billing/default/annotations?q=type:sbt')
    await router.push('/acme/billing/default/annotations?q=type:sbt&mode=tree#top')
    await router.push('/acme/billing/settings')

    expect(capturePageview).toHaveBeenCalledTimes(3)
  })

  it('counts no failed, cancelled or duplicated navigation', async () => {
    const remove = router.beforeEach((to) => to.path !== '/blocked')

    // A cancelled first navigation (the auth guard on its way to AuthKit): the next one is still the first page.
    await router.push('/blocked')
    expect(capturePageview).not.toHaveBeenCalled()
    await router.push('/orgs')
    await router.push('/blocked')
    await router.push('/orgs')
    remove()

    expect(capturePageview).toHaveBeenCalledOnce()
  })
})

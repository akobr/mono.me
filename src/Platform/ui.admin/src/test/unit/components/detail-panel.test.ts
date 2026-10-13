import { afterEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h } from 'vue'
import { mount, type VueWrapper } from '@vue/test-utils'
import ui from '@nuxt/ui/vue-plugin'
import UApp from '@nuxt/ui/components/App.vue'
import DetailPanel from '@/components/shell/DetailPanel.vue'
import DetailPanelActions from '@/components/shell/DetailPanelActions.vue'
import { usePreferencesStore } from '@/stores/preferences'
import { settle } from '../../helpers'

let wrapper: VueWrapper | undefined

/** A detail as the pages build it, at 1024 px (lg): the panel buttons in the header, a field in the body. */
async function mountDetail() {
  const close = vi.fn()
  wrapper = mount(
    defineComponent({
      setup: () => () =>
        h(UApp, null, {
          default: () =>
            h(
              DetailPanel,
              { id: 'test-detail', title: 'Detail', onClose: close },
              { header: () => h('div', [h(DetailPanelActions)]), default: () => h('input', { 'aria-label': 'Title' }) },
            ),
        }),
    }),
    { attachTo: document.body, global: { plugins: [ui], stubs: { AppNavbar: true } } },
  )
  await settle()

  const panel = () => wrapper!.get('section')
  const button = (name: string) => wrapper!.get(`button[aria-label="${name}"]`)
  return { close, panel, button, separator: () => wrapper!.find('[role="separator"]') }
}

afterEach(() => {
  wrapper?.unmount()
  wrapper = undefined
})

describe('DetailPanel', () => {
  it('opens as an overlay and can be pinned and unpinned', async () => {
    const preferences = usePreferencesStore()
    const { panel, button } = await mountDetail()

    expect(panel().attributes('data-detail-mode')).toBe('overlay')
    expect(panel().attributes('aria-label')).toBe('Detail')

    await button('Pin the detail').trigger('click')
    expect(panel().attributes('data-detail-mode')).toBe('pinned')
    expect(preferences.state.detailPinned).toBe(true)

    await button('Unpin the detail').trigger('click')
    expect(panel().attributes('data-detail-mode')).toBe('overlay')
    expect(preferences.state.detailPinned).toBe(false)
  })

  it('opens in full screen with the navbar and returns to the remembered mode', async () => {
    const preferences = usePreferencesStore()
    preferences.state.detailPinned = true
    const { panel, button, separator } = await mountDetail()

    await button('Open in full screen').trigger('click')
    expect(panel().attributes('data-detail-mode')).toBe('full')
    expect(preferences.state.detailFullscreen).toBe(true)
    expect(wrapper!.findComponent({ name: 'AppNavbar' }).exists()).toBe(true)
    expect(separator().exists()).toBe(false)
    expect(wrapper!.find('button[aria-label="Unpin the detail"]').exists()).toBe(false)

    await button('Exit full screen').trigger('click')
    expect(panel().attributes('data-detail-mode')).toBe('pinned')
    expect(wrapper!.findComponent({ name: 'AppNavbar' }).exists()).toBe(false)
  })

  it('resizes with the arrow keys within the limits and resets on double-click', async () => {
    const preferences = usePreferencesStore()
    const { panel, separator } = await mountDetail()

    expect(separator().attributes('aria-valuenow')).toBe('40')
    expect(panel().attributes('style')).toContain('width: 40%')

    await separator().trigger('keydown', { key: 'ArrowLeft' })
    expect(preferences.state.detailWidth).toBe(42)
    expect(panel().attributes('style')).toContain('width: 42%')

    preferences.state.detailWidth = 69
    await separator().trigger('keydown', { key: 'ArrowLeft' })
    expect(preferences.state.detailWidth).toBe(70)

    preferences.state.detailWidth = 26
    await separator().trigger('keydown', { key: 'ArrowRight' })
    await separator().trigger('keydown', { key: 'ArrowRight' })
    expect(preferences.state.detailWidth).toBe(25)

    await separator().trigger('dblclick')
    expect(preferences.state.detailWidth).toBe(40)
  })

  it('closes on Esc from the page, but not from a field', async () => {
    const { close } = await mountDetail()

    wrapper!.get('input').element.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))
    expect(close).not.toHaveBeenCalled()

    document.body.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))
    expect(close).toHaveBeenCalledOnce()
  })
})

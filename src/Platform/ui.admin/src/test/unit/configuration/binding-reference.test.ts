import { computed, defineComponent, h } from 'vue'
import { afterEach, describe, expect, it } from 'vitest'
import BindingReference from '@/components/configuration/BindingReference.vue'
import { provideBindingAssist, staticBindingAssist } from '@/composables/useBindingAssist'
import { catalogView } from '@/utils/binding-language'
import { mountInApp, settle } from '../../helpers'

const catalog = catalogView({
  Functions: catalogView().functions,
  Sources: [
    {
      Name: 'primaryVault',
      Summary: 'Secrets of the vault.',
      IsDefault: false,
      IsSecret: true,
      Examples: [{ Expression: '@(db.password, primaryVault)', Description: 'The secret `db--password`.' }],
    },
  ],
})

/** The reference under a provider of the catalog, opened. */
async function open(props: Record<string, unknown> = {}) {
  const inserted: string[] = []
  const Host = defineComponent({
    setup() {
      provideBindingAssist({ ...staticBindingAssist, catalog: computed(() => catalog) })
      return () => h(BindingReference, { ...props, onInsert: (expression: string) => inserted.push(expression) })
    },
  })
  const view = mountInApp(Host)
  await settle()
  await view.host.get('button').trigger('click')
  await settle()
  return {
    view,
    inserted,
    panel: () => document.body.querySelector<HTMLElement>('[aria-label="Binding reference"][role="region"]'),
  }
}

const entries = () =>
  [...document.body.querySelectorAll('[data-binding]')].map((entry) => entry.getAttribute('data-binding'))

describe('BindingReference (424-10)', () => {
  afterEach(() => {
    document.body.innerHTML = ''
  })

  it('lists the expressions, the functions and sources of the server, and the object bindings', async () => {
    const { view, panel } = await open()

    expect([...panel()!.querySelectorAll('[data-section] > h3')].map((title) => title.textContent)).toEqual([
      'Expressions',
      'Functions',
      'Sources',
      'Object bindings',
    ])
    expect(entries()).toEqual([
      'path',
      'sourced',
      'function',
      'interpolation',
      'math',
      'function-annotation',
      'function-config',
      'source-primaryVault',
      'jlogic',
      'jsone',
    ])
    const vault = panel()!.querySelector('[data-binding="source-primaryVault"]')!
    expect(vault.textContent).toContain('secret')
    expect(vault.textContent).toContain('@(path.to.value, primaryVault)')
    expect(panel()!.querySelector('[data-binding="function-annotation"]')!.textContent).toContain(
      'Responsibility, Unit, Subject, Usage, Context, Execution',
    )
    // Without insertable, the examples can only be copied.
    expect([...panel()!.querySelectorAll('button')].some((button) => button.textContent === 'Use')).toBe(false)
    view.host.unmount()
  })

  it('filters by name, text and example', async () => {
    const { view, panel } = await open()
    const input = panel()!.querySelector('input')!

    input.value = 'vault'
    input.dispatchEvent(new Event('input'))
    await settle()
    expect(entries()).toEqual(['sourced', 'source-primaryVault'])

    input.value = 'nothing like this'
    input.dispatchEvent(new Event('input'))
    await settle()
    expect(entries()).toEqual([])
    expect(panel()!.textContent).toContain('Nothing matches')
    view.host.unmount()
  })

  it('emits the example of a string form with Use and closes', async () => {
    const { view, inserted, panel } = await open({ insertable: true })
    const config = panel()!.querySelector('[data-binding="function-config"]')!
    const use = [...config.querySelectorAll('button')].find((button) => button.textContent === 'Use')!

    use.click()
    await settle()

    expect(inserted).toEqual(['@config("/maxPrice")'])
    expect(panel()).toBeNull()
    view.host.unmount()
  })

  // The object bindings are JSON objects, not strings to put into a value.
  it('offers no Use for the object bindings', async () => {
    const { view, panel } = await open({ insertable: true })
    const jsone = panel()!.querySelector('[data-binding="jsone"]')!

    expect([...jsone.querySelectorAll('button')].some((button) => button.textContent === 'Use')).toBe(false)
    expect(jsone.querySelector('pre')?.textContent).toContain('"$binding": "jsone"')
    view.host.unmount()
  })
})

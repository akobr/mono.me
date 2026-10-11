import { describe, expect, it } from 'vitest'
import SchemaForm from '@/components/configuration/SchemaForm.vue'
import { mountInApp, settle } from '../../helpers'

const schema = {
  type: 'object',
  properties: {
    title: { type: 'string', title: 'Display title', description: 'Shown to **users**.' },
    notes: { type: 'string', 'x-multiline': true },
    apiKey: { type: 'string', format: 'password' },
    homepage: { type: 'string', format: 'uri' },
    retries: { type: 'integer', minimum: 0, maximum: 10 },
    enabled: { type: 'boolean' },
    mode: { type: 'string', enum: ['batch', 'stream'] },
    region: {
      type: 'string',
      oneOf: [
        { const: 'eu', title: 'Europe' },
        { const: 'us', title: 'United States' },
      ],
    },
    start: { type: 'string', format: 'date' },
    tags: { type: 'array', items: { type: 'string' } },
    tiers: { type: 'array', items: { type: 'object', properties: { name: { type: 'string' } } } },
    headers: { type: 'object', additionalProperties: { type: 'string' } },
    schedule: { type: 'object', properties: { cron: { type: 'string' } } },
    advanced: { type: 'object', patternProperties: { '^x-': {} } },
    deprecatedField: { type: 'string', deprecated: true },
  },
  required: ['retries'],
}

function mountForm(props: Record<string, unknown>) {
  const updates: unknown[] = []
  const view = mountInApp(SchemaForm, {
    schema,
    ...props,
    'onUpdate:modelValue': (value: unknown) => {
      updates.push(value)
      void view.setProps({ modelValue: value })
    },
  })
  return { view, updates }
}

const field = (view: ReturnType<typeof mountInApp>, pointer: string) => view.host.find(`[data-path="${pointer}"]`)

describe('SchemaForm (JSON Forms with the Nuxt UI renderers)', () => {
  it('renders a control for every kind of property', async () => {
    const { view } = mountForm({ modelValue: { retries: 1, tiers: [{ name: 'gold' }], headers: { 'X-A': '1' } } })
    await settle(6)

    expect(field(view, '/title').text()).toContain('Display title')
    expect(field(view, '/title').html()).toContain('<strong>users</strong>')
    expect(field(view, '/notes').find('textarea').exists()).toBe(true)
    expect(field(view, '/apiKey').find('input[type="password"]').exists()).toBe(true)
    expect(field(view, '/homepage').find('input[type="url"]').exists()).toBe(true)
    expect(field(view, '/retries').text()).toContain('*')
    expect(field(view, '/enabled').find('[role="switch"]').exists()).toBe(true)
    expect(field(view, '/start').find('input[type="date"]').exists()).toBe(true)
    expect(field(view, '/tiers').text()).toContain('gold')
    expect(field(view, '/headers').find('input[aria-label="Key"]').element).toHaveProperty('value', 'X-A')
    expect(view.host.text()).toContain('Schedule')
    expect(field(view, '/advanced').text()).toContain('Advanced')
    expect(field(view, '/deprecatedField').text()).toContain('Deprecated')
    view.host.unmount()
  })

  it('replaces an object array with a binding expression through the fx toggle', async () => {
    const { view, updates } = mountForm({ modelValue: { retries: 1, tiers: [{ name: 'gold' }] } })
    await settle(6)

    await field(view, '/tiers')
      .findAll('button')
      .find((button) => button.text() === 'fx')!
      .trigger('click')
    await settle()

    expect(updates.at(-1)).toEqual({ retries: 1, tiers: '@' })
    view.host.unmount()
  })

  it('emits the edited document and removes emptied values', async () => {
    const { view, updates } = mountForm({ modelValue: { retries: 1, title: 'x' } })
    await settle(6)

    await field(view, '/title').find('input').setValue('')
    await settle()

    expect(updates.at(-1)).toEqual({ retries: 1 })
    view.host.unmount()
  })

  it('shows inherited values as placeholders and copies them on Override', async () => {
    const { view, updates } = mountForm({
      modelValue: { retries: 1 },
      effective: { retries: 1, title: 'Inherited title' },
    })
    await settle(6)

    const title = field(view, '/title')
    expect(title.text()).toContain('inherited')
    expect(title.find('input').attributes('placeholder')).toBe('Inherited title')

    await title
      .findAll('button')
      .find((button) => button.text() === 'Override')!
      .trigger('click')
    await settle()

    expect(updates.at(-1)).toEqual({ retries: 1, title: 'Inherited title' })
    view.host.unmount()
  })

  it('offers Reset to inherited for own values that the effective document also has', async () => {
    const { view, updates } = mountForm({ modelValue: { retries: 4 }, effective: { retries: 4 } })
    await settle(6)

    await field(view, '/retries')
      .findAll('button')
      .find((button) => button.text() === 'Reset to inherited')!
      .trigger('click')
    await settle()

    expect(updates.at(-1)).toEqual({})
    view.host.unmount()
  })

  it('treats a value as own, not inherited, when the saved own content has it', async () => {
    // The effective document contains the own content too; a cleared own value has nothing to fall back to.
    const { view } = mountForm({ modelValue: {}, effective: { retries: 4, title: 'Own' }, own: { retries: 4, title: 'Own' } })
    await settle(6)

    expect(field(view, '/title').text()).not.toContain('inherited')
    expect(field(view, '/title').find('input').attributes('placeholder')).toBeUndefined()
    view.host.unmount()

    const saved = mountForm({ modelValue: { retries: 4 }, effective: { retries: 4 }, own: { retries: 4 } })
    await settle(6)

    expect(field(saved.view, '/retries').findAll('button').some((button) => button.text() === 'Reset to inherited')).toBe(
      false,
    )
    saved.view.host.unmount()
  })

  it('renders binding values as expressions and skips their schema check', async () => {
    const { view } = mountForm({ modelValue: { retries: '@rst.invoicing/retries' } })
    await settle(6)

    const retries = field(view, '/retries')
    expect(retries.text()).toContain('Evaluated by the server')
    expect(retries.find('input').element).toHaveProperty('value', '@rst.invoicing/retries')
    expect(retries.find('[role="alert"]').exists()).toBe(false)
    view.host.unmount()
  })

  it('attaches client and server errors to the controls', async () => {
    const { view } = mountForm({
      modelValue: { retries: 20 },
      serverErrors: [{ pointer: '/mode', message: 'Not in enumeration', keyword: 'server' }],
    })
    await settle(6)

    expect(field(view, '/retries').find('[role="alert"]').text()).toContain('must be <= 10')
    expect(field(view, '/mode').find('[role="alert"]').text()).toContain('Not in enumeration')
    view.host.unmount()
  })
})

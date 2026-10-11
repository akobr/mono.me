import { describe, expect, it } from 'vitest'
import { ref } from 'vue'
import DocumentEditor from '@/components/editor/DocumentEditor.vue'
import ValuesEditor from '@/components/annotations/ValuesEditor.vue'
import { mountInApp, settle } from '../../helpers'

describe('DocumentEditor', () => {
  it('serializes the value in the chosen format and emits parsed values', async () => {
    const view = mountInApp(DocumentEditor, { modelValue: { b: 1, a: [true] }, format: 'yaml' })
    await settle()
    const textarea = view.host.find('textarea')

    expect(textarea.element.value).toBe('b: 1\na:\n  - true\n')

    await textarea.setValue('b: 2\na: []\n')
    expect(view.inner().emitted('update:modelValue')?.at(-1)).toEqual([{ b: 2, a: [] }])
    view.host.unmount()
  })

  it('keeps the last valid value and blocks the format switch on a syntax error', async () => {
    const view = mountInApp(DocumentEditor, { modelValue: { a: 1 }, format: 'json' })
    await settle()

    await view.host.find('textarea').setValue('{ "a": ')
    await settle()

    expect(view.inner().emitted('update:valid')?.at(-1)).toEqual([false])
    expect(view.host.text()).toContain('Line 1: Value expected')

    await view.setProps({ format: 'yaml' })

    expect(view.inner().emitted('update:format')?.at(-1)).toEqual(['json'])
    expect(view.host.text()).toContain('Fix the syntax error at line 1 first.')
    view.host.unmount()
  })

  it('switches the format from the last valid value', async () => {
    const view = mountInApp(DocumentEditor, { modelValue: { a: 1 }, format: 'json' })
    await settle()

    await view.setProps({ format: 'yaml' })

    expect(view.host.find('textarea').element.value).toBe('a: 1\n')
    view.host.unmount()
  })

  it('shows semantic issues from validate', async () => {
    const view = mountInApp(DocumentEditor, {
      modelValue: { Name: 'x' },
      format: 'json',
      validate: () => [{ path: 'Name', message: 'cannot change.' }],
    })
    await settle()

    expect(view.host.text()).toContain('Name cannot change.')
    expect(view.inner().emitted('update:valid')?.at(-1)).toEqual([false])
    view.host.unmount()
  })
})

describe('ValuesEditor', () => {
  function mountValues(initial: Record<string, unknown>) {
    const model = ref(initial)
    const view = mountInApp(ValuesEditor, {
      modelValue: model.value,
      'onUpdate:modelValue': (value: Record<string, unknown>) => {
        model.value = value
        void view.setProps({ modelValue: value })
      },
    })
    return { view, model }
  }

  it('edits primitive values and keeps the key order', async () => {
    const { view, model } = mountValues({ tier: 'gold', retries: 3 })
    await settle()

    await view.host.findAll('input[aria-label="Value"]')[0]!.setValue('silver')
    await settle()

    expect(model.value).toEqual({ tier: 'silver', retries: 3 })
    view.host.unmount()
  })

  it('shows numbers with all their fraction digits', async () => {
    const { view } = mountValues({ threshold: 0.12345, big: 1234567 })
    await settle()

    const values = view.host.findAll('input[aria-label="Value"]').map((input) => (input.element as HTMLInputElement).value)
    expect(values).toEqual(['0.12345', '1234567'])
    view.host.unmount()
  })

  it('adds rows with unique keys and reports duplicates', async () => {
    const { view, model } = mountValues({ key1: 'a' })
    await settle()

    await view.host
      .findAll('button')
      .find((button) => button.text().includes('Add value'))!
      .trigger('click')
    await settle()

    expect(Object.keys(model.value)).toEqual(['key1', 'key2'])

    await view.host.findAll('input[aria-label="Value key"]')[1]!.setValue('key1')
    await settle()

    expect(view.host.text()).toContain('Duplicate keys: key1')
    view.host.unmount()
  })
})

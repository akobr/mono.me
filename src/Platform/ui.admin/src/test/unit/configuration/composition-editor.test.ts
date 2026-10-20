import { afterEach, describe, expect, it, vi } from 'vitest'
import { Compartment, EditorState } from '@codemirror/state'
import { EditorView } from '@codemirror/view'
import { json } from '@codemirror/lang-json'
import {
  compositionDecorations,
  compositionMarker,
  selectedStep,
  setSelectedStep,
  stepLines,
  type CompositionGutterOptions,
  type CompositionMarkerStep,
} from '@/components/editor/setup/composition'
import { compositionLines } from '@/utils/composition-lines'

// The blame gutter of the Composition view (424-36) in a real EditorView: happy-dom renders the lines and the
// gutters, though without layout, so a click on a marker is checked in Playwright.
const content = { retries: 3, plan: 'enterprise', tiers: [{ name: 'gold' }] }
const lines = compositionLines(content, [
  { Path: '/retries', Step: 6 },
  { Path: '/plan', Step: 3 },
  { Path: '/tiers', Step: 3 },
  { Path: '/tiers/0', Step: 3 },
  { Path: '/tiers/0/name', Step: 3 },
])

const marker = (order: number, code: string, template = false): [number, CompositionMarkerStep] => [
  order,
  { order, code, template, title: `Step ${order}` },
]
const steps = new Map([marker(3, 'usg'), marker(6, 'exe', true)])

let view: EditorView | undefined
afterEach(() => {
  view?.destroy()
  view = undefined
})

function mount(options: Partial<CompositionGutterOptions> = {}, slot = new Compartment()) {
  const parent = document.createElement('div')
  document.body.append(parent)
  view = new EditorView({
    parent,
    state: EditorState.create({
      doc: lines.map((line) => line.text).join('\n'),
      extensions: [
        json(),
        slot.of(compositionDecorations({ lines: lines.map((line) => line.step), steps, ...options })),
      ],
    }),
  })
  return view
}

/** The markers of the gutter, without its spacer (the first element, which only keeps the width). */
const markers = (target: EditorView) =>
  [...target.dom.querySelectorAll<HTMLElement>('.cm-composition-gutter .cm-gutterElement')]
    .slice(1)
    .map((element) => element.querySelector<HTMLElement>('.cm-composition-marker')!)

const selectedTexts = (target: EditorView) =>
  [...target.dom.querySelectorAll('.cm-line.cm-composition-selected')].map((line) => line.textContent?.trim())

describe('the composition gutter', () => {
  it('shows the step of every line, none on the root braces, and a T on a template step', () => {
    const target = mount()

    // a line without a step gets no gutter element
    expect(markers(target).map((element) => element.textContent)).toEqual(['6T', '3', '3', '3', '3', '3', '3'])
    expect(
      Array.from({ length: lines.length }, (_, index) => compositionMarker(target.state, index + 1)?.step.order),
    ).toEqual([undefined, 6, 3, 3, 3, 3, 3, 3, undefined])
  })

  it('tints the marker with the type color of its source and keeps the hover text', () => {
    const [retries, plan] = markers(mount())

    expect(retries?.style.getPropertyValue('--cm-composition-type')).toBe('var(--st-exe)')
    expect(plan?.style.getPropertyValue('--cm-composition-type')).toBe('var(--st-usg)')
    expect(retries?.title).toBe('Step 6')
    // the gutters are hidden from assistive technology; the view has a status line instead
    expect(view!.dom.querySelector('.cm-gutters')?.getAttribute('aria-hidden')).toBe('true')
  })

  it('highlights the lines of the step the selection effect selects', () => {
    const target = mount()
    expect(selectedTexts(target)).toEqual([])

    target.dispatch({ effects: setSelectedStep.of(3) })
    expect(target.state.field(selectedStep)).toBe(3)
    expect(stepLines(target.state, 3)).toEqual([3, 4, 5, 6, 7, 8])
    expect(selectedTexts(target)).toEqual(['"plan": "enterprise",', '"tiers": [', '{', '"name": "gold"', '}', ']'])
    expect(target.dom.querySelectorAll('.cm-composition-marker-selected')).toHaveLength(6)

    target.dispatch({ effects: setSelectedStep.of(6) })
    expect(selectedTexts(target)).toEqual(['"retries": 3,'])

    target.dispatch({ effects: setSelectedStep.of(null) })
    expect(selectedTexts(target)).toEqual([])
    expect(target.dom.querySelectorAll('.cm-composition-marker-selected')).toHaveLength(0)
  })

  it('keeps the selected step when a newer composition reconfigures it', () => {
    const slot = new Compartment()
    const target = mount({}, slot)
    target.dispatch({ effects: setSelectedStep.of(6) })

    // the same text, now with the retries from step 3
    const next = lines.map((line) => (line.pointer === '/retries' ? 3 : line.step))
    target.dispatch({ effects: slot.reconfigure(compositionDecorations({ lines: next, steps })) })

    expect(target.state.field(selectedStep)).toBe(6)
    expect(selectedTexts(target)).toEqual([])
    expect(markers(target)[1]?.textContent).toBe('3')
  })

  it('reports the line of the cursor and lets the read-only content take the focus', () => {
    const onCursor = vi.fn()
    const target = mount({ onCursor })

    target.dispatch({ selection: { anchor: target.state.doc.line(3).from + 2 } })
    expect(onCursor).toHaveBeenLastCalledWith(3)
    // a change of the effects alone moves no cursor
    target.dispatch({ effects: setSelectedStep.of(3) })
    expect(onCursor).toHaveBeenCalledTimes(1)
    expect(target.contentDOM.getAttribute('tabindex')).toBe('0')
  })
})

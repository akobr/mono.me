import { Facet, RangeSetBuilder, StateEffect, StateField, type EditorState, type Extension } from '@codemirror/state'
import { Decoration, EditorView, GutterMarker, gutter, type DecorationSet, type ViewUpdate } from '@codemirror/view'

// The blame gutter of the Composition view (spec 8.4 of the Composition of the Effective Configuration), in the
// pattern of bindings.ts: every line shows the order of the step that set it, the type of the step's source tints the
// marker, a template step adds a "T", and the lines of the selected step are highlighted. The view sets the selected
// step with `setSelectedStep` and hears a click on a marker through `onSelect`.

/** What the gutter shows of a step. */
export interface CompositionMarkerStep {
  order: number
  /** The type code of the step's source, for the tint (var(--st-{code})). */
  code?: string
  /** A template step: the marker carries a "T" after the number, a cue that is not a color. */
  template: boolean
  /** The hover text: the step, the source and its version, the node that applied it and the node it was merged into. */
  title: string
}

export interface CompositionGutterOptions {
  /** The step of every line, line 1 first; undefined for the lines of the root braces. */
  lines: readonly (number | undefined)[]
  steps: ReadonlyMap<number, CompositionMarkerStep>
  /** A click on the marker of a line (from 1). */
  onSelect?: (step: number, line: number) => void
  /** The line of the cursor (from 1), after the user moved it. */
  onCursor?: (line: number) => void
}

const compositionData = Facet.define<CompositionGutterOptions, CompositionGutterOptions | undefined>({
  combine: (values) => values.at(-1),
})

/** Selects the step whose lines are highlighted; null clears the selection. */
export const setSelectedStep = StateEffect.define<number | null>()

/** The selected step; it stays when the extension is reconfigured with a newer composition. */
export const selectedStep = StateField.define<number | null>({
  create: () => null,
  update: (value, transaction) => {
    for (const effect of transaction.effects) {
      if (effect.is(setSelectedStep)) {
        value = effect.value
      }
    }
    return value
  },
})

export class StepMarker extends GutterMarker {
  constructor(
    readonly step: CompositionMarkerStep,
    readonly selected: boolean,
  ) {
    super()
  }

  override eq(other: GutterMarker): boolean {
    return (
      other instanceof StepMarker &&
      other.selected === this.selected &&
      other.step.order === this.step.order &&
      other.step.code === this.step.code &&
      other.step.template === this.step.template &&
      other.step.title === this.step.title
    )
  }

  override toDOM(): HTMLElement {
    const element = document.createElement('span')
    element.className = this.selected ? 'cm-composition-marker cm-composition-marker-selected' : 'cm-composition-marker'
    element.textContent = `${this.step.order}${this.step.template ? 'T' : ''}`
    element.title = this.step.title
    element.dataset.step = String(this.step.order)
    if (this.step.code) {
      element.style.setProperty('--cm-composition-type', `var(--st-${this.step.code})`)
    }
    return element
  }
}

/** The marker of a line (from 1), or null for a line without a step. */
export function compositionMarker(state: EditorState, line: number): StepMarker | null {
  const data = state.facet(compositionData)
  const order = data?.lines[line - 1]
  const step = order === undefined ? undefined : data?.steps.get(order)
  return step ? new StepMarker(step, state.field(selectedStep, false) === order) : null
}

/** The lines (from 1) the step set. */
export function stepLines(state: EditorState, order: number): number[] {
  const lines = state.facet(compositionData)?.lines ?? []
  const result: number[] = []
  for (let line = 1; line <= Math.min(lines.length, state.doc.lines); line++) {
    if (lines[line - 1] === order) {
      result.push(line)
    }
  }
  return result
}

const selectedLine = Decoration.line({ class: 'cm-composition-selected' })

/** The line decorations of the selected step. */
export function selectedLineDecorations(state: EditorState): DecorationSet {
  const builder = new RangeSetBuilder<Decoration>()
  const order = state.field(selectedStep, false)
  if (order !== null && order !== undefined) {
    for (const line of stepLines(state, order)) {
      const from = state.doc.line(line).from
      builder.add(from, from, selectedLine)
    }
  }
  return builder.finish()
}

const changed = (update: ViewUpdate) =>
  update.docChanged ||
  update.startState.facet(compositionData) !== update.state.facet(compositionData) ||
  update.startState.field(selectedStep, false) !== update.state.field(selectedStep, false)

/** The widest marker, so the gutter keeps its width while scrolling. */
function spacer(state: EditorState): GutterMarker {
  const steps = [...(state.facet(compositionData)?.steps.values() ?? [])]
  const order = Math.max(0, ...steps.map((step) => step.order))
  return new StepMarker({ order, template: steps.some((step) => step.template), title: '' }, false)
}

const compositionGutter = gutter({
  class: 'cm-composition-gutter',
  lineMarker: (view, line) => compositionMarker(view.state, view.state.doc.lineAt(line.from).number),
  lineMarkerChange: changed,
  initialSpacer: (view) => spacer(view.state),
  updateSpacer: (current, update) => (changed(update) ? spacer(update.state) : current),
  domEventHandlers: {
    // A click selects the step; the default (focus and selection) is prevented.
    mousedown: (view, block, event) => {
      const line = view.state.doc.lineAt(block.from).number
      const marker = compositionMarker(view.state, line)
      if (!marker || (event as MouseEvent).button !== 0) {
        return false
      }
      view.state.facet(compositionData)?.onSelect?.(marker.step.order, line)
      return true
    },
  },
})

const cursorListener = EditorView.updateListener.of((update) => {
  if (update.selectionSet) {
    const { state } = update
    state.facet(compositionData)?.onCursor?.(state.doc.lineAt(state.selection.main.head).number)
  }
})

const compositionTheme = EditorView.baseTheme({
  '.cm-composition-gutter .cm-gutterElement': { padding: '0 2px' },
  '.cm-composition-marker': {
    display: 'inline-block',
    minWidth: '2.25em',
    padding: '0 4px',
    borderRadius: '2px',
    textAlign: 'end',
    color: 'var(--ui-text-highlighted)',
    fontVariantNumeric: 'tabular-nums',
    cursor: 'pointer',
    // The type tints the background and the side bar only; the number keeps the full-contrast text color.
    backgroundColor: 'color-mix(in oklab, var(--cm-composition-type, var(--ui-border)) 20%, transparent)',
    boxShadow: 'inset 3px 0 0 color-mix(in oklab, var(--cm-composition-type, var(--ui-border)) 75%, transparent)',
  },
  '.cm-composition-marker-selected': { fontWeight: '700', outline: '1px solid var(--ui-primary)' },
  '.cm-composition-selected': { backgroundColor: 'color-mix(in oklab, var(--ui-primary) 12%, transparent)' },
})

/**
 * The blame gutter, its hover text, the highlight of the selected step and the cursor report. The read-only content
 * takes the keyboard focus too, so the arrow keys move the cursor whose line the view names.
 */
export function compositionDecorations(options: CompositionGutterOptions): Extension {
  return [
    compositionData.of(options),
    selectedStep,
    compositionGutter,
    EditorView.decorations.compute([selectedStep, compositionData, 'doc'], selectedLineDecorations),
    cursorListener,
    compositionTheme,
    EditorView.contentAttributes.of({ tabindex: '0' }),
  ]
}

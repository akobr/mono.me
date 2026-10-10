import { RangeSetBuilder, type Extension } from '@codemirror/state'
import {
  Decoration,
  EditorView,
  GutterMarker,
  gutter,
  ViewPlugin,
  type DecorationSet,
  type ViewUpdate,
} from '@codemirror/view'

// Highlights binding expressions (spec section 9.3): strings starting with "@" (JSON "…" or YAML plain scalars)
// and the $jsonlogic, $jsone and $definition envelope keys get a subtle background and an "fx" gutter marker.

const pattern = /"@(?:[^"\\\n]|\\.)*"|(?<=:\s|-\s)@[^\s#,}\]]+|"?\$(?:jsonlogic|jsone|definition)"?(?=\s*:)/g

const mark = Decoration.mark({ class: 'cm-binding' })

class FxMarker extends GutterMarker {
  override toDOM() {
    const element = document.createElement('span')
    element.textContent = 'fx'
    element.className = 'cm-binding-gutter'
    element.title = 'Binding expression, evaluated by the server'
    return element
  }
}

const fx = new FxMarker()

function findBindings(view: EditorView): { decorations: DecorationSet; lines: Set<number> } {
  const builder = new RangeSetBuilder<Decoration>()
  const lines = new Set<number>()

  for (const { from, to } of view.visibleRanges) {
    const text = view.state.doc.sliceString(from, to)
    for (const match of text.matchAll(pattern)) {
      const start = from + match.index
      builder.add(start, start + match[0].length, mark)
      lines.add(view.state.doc.lineAt(start).from)
    }
  }

  return { decorations: builder.finish(), lines }
}

const bindingPlugin = ViewPlugin.fromClass(
  class {
    decorations: DecorationSet
    lines: Set<number>

    constructor(view: EditorView) {
      ;({ decorations: this.decorations, lines: this.lines } = findBindings(view))
    }

    update(update: ViewUpdate) {
      if (update.docChanged || update.viewportChanged) {
        ;({ decorations: this.decorations, lines: this.lines } = findBindings(update.view))
      }
    }
  },
  { decorations: (plugin) => plugin.decorations },
)

const bindingGutter = gutter({
  class: 'cm-binding-gutters',
  lineMarker: (view, line) => (view.plugin(bindingPlugin)?.lines.has(line.from) ? fx : null),
  lineMarkerChange: (update) => update.docChanged || update.viewportChanged,
})

const bindingTheme = EditorView.baseTheme({
  '.cm-binding': {
    backgroundColor: 'color-mix(in oklab, var(--ui-info) 14%, transparent)',
    borderRadius: '3px',
  },
  '.cm-binding-gutter': {
    color: 'var(--ui-info)',
    fontSize: '10px',
    fontStyle: 'italic',
    fontWeight: '600',
    padding: '0 3px',
  },
})

export function bindingDecorations(): Extension {
  return [bindingPlugin, bindingGutter, bindingTheme]
}

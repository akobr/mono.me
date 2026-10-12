import MarkdownIt from 'markdown-it'

const allowedSchemes = new Set(['http', 'https', 'mailto'])

/** Browsers skip leading controls and spaces, and tabs and newlines anywhere, before they read a scheme. */
export function isAllowedUrl(url: string): boolean {
  const normalized = url.replaceAll(/[\t\n\r]/g, '').replace(/^[\p{Cc}\s]+/u, '')
  const scheme = /^([a-z][a-z\d+.-]*):/i.exec(normalized)?.[1]?.toLowerCase()
  return scheme === undefined || allowedSchemes.has(scheme)
}

const markdown = new MarkdownIt('default', {
  html: false,
  linkify: true,
  breaks: true,
  typographer: false,
})

// Only URLs with a scheme are linked automatically: "example.com" stays text.
markdown.linkify.set({ fuzzyLink: false })
markdown.validateLink = isAllowedUrl

const rules = markdown.renderer.rules
const renderImage = rules.image!

rules.link_open = (tokens, index, options, _env, renderer) => {
  tokens[index]!.attrSet('target', '_blank')
  tokens[index]!.attrSet('rel', 'noopener noreferrer')
  return renderer.renderToken(tokens, index, options)
}

rules.image = (tokens, index, options, env, renderer) => {
  tokens[index]!.attrSet('loading', 'lazy')
  tokens[index]!.attrSet('referrerpolicy', 'no-referrer')
  return renderImage(tokens, index, options, env, renderer)
}

// A wide table scrolls in its own box instead of widening the panel.
rules.table_open = (tokens, index, options, _env, renderer) =>
  `<div class="markdown-table">${renderer.renderToken(tokens, index, options)}`
rules.table_close = (tokens, index, options, _env, renderer) =>
  `${renderer.renderToken(tokens, index, options)}</div>\n`

/**
 * Markdown of descriptions and schema help: CommonMark with GFM tables and strikethrough, line breaks on a single
 * newline and bare http(s) URLs as links. Raw HTML is escaped and only http(s), mailto and relative URLs become links
 * or images, so the output is safe for v-html.
 */
export function renderMarkdown(source: string | undefined | null): string {
  if (!source) {
    return ''
  }

  return markdown.render(source)
}

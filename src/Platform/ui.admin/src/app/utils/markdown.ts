const escapeHtml = (text: string) =>
  text
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;')

/**
 * A deliberately small Markdown subset for descriptions and schema help: paragraphs, `code`, **bold**, *emphasis*
 * and [links](https://…). Everything is HTML-escaped first and only http(s) links are produced, so the output is
 * safe for v-html.
 */
export function renderInlineMarkdown(source: string | undefined | null): string {
  if (!source) {
    return ''
  }

  return escapeHtml(source)
    .split(/\n{2,}/)
    .map((paragraph) =>
      paragraph
        .replaceAll(/`([^`]+)`/g, '<code>$1</code>')
        .replaceAll(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>')
        .replaceAll(/(^|[^*])\*([^*]+)\*/g, '$1<em>$2</em>')
        .replaceAll(
          /\[([^\]]+)\]\((https?:\/\/[^\s)]+)\)/g,
          '<a href="$2" target="_blank" rel="noopener noreferrer" class="text-primary underline">$1</a>',
        )
        .replaceAll('\n', '<br>'),
    )
    .map((paragraph) => `<p>${paragraph}</p>`)
    .join('')
}

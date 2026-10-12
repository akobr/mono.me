import { describe, expect, it } from 'vitest'
import { isAllowedUrl, renderMarkdown } from '@/utils/markdown'

const link = (href: string, text: string) => `<a href="${href}" target="_blank" rel="noopener noreferrer">${text}</a>`

describe('renderMarkdown', () => {
  it('returns nothing for an empty source', () => {
    expect(renderMarkdown('')).toBe('')
    expect(renderMarkdown(null)).toBe('')
    expect(renderMarkdown(undefined)).toBe('')
  })

  it('keeps the inline subset of the previous renderer', () => {
    expect(renderMarkdown('**bold** *em* `code` [docs](https://d.test)\nnext line')).toBe(
      `<p><strong>bold</strong> <em>em</em> <code>code</code> ${link('https://d.test', 'docs')}<br>\nnext line</p>\n`,
    )
  })

  it('renders headings', () => {
    expect(renderMarkdown('# One\n## Two\n###### Six\n\nSetext\n---')).toBe(
      '<h1>One</h1>\n<h2>Two</h2>\n<h6>Six</h6>\n<h2>Setext</h2>\n',
    )
  })

  it('renders nested and numbered lists', () => {
    expect(renderMarkdown('- a\n  - nested\n- b\n\n1. one\n2. two')).toBe(
      '<ul>\n<li>a\n<ul>\n<li>nested</li>\n</ul>\n</li>\n<li>b</li>\n</ul>\n<ol>\n<li>one</li>\n<li>two</li>\n</ol>\n',
    )
  })

  it('renders nested block quotes', () => {
    expect(renderMarkdown('> quote\n> > nested')).toBe(
      '<blockquote>\n<p>quote</p>\n<blockquote>\n<p>nested</p>\n</blockquote>\n</blockquote>\n',
    )
  })

  it('renders fenced and indented code blocks with escaped content', () => {
    expect(renderMarkdown('```ts\nconst a = 1 < 2\n```\n\n    indented')).toBe(
      '<pre><code class="language-ts">const a = 1 &lt; 2\n</code></pre>\n<pre><code>indented\n</code></pre>\n',
    )
  })

  it('renders aligned tables in a scrolling box', () => {
    const html = renderMarkdown('| Left | Right |\n| :--- | ---: |\n| a | b |')

    expect(html).toMatch(/^<div class="markdown-table"><table>\n<thead>/)
    expect(html).toContain('<th style="text-align:right">Right</th>')
    expect(html).toContain('<td style="text-align:left">a</td>')
    expect(html).toMatch(/<\/table>\n<\/div>\n$/)
  })

  it('renders strikethrough and horizontal rules', () => {
    expect(renderMarkdown('~~gone~~\n\n***')).toBe('<p><s>gone</s></p>\n<hr>\n')
  })

  it('escapes raw HTML', () => {
    expect(renderMarkdown('<script>x</script> <img src=x onerror=alert(1)> <b>bold</b>')).toBe(
      '<p>&lt;script&gt;x&lt;/script&gt; &lt;img src=x onerror=alert(1)&gt; &lt;b&gt;bold&lt;/b&gt;</p>\n',
    )
  })

  it('leaves links and images with other schemes as typed', () => {
    const source =
      '[a](javascript:alert(1)) [b](JaVaScRiPt:alert(1)) [c](vbscript:x) [d](data:text/html;base64,xx) ' +
      '[e](file:///etc/passwd) ![i](javascript:alert(1)) ![j](data:image/png;base64,xx)'

    expect(renderMarkdown(source)).toBe(`<p>${source}</p>\n`)
  })

  it('links relative and mailto URLs and opens links in a new tab', () => {
    expect(renderMarkdown('[rel](/acme/billing) [frag](#top) [mail](mailto:a@b.test)')).toBe(
      `<p>${link('/acme/billing', 'rel')} ${link('#top', 'frag')} ${link('mailto:a@b.test', 'mail')}</p>\n`,
    )
  })

  it('loads images lazily without a referrer', () => {
    expect(renderMarkdown('![chart](https://i.test/a.png "Chart")')).toBe(
      '<p><img src="https://i.test/a.png" alt="chart" title="Chart" loading="lazy" referrerpolicy="no-referrer"></p>\n',
    )
  })

  it('links bare URLs with a scheme and e-mail addresses only', () => {
    expect(renderMarkdown('see https://d.test/x and example.com and a@b.test')).toBe(
      `<p>see ${link('https://d.test/x', 'https://d.test/x')} and example.com and ${link('mailto:a@b.test', 'a@b.test')}</p>\n`,
    )
  })
})

describe('isAllowedUrl', () => {
  it('allows http(s), mailto and relative URLs', () => {
    expect(isAllowedUrl('https://d.test')).toBe(true)
    expect(isAllowedUrl('HTTP://d.test')).toBe(true)
    expect(isAllowedUrl('mailto:a@b.test')).toBe(true)
    expect(isAllowedUrl('/acme/billing')).toBe(true)
    expect(isAllowedUrl('#top')).toBe(true)
  })

  it('reads the scheme as a browser does', () => {
    expect(isAllowedUrl(' \u0001javascript:alert(1)')).toBe(false)
    expect(isAllowedUrl('java\tscript:alert(1)')).toBe(false)
    expect(isAllowedUrl('ftp://f.test')).toBe(false)
    expect(isAllowedUrl('c:/windows')).toBe(false)
  })
})

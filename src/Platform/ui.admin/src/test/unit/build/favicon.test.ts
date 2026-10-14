// @vitest-environment node
import { readFile } from 'node:fs/promises'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { beforeAll, describe, expect, it } from 'vitest'

// The favicon is the 42 logo drawn at 16 and 32 px, the same files as the docs site (424-32). Vite copies public/ as
// it is and keeps the links of index.html, so each link must point at a file of the size it declares.
const root = fileURLToPath(new URL('../../..', import.meta.url))

const PNG_SIGNATURE = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a])

/** The attributes of every <link> of the HTML. */
const links = (html: string) =>
  [...html.matchAll(/<link\b[^>]*>/g)].map(([tag]) =>
    Object.fromEntries([...tag.matchAll(/([\w-]+)="([^"]*)"/g)].map(([, name, value]) => [name!, value!])),
  )

/** The size in the IHDR chunk, which follows the signature of every PNG. */
function pngSize(file: Buffer): string {
  expect(file.subarray(0, 8).equals(PNG_SIGNATURE)).toBe(true)
  return `${file.readUInt32BE(16)}x${file.readUInt32BE(20)}`
}

describe('favicon', () => {
  let html: string

  beforeAll(async () => {
    html = await readFile(path.join(root, 'index.html'), 'utf8')
  })

  it('links the 42 logo at 32 and 16 px', async () => {
    const icons = links(html).filter((link) => link.rel === 'icon')

    expect(icons.map((icon) => icon.sizes)).toEqual(['32x32', '16x16'])
    for (const icon of icons) {
      expect(icon.type).toBe('image/png')
      expect(icon.href).toBe(`/favicon-${icon.sizes}.png`)
      expect(pngSize(await readFile(path.join(root, 'public', icon.href!)))).toBe(icon.sizes)
    }
  })

  it('no longer links the "2S" placeholder', () => {
    expect(html).not.toContain('favicon.svg')
  })
})

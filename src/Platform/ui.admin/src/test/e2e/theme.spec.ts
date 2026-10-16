import { expect, test, type Page } from '@playwright/test'
import { isMobile, openApp } from './support'

// The accent is the purple of the 42 logo (424-39): purple-700 in light and purple-400 in dark, as sRGB renders them.
const accents = { light: [0x82, 0x00, 0xdb], dark: [0xc2, 0x7a, 0xff] } as const

/** Paints a CSS color on a 2D canvas and expects its 8-bit sRGB channels within 2 of `expected`. */
async function expectColor(page: Page, color: string, expected: readonly number[]): Promise<void> {
  const channels = await page.evaluate((value) => {
    const context = document.createElement('canvas').getContext('2d')!
    context.fillStyle = value
    context.fillRect(0, 0, 1, 1)
    return Array.from(context.getImageData(0, 0, 1, 1).data.subarray(0, 3))
  }, color)
  const off = channels.map((channel, index) => Math.abs(channel - expected[index]!))
  expect(Math.max(...off), `${color} renders as rgb(${channels.join(', ')})`).toBeLessThanOrEqual(2)
}

for (const colorScheme of ['light', 'dark'] as const) {
  test.describe(colorScheme, () => {
    test.use({ colorScheme })

    test('the accent is the purple of the 42 logo', async ({ page }, testInfo) => {
      await openApp(page, '/acme/billing/default/annotations')

      const accent = await page.evaluate(() =>
        getComputedStyle(document.documentElement).getPropertyValue('--ui-primary'),
      )
      await expectColor(page, accent, accents[colorScheme])

      // A solid button of Nuxt UI is painted with the accent.
      const create = page.getByRole('button', { name: isMobile(testInfo) ? 'New annotation' : 'New', exact: true })
      await expect(create).toBeVisible()
      await expectColor(
        page,
        await create.evaluate((button) => getComputedStyle(button).backgroundColor),
        accents[colorScheme],
      )
    })
  })
}

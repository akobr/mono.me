import { expect, type Locator, type Page, type TestInfo } from '@playwright/test'
import { AxeBuilder } from '@axe-core/playwright'

export type MockScenario = 'default' | 'new-user' | 'invited' | 'large'

declare global {
  interface Window {
    /** The hooks of the mock API (app/mock/browser.ts) that the tests call from the page. */
    storytellerMock?: {
      changeConfiguration: (key: string, changes: Record<string, unknown>) => void
      addMachine: (projectKey: string, machine: Record<string, unknown>) => void
    }
  }
}

/**
 * Opens a path of the app with a fresh mock data set. Every test has its own browser context, so the
 * sessionStorage-backed mock API starts from the scenario's seed. The scenario goes before the hash of a path with an
 * anchor (`/settings#secrets`).
 */
export async function openApp(page: Page, path: string, scenario: MockScenario = 'default'): Promise<void> {
  const [location, hash] = path.split('#')
  await page.goto(`${location}${location!.includes('?') ? '&' : '?'}mock-scenario=${scenario}${hash ? `#${hash}` : ''}`)
  await page.waitForLoadState('networkidle')
}

/** Navigates within a test without resetting the mock data (no scenario parameter). */
export async function goto(page: Page, path: string): Promise<void> {
  await page.goto(path)
  await page.waitForLoadState('networkidle')
}

/** Starts the tab signed out (the mock sign-in keeps its state in sessionStorage). */
export async function signedOut(page: Page): Promise<void> {
  await page.addInitScript(() => {
    if (!sessionStorage.getItem('2s-admin:e2e-started')) {
      sessionStorage.setItem('2s-admin:e2e-started', '1')
      sessionStorage.setItem('2s-admin:mock-session', 'signed-out')
    }
  })
}

export const isMobile = (testInfo: TestInfo) => testInfo.project.name === 'mobile'

/** Opens an annotation from the grid (desktop) or the list (mobile). */
export async function openFromList(page: Page, key: string, testInfo: TestInfo): Promise<void> {
  const item = isMobile(testInfo)
    ? page.getByRole('listitem').filter({ hasText: key }).getByRole('button')
    : page.getByRole('row').filter({ hasText: key })
  await item.first().click()
}

/** Opens Bulk create from the navbar (desktop) or the command palette (mobile, where the navbar has no room for it). */
export async function openBulkCreate(page: Page, testInfo: TestInfo): Promise<Locator> {
  if (isMobile(testInfo)) {
    await page.getByRole('button', { name: 'Search…' }).click()
    await page.getByPlaceholder('Search annotations, pages, projects and actions…').fill('Bulk create')
    await page.getByRole('option', { name: 'Bulk create annotations' }).click()
  } else {
    await page.getByRole('button', { name: 'Bulk create', exact: true }).click()
  }
  const dialog = page.getByRole('dialog', { name: 'Bulk create annotations' })
  await expect(dialog).toBeVisible()
  return dialog
}

/** Replaces the text of the visible CodeMirror editor. */
export async function replaceEditorText(page: Page, text: string): Promise<void> {
  const editor = page.locator('.cm-content').first()
  await editor.click()
  await page.keyboard.press('ControlOrMeta+A')
  await page.keyboard.insertText(text)
}

/** Serious and critical WCAG 2.1 A/AA violations of the current page. */
export async function expectAccessible(page: Page, exclude: string[] = []): Promise<void> {
  let builder = new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'])
  for (const selector of exclude) {
    builder = builder.exclude(selector)
  }
  const { violations } = await builder.analyze()
  const serious = violations
    .filter((violation) => violation.impact === 'serious' || violation.impact === 'critical')
    .map((violation) => ({
      id: violation.id,
      help: violation.help,
      targets: violation.nodes.slice(0, 5).map((node) => node.target.join(' ')),
    }))
  expect(serious, JSON.stringify(serious, null, 2)).toEqual([])
}

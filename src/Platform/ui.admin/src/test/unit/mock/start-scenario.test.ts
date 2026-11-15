import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  applyStartScenario,
  createSeed,
  currentScenario,
  launchScenario,
  loadDb,
  resetDb,
  saveDb,
  storageKey,
} from '@/mock/db'

// The dataset of a page load in the mock mode (spec 424-77, section 2): ?mock-scenario= first, then the dataset the
// build was started with when it changed, else the session's own dataset with its edits.

const open = (search = '') => history.replaceState(null, '', `/${search}`)
const stored = () => sessionStorage.getItem(storageKey)

afterEach(() => {
  open()
})

describe('the dataset of a page load', () => {
  it('starts a new tab with the dataset of the build', () => {
    expect(applyStartScenario('showcase')).toBe(true)
    expect(currentScenario()).toBe('showcase')
  })

  it('keeps the dataset and its edits on a reload', () => {
    applyStartScenario('showcase')
    const db = resetDb()
    db.invitations = []
    saveDb(db)

    expect(applyStartScenario('showcase')).toBe(false)
    expect(currentScenario()).toBe('showcase')
    expect(loadDb().invitations).toEqual([])
  })

  it('switches to the dataset of a restarted build, with fresh data', () => {
    applyStartScenario('showcase')
    resetDb()

    expect(applyStartScenario('default')).toBe(true)
    expect(currentScenario()).toBe('default')
    expect(stored()).toBeNull()
  })

  it('starts a tab with data under the previous key from the new seed', () => {
    // A tab opened before the exhibition (424-267): the same dataset and build, its edits under the key of that shape.
    applyStartScenario('default')
    const { exhibition: _exhibition, ...previous } = createSeed('default')
    sessionStorage.setItem('2s-admin:mock-db:2', JSON.stringify({ ...previous, invitations: [] }))

    expect(applyStartScenario('default')).toBe(true)
    expect(currentScenario()).toBe('default')
    expect(stored()).toBeNull()
    expect(loadDb().exhibition).toEqual({ Organization: 'exhibition', Project: 'exhibition' })
    expect(loadDb().invitations).toHaveLength(1)
  })

  it('prefers ?mock-scenario=, and keeps that choice on later loads', () => {
    applyStartScenario('showcase')
    resetDb()

    open('?mock-scenario=large')
    expect(applyStartScenario('showcase')).toBe(true)
    expect(currentScenario()).toBe('large')
    resetDb()

    open()
    expect(applyStartScenario('showcase')).toBe(false)
    expect(currentScenario()).toBe('large')
  })

  it('ignores an unknown ?mock-scenario=, and keeps the edits of the session', () => {
    open('?mock-scenario=nope')
    expect(applyStartScenario('showcase')).toBe(true)
    expect(currentScenario()).toBe('showcase')
    const db = resetDb()
    db.invitations = []
    saveDb(db)

    expect(applyStartScenario('showcase')).toBe(false)
    expect(currentScenario()).toBe('showcase')
    expect(loadDb().invitations).toEqual([])
  })

  it('reads the dataset of the build, the showcase without a valid one', () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    expect(launchScenario(undefined)).toBe('showcase')
    expect(launchScenario('')).toBe('showcase')
    expect(launchScenario('large')).toBe('large')
    expect(launchScenario('nope')).toBe('showcase')
    expect(warn).toHaveBeenCalledOnce()
  })

  it('leaves the unit tests on the default dataset', () => {
    expect(currentScenario()).toBe('default')
    expect(loadDb().scenario).toBe('default')
  })
})

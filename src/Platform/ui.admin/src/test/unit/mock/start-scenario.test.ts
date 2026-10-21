import { afterEach, describe, expect, it, vi } from 'vitest'
import { applyStartScenario, currentScenario, launchScenario, loadDb, resetDb, saveDb } from '@/mock/db'

// The dataset of a page load in the mock mode (spec 424-77, section 2): ?mock-scenario= first, then the dataset the
// build was started with when it changed, else the session's own dataset with its edits.

const open = (search = '') => history.replaceState(null, '', `/${search}`)
const stored = () => sessionStorage.getItem('2s-admin:mock-db')

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

  it('ignores an unknown ?mock-scenario=', () => {
    open('?mock-scenario=nope')
    applyStartScenario('default')
    expect(currentScenario()).toBe('default')
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

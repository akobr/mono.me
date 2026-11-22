import { describe, expect, it } from 'vitest'
import { mergeShownSelection, selectedKeysOf, shownSelection } from '@/utils/row-selection'

// The selection of Configurations is kept by key while a filter shows only some of the keys (spec "Copy and Compare
// Annotations and Configurations Between Views", section 8).
describe('shownSelection', () => {
  it('gives the table only the selected keys among the shown ones', () => {
    const selection = { 'sbt.contoso': true, 'sbt.northwind': true, 'usg.northwind.invoicing': true }

    expect(shownSelection(selection, ['sbt.northwind', 'sbt.initech'])).toEqual({ 'sbt.northwind': true })
    expect(shownSelection(selection, [])).toEqual({})
    expect(shownSelection({}, ['sbt.northwind'])).toEqual({})
  })

  it('leaves out a key that is false: the table counts every key of its selection for the header checkbox', () => {
    const shown = shownSelection({ 'sbt.contoso': false, 'sbt.northwind': true }, ['sbt.contoso', 'sbt.northwind'])

    expect(Object.keys(shown)).toEqual(['sbt.northwind'])
  })

  it('does not change the selection it reads', () => {
    const selection = { 'sbt.contoso': true, 'sbt.northwind': true }

    shownSelection(selection, ['sbt.contoso'])

    expect(selection).toEqual({ 'sbt.contoso': true, 'sbt.northwind': true })
  })
})

describe('mergeShownSelection', () => {
  const shown = ['sbt.contoso', 'sbt.northwind']

  it('gives the shown keys their new state and keeps the keys that the filter hides', () => {
    const selection = { 'sbt.contoso': true, 'usg.northwind.invoicing': true }

    // the table unchecked sbt.contoso and checked sbt.northwind
    expect(mergeShownSelection(selection, shown, { 'sbt.northwind': true })).toEqual({
      'usg.northwind.invoicing': true,
      'sbt.northwind': true,
    })
  })

  it('selects every shown row with the header checkbox, and clears only the shown rows with it', () => {
    const selection = { 'usg.northwind.invoicing': true }

    const all = mergeShownSelection(selection, shown, { 'sbt.contoso': true, 'sbt.northwind': true })
    expect(all).toEqual({ 'usg.northwind.invoicing': true, 'sbt.contoso': true, 'sbt.northwind': true })

    expect(mergeShownSelection(all, shown, {})).toEqual({ 'usg.northwind.invoicing': true })
  })

  it('drops the keys that are false, and takes no key that the table does not show', () => {
    const selection = { 'sbt.contoso': false, 'usg.northwind.invoicing': false }

    expect(mergeShownSelection(selection, shown, { 'sbt.northwind': false, 'sbt.initech': true })).toEqual({})
  })

  it('is the selection of the table again once the table shows it', () => {
    const selection = { 'sbt.contoso': true, 'usg.northwind.invoicing': true }
    const next = { 'sbt.northwind': true }

    const merged = mergeShownSelection(selection, shown, next)

    expect(shownSelection(merged, shown)).toEqual(next)
  })
})

describe('selectedKeysOf', () => {
  it('lists the selected keys in the order of the selection', () => {
    expect(selectedKeysOf({ 'usg.northwind.invoicing': true, 'sbt.contoso': false, 'sbt.northwind': true })).toEqual([
      'usg.northwind.invoicing',
      'sbt.northwind',
    ])
    expect(selectedKeysOf({})).toEqual([])
  })
})

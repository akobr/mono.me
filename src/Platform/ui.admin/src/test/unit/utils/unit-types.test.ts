import { describe, expect, it } from 'vitest'
import {
  cronFieldHint,
  genericUnitDefinitionHelp,
  isUnitAnnotation,
  unitTypeInfo,
  unitTypeMenuItems,
} from '@/utils/unit-types'

describe('unit types', () => {
  it('matches a suggestion without regard to case', () => {
    expect(unitTypeInfo('TIME')?.label).toBe('Time')
    expect(unitTypeInfo(' message ')?.definitionPlaceholder).toBe('orders.created')
    expect(unitTypeInfo('volume')?.definitionHelp).toContain('Cosmos DB')
  })

  it('gives an unknown value no suggestion', () => {
    expect(unitTypeInfo('event')).toBeUndefined()
    expect(unitTypeInfo('file')).toBeUndefined()
    expect(unitTypeInfo('')).toBeUndefined()
    expect(genericUnitDefinitionHelp).toContain('Volume')
  })

  it('keeps a custom value in the menu', () => {
    expect(unitTypeMenuItems('event').map((item) => item.value)).toEqual(['event', 'time', 'message', 'volume'])
    expect(unitTypeMenuItems('time').map((item) => item.value)).toEqual(['time', 'message', 'volume'])
    expect(unitTypeMenuItems(' TIME ').some((item) => item.value === 'TIME')).toBe(true)
  })

  it('hints only when a definition is not 5 or 6 fields', () => {
    expect(cronFieldHint('')).toBeUndefined()
    expect(cronFieldHint('   ')).toBeUndefined()
    expect(cronFieldHint('*/15 * * * *')).toBeUndefined()
    expect(cronFieldHint('0 0 1 * * *')).toBeUndefined()
    expect(cronFieldHint('hourly')).toContain('5 or 6')
    expect(cronFieldHint('* * *')).toContain('5 or 6')
    expect(cronFieldHint('0 0 0 0 0 0 0')).toContain('5 or 6')
  })

  it('recognizes a unit by its type or its key', () => {
    expect(isUnitAnnotation({ AnnotationType: 'Unit', AnnotationKey: 'unt.invoicing.pdf' })).toBe(true)
    expect(isUnitAnnotation({ AnnotationKey: 'unt.invoicing.pdf' })).toBe(true)
    expect(isUnitAnnotation({ AnnotationType: 'Unit', AnnotationKey: 'sbt.northwind' })).toBe(true)
    expect(isUnitAnnotation({ AnnotationType: 'Subject', AnnotationKey: 'sbt.northwind' })).toBe(false)
  })
})

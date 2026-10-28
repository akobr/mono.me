import { afterEach, describe, expect, it } from 'vitest'
import {
  configureStoryteller,
  getConfigurationConditional,
  getResolvedConfigurationConditional,
  isTransientError,
  StorytellerError,
} from '../../index'
import { filesOfKind, readConformance, toResponse, type ErrorsFile } from './conformance'

const files = filesOfKind('errors').map(path => ({ path, file: readConformance<ErrorsFile>(path) }))

afterEach(() => {
  configureStoryteller({ baseUrl: 'http://unused' })
})

describe('error-mapping vectors', () => {
  describe.each(files)('$path', ({ file }) => {
    // @organization.project.view.annotation-key
    const [organization = '', project = '', view = '', ...annotation] = file.key.slice(1).split('.')

    it.each(file.vectors)('$name', async vector => {
      const urls: string[] = []
      configureStoryteller({
        baseUrl: 'https://storyteller.example.com/api',
        // The response answers every request of the read.
        fetch: (async (url: RequestInfo | URL) => {
          urls.push(String(url))
          return toResponse(vector.response)
        }) as typeof fetch,
      })

      const read = vector.read === 'resolved' ? getResolvedConfigurationConditional : getConfigurationConditional
      const error = await read(organization, project, view, annotation.join('.')).then(
        () => undefined,
        (thrown: unknown) => thrown,
      )

      expect(error).toBeInstanceOf(StorytellerError)
      const failure = error as StorytellerError
      expect({ status: failure.status, errorCode: failure.errorCode ?? null, transient: isTransientError(failure) }).toEqual(vector.error)

      expect(urls).not.toHaveLength(0)

      for (const url of urls) {
        expect(new URL(url).pathname.endsWith('/resolved')).toBe(vector.read === 'resolved')
      }
    })
  })
})

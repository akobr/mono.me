// @42for.net/2splatform.sdk: plain fetch functions, types and helpers. No framework dependencies.
export * from './generated/client'
export * from './generated/model'
export {
  configureStoryteller,
  getStorytellerConfig,
  storytellerFetch,
  type StorytellerConfig,
} from './lib/fetcher'
export * from './lib/conditional'
export * from './lib/errors'
export * from './lib/federation'
export * from './lib/keys'
export * from './lib/key-values'
export * from './lib/paging'
export * from './lib/roles'

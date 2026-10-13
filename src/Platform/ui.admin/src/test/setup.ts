import { afterEach, beforeEach } from 'vitest'
import { nextTick } from 'vue'
import { createPinia, setActivePinia } from 'pinia'
import { configureStoryteller } from '@42for.net/2splatform.sdk'

// Every test starts with fresh stores, empty storage and an SDK pointing at the MSW test origin.
beforeEach(() => {
  setActivePinia(createPinia())
  configureStoryteller({ baseUrl: 'http://api.test/api', getAccessToken: () => 'test-token' })
})

afterEach(async () => {
  // Let watchers (VueUse useStorage) flush their writes before the storage is cleared.
  await nextTick()
  // Tests in the node environment (test/unit/build) have no Web Storage.
  globalThis.localStorage?.clear()
  globalThis.sessionStorage?.clear()
})

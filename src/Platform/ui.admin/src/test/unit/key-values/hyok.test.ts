import { describe, expect, it } from 'vitest'
import { webcrypto } from 'node:crypto'
import { decryptClientSecret, encryptForRecipients, getHyokPlace, readHyokHeader } from '@42for.net/2splatform.sdk/hyok'
import { mockRecipient } from '@/mock/key-values'

// HYOK encryption as the browser does it (WebCrypto): a value encrypted for the mock recipient has the right place,
// and a value encrypted for a fresh key pair decrypts with its private key.
const subtle = globalThis.crypto?.subtle ? globalThis.crypto : (webcrypto as unknown as Crypto)
if (!globalThis.crypto?.subtle) {
  Object.defineProperty(globalThis, 'crypto', { value: subtle, configurable: true })
}

describe('HYOK in the browser', () => {
  it('encrypts for the API recipient shape with the place in the protected header', async () => {
    const place = getHyokPlace('acme', 'billing', 'default', 'Worker.Key')
    const jwe = await encryptForRecipients('plain', [mockRecipient], place)

    expect(readHyokHeader(jwe)).toEqual({ enc: 'A256GCM', cty: 'text/plain', '2s': 'acme/billing/default/worker.key' })
    expect(JSON.parse(jwe).recipients[0].header.kid).toBe('invoicing-api')
  })

  it('round-trips with a generated key pair and refuses short keys', async () => {
    const pair = await crypto.subtle.generateKey({ name: 'RSA-OAEP', modulusLength: 3072, publicExponent: new Uint8Array([1, 0, 1]), hash: 'SHA-256' }, true, ['encrypt', 'decrypt'])
    const jwk = (await crypto.subtle.exportKey('jwk', pair.publicKey)) as JsonWebKey & { n: string; e: string }

    const jwe = await encryptForRecipients('s3cret', [{ id: 'test', publicKey: { n: jwk.n, e: jwk.e } }], 'acme/billing/default/k')

    expect(await decryptClientSecret(jwe, pair.privateKey, 'test')).toBe('s3cret')
    await expect(encryptForRecipients('x', [], 'acme/billing/default/k')).rejects.toThrow(/recipient/)
  }, 30_000)
})

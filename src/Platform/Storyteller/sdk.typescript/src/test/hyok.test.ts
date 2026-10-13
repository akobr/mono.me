import { GeneralEncrypt, generalDecrypt, importJWK } from 'jose'
import { beforeAll, describe, expect, it } from 'vitest'
import {
  decryptClientSecret,
  encryptForRecipients,
  generateRecipientKeyPair,
  getHyokPlace,
  getHyokRecipientIds,
  parseHyokDocument,
  readHyokHeader,
} from '../hyok'

// Client-side encryption on WebCrypto, checked against jose in both directions: what the SDK writes, jose reads, and the
// other way round, so the documents interoperate with any RFC 7516 implementation (and the C# library of the platform).
const place = getHyokPlace('acme', 'billing', 'default', 'Db.Password')

const toPem = (label: string, der: ArrayBuffer) => `-----BEGIN ${label}-----\n${Buffer.from(der).toString('base64').match(/.{1,64}/g)!.join('\n')}\n-----END ${label}-----`

let api: Awaited<ReturnType<typeof generateRecipientKeyPair>>
let worker: Awaited<ReturnType<typeof generateRecipientKeyPair>>

beforeAll(async () => {
  api = await generateRecipientKeyPair()
  worker = await generateRecipientKeyPair()
}, 60_000)

describe('encryptForRecipients', () => {
  it('writes a General JSON JWE with the place, and every recipient decrypts', async () => {
    const jwe = await encryptForRecipients('s3cret', [{ id: 'api', publicKey: api.publicJwk }, { id: 'worker', publicKey: worker.publicJwk }], place)
    const document = parseHyokDocument(jwe)

    expect(place).toBe('acme/billing/default/db.password')
    expect(readHyokHeader(document)).toEqual({ enc: 'A256GCM', cty: 'text/plain', '2s': place })
    expect(getHyokRecipientIds(document)).toEqual(['api', 'worker'])
    expect(document.recipients.map((entry) => entry.header.alg)).toEqual(['RSA-OAEP-256', 'RSA-OAEP-256'])
    expect(await decryptClientSecret(jwe, api.privateKey, 'api')).toBe('s3cret')
    expect(await decryptClientSecret(jwe, worker.privateJwk, 'worker')).toBe('s3cret')
  })

  it('accepts the API recipient shape and skips disabled ones', async () => {
    const recipients = [
      { Id: 'api', PublicKey: { Kty: 'RSA', N: api.publicJwk.n, E: api.publicJwk.e }, IsEnabled: true },
      { Id: 'old', PublicKey: { Kty: 'RSA', N: worker.publicJwk.n, E: worker.publicJwk.e }, IsEnabled: false },
    ]
    const jwe = await encryptForRecipients('v', recipients, place, 'application/json')

    expect(getHyokRecipientIds(jwe)).toEqual(['api'])
    expect(readHyokHeader(jwe).cty).toBe('application/json')
    await expect(decryptClientSecret(jwe, worker.privateKey, 'old')).rejects.toThrow(/not encrypted for the recipient/)
  })

  it('refuses no recipients, a bad place, and short keys', async () => {
    await expect(encryptForRecipients('v', [], place)).rejects.toThrow(/recipient/)
    await expect(encryptForRecipients('v', [{ id: 'api', publicKey: api.publicJwk }], 'acme/billing')).rejects.toThrow(/place/)

    const small = await crypto.subtle.generateKey({ name: 'RSA-OAEP', modulusLength: 2048, publicExponent: new Uint8Array([1, 0, 1]), hash: 'SHA-256' }, true, ['encrypt', 'decrypt'])
    const smallJwk = (await crypto.subtle.exportKey('jwk', small.publicKey)) as JsonWebKey & { n: string; e: string }
    await expect(encryptForRecipients('v', [{ id: 'small', publicKey: { n: smallJwk.n, e: smallJwk.e } }], place)).rejects.toThrow(/3072/)
  })

  it('detects tampering', async () => {
    const jwe = await encryptForRecipients('s3cret', [{ id: 'api', publicKey: api.publicJwk }], place)
    const document = parseHyokDocument(jwe)
    const moved = { ...document, protected: Buffer.from(JSON.stringify({ enc: 'A256GCM', cty: 'text/plain', '2s': 'acme/billing/default/other' })).toString('base64url') }

    await expect(decryptClientSecret(JSON.stringify(moved), api.privateKey, 'api')).rejects.toThrow()
    await expect(decryptClientSecret('{"protected":"x"}', api.privateKey, 'api')).rejects.toThrow(/missing|recipients/)
    await expect(decryptClientSecret('not json', api.privateKey, 'api')).rejects.toThrow(/not JSON/)
  })
})

describe('interoperability with jose', () => {
  it('jose decrypts what the SDK writes', async () => {
    const jwe = await encryptForRecipients('from the sdk', [{ id: 'api', publicKey: api.publicJwk }], place)
    const privateKey = await importJWK(api.privateJwk as Parameters<typeof importJWK>[0], 'RSA-OAEP-256')

    const result = await generalDecrypt(JSON.parse(jwe), privateKey)

    expect(new TextDecoder().decode(result.plaintext)).toBe('from the sdk')
    expect(result.protectedHeader).toEqual({ enc: 'A256GCM', cty: 'text/plain', '2s': place })
    expect(result.unprotectedHeader).toEqual({ alg: 'RSA-OAEP-256', kid: 'api' })
  })

  it('the SDK decrypts what jose writes', async () => {
    const publicKey = await importJWK({ ...api.publicJwk } as Parameters<typeof importJWK>[0], 'RSA-OAEP-256')
    const jwe = await new GeneralEncrypt(new TextEncoder().encode('from jose'))
      .setProtectedHeader({ enc: 'A256GCM', cty: 'text/plain', '2s': place })
      .addRecipient(publicKey)
      .setUnprotectedHeader({ alg: 'RSA-OAEP-256', kid: 'api' })
      .encrypt()

    expect(await decryptClientSecret(JSON.stringify(jwe), api.privateKey, 'api')).toBe('from jose')
    expect(readHyokHeader(JSON.stringify(jwe))['2s']).toBe(place)
  })

  it('reads a PKCS#8 PEM private key and a SPKI PEM public key', async () => {
    const pair = await crypto.subtle.generateKey({ name: 'RSA-OAEP', modulusLength: 3072, publicExponent: new Uint8Array([1, 0, 1]), hash: 'SHA-256' }, true, ['encrypt', 'decrypt'])
    const publicPem = toPem('PUBLIC KEY', await crypto.subtle.exportKey('spki', pair.publicKey))
    const privatePem = toPem('PRIVATE KEY', await crypto.subtle.exportKey('pkcs8', pair.privateKey))

    const jwe = await encryptForRecipients('pem', [{ id: 'pem', publicKey: publicPem }], place)

    expect(await decryptClientSecret(jwe, privatePem, 'pem')).toBe('pem')
  })
})

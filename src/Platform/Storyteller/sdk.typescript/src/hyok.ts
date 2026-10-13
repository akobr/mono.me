// @42for.net/2splatform.sdk/hyok: client-side encryption of key/value secrets (the Hyok level) on WebCrypto, in browsers and
// in Node 22 or later, without runtime dependencies. The format is JWE General JSON Serialization (RFC 7516): A256GCM content,
// one RSA-OAEP-256 recipient per registered public key, and a protected header with the content type and the place
// "{organization}/{project}/{view}/{lower-cased key}". The C# library KeyValues.ClientEncryption and the CLI read and write
// the same documents.

/** A recipient as GET …/key-values/recipients returns it (Id and PublicKey.N/E), or a plain id and RSA public JWK. */
export interface HyokRecipient {
  id: string
  /** An RSA public key as JWK (`n`, `e`), or its PEM (`-----BEGIN PUBLIC KEY-----`). */
  publicKey: RsaPublicJwk | string
}

export interface RsaPublicJwk {
  kty?: string
  n: string
  e: string
}

/** The generated model shape, accepted as it is. */
export interface ApiRecipient {
  Id?: string | null
  PublicKey?: { Kty?: string | null; N?: string | null; E?: string | null } | null
  IsEnabled?: boolean | null
}

export const hyokContentEncryption = 'A256GCM'
export const hyokKeyAlgorithm = 'RSA-OAEP-256'
export const minimumRecipientModulusBits = 3072

export interface HyokDocument {
  protected: string
  recipients: { header: { alg: string; kid: string }; encrypted_key: string }[]
  iv: string
  ciphertext: string
  tag: string
}

export interface HyokHeader {
  enc?: string
  cty?: string
  /** The place the value was encrypted for: "{organization}/{project}/{view}/{lower-cased key}". */
  '2s'?: string
}

/** The protected-header place of a key. */
export function getHyokPlace(organization: string, project: string, view: string, key: string): string {
  for (const [name, value] of Object.entries({ organization, project, view, key })) {
    if (typeof value !== 'string' || value.trim().length === 0) {
      throw new Error(`The ${name} is required.`)
    }
  }

  return `${organization}/${project}/${view}/${key.trim().toLowerCase()}`
}

/**
 * Encrypts a value for the recipients of a project. Disabled API recipients are skipped; at least one recipient is needed.
 * The result is the string to send as the Value of a Hyok pair.
 */
export async function encryptForRecipients(
  value: string,
  recipients: readonly (HyokRecipient | ApiRecipient)[],
  place: string,
  contentType = 'text/plain',
): Promise<string> {
  const keys = normalizeRecipients(recipients)

  if (keys.length === 0) {
    throw new Error('At least one enabled recipient is required.')
  }

  if (typeof place !== 'string' || place.split('/').length !== 4) {
    throw new Error('The place must be "{organization}/{project}/{view}/{key}"; use getHyokPlace.')
  }

  const header: HyokHeader = { enc: hyokContentEncryption, cty: contentType, '2s': place }
  const protectedBase64 = base64UrlEncode(utf8Encode(JSON.stringify(header)))
  const contentKey = crypto.getRandomValues(new Uint8Array(32))
  const iv = crypto.getRandomValues(new Uint8Array(12))

  try {
    const aesKey = await crypto.subtle.importKey('raw', contentKey, { name: 'AES-GCM' }, false, ['encrypt'])
    const sealed = new Uint8Array(
      await crypto.subtle.encrypt(
        { name: 'AES-GCM', iv, additionalData: asciiEncode(protectedBase64), tagLength: 128 },
        aesKey,
        utf8Encode(value),
      ),
    )
    const ciphertext = sealed.slice(0, sealed.length - 16)
    const tag = sealed.slice(sealed.length - 16)

    const wrapped: HyokDocument['recipients'] = []

    for (const recipient of keys) {
      const publicKey = await importRecipientKey(recipient.publicKey)
      const encryptedKey = new Uint8Array(await crypto.subtle.encrypt({ name: 'RSA-OAEP' }, publicKey, contentKey))
      wrapped.push({ header: { alg: hyokKeyAlgorithm, kid: recipient.id }, encrypted_key: base64UrlEncode(encryptedKey) })
    }

    const document: HyokDocument = {
      protected: protectedBase64,
      recipients: wrapped,
      iv: base64UrlEncode(iv),
      ciphertext: base64UrlEncode(ciphertext),
      tag: base64UrlEncode(tag),
    }

    return JSON.stringify(document)
  } finally {
    contentKey.fill(0)
  }
}

/**
 * Decrypts a Hyok document (a reveal, a secret version, an export or a binding) with one recipient's private key:
 * a CryptoKey (RSA-OAEP, SHA-256, usage decrypt), a private JWK, or a PKCS#8 PEM (`-----BEGIN PRIVATE KEY-----`).
 */
export async function decryptClientSecret(jwe: string, privateKey: CryptoKey | JsonWebKey | string, recipientId: string): Promise<string> {
  const document = parseHyokDocument(jwe)
  const recipient = document.recipients.find((entry) => entry.header.kid === recipientId)

  if (!recipient) {
    throw new Error(`The value was not encrypted for the recipient '${recipientId}'.`)
  }

  const header = readHyokHeader(document)

  if (recipient.header.alg !== hyokKeyAlgorithm || header.enc !== hyokContentEncryption) {
    throw new Error('The value uses an algorithm this decryptor does not support.')
  }

  const key = await importPrivateKey(privateKey)
  const contentKey = new Uint8Array(await crypto.subtle.decrypt({ name: 'RSA-OAEP' }, key, base64UrlDecode(recipient.encrypted_key)))

  try {
    const aesKey = await crypto.subtle.importKey('raw', contentKey, { name: 'AES-GCM' }, false, ['decrypt'])
    const ciphertext = base64UrlDecode(document.ciphertext)
    const tag = base64UrlDecode(document.tag)
    const sealed = new Uint8Array(ciphertext.length + tag.length)
    sealed.set(ciphertext)
    sealed.set(tag, ciphertext.length)

    const plaintext = await crypto.subtle.decrypt(
      { name: 'AES-GCM', iv: base64UrlDecode(document.iv), additionalData: asciiEncode(document.protected), tagLength: 128 },
      aesKey,
      sealed,
    )

    return new TextDecoder().decode(plaintext)
  } finally {
    contentKey.fill(0)
  }
}

/** Parses the JSON of a Hyok document and checks its shape. */
export function parseHyokDocument(jwe: string): HyokDocument {
  let parsed: unknown

  try {
    parsed = JSON.parse(jwe)
  } catch (error) {
    throw new Error('The client-encrypted value is not JSON.', { cause: error })
  }

  if (!isRecord(parsed)) {
    throw new Error('The client-encrypted value is not a JSON object.')
  }

  const { protected: protectedBase64, recipients, iv, ciphertext, tag } = parsed

  if (typeof protectedBase64 !== 'string' || typeof iv !== 'string' || typeof ciphertext !== 'string' || typeof tag !== 'string') {
    throw new Error('The client-encrypted value is missing protected, iv, ciphertext or tag.')
  }

  if (!Array.isArray(recipients) || recipients.length === 0) {
    throw new Error('The client-encrypted value has no recipients.')
  }

  const entries = recipients.map((entry) => {
    if (!isRecord(entry) || !isRecord(entry.header) || typeof entry.header.alg !== 'string' || typeof entry.header.kid !== 'string' || typeof entry.encrypted_key !== 'string') {
      throw new Error('A recipient of the client-encrypted value is malformed.')
    }

    return { header: { alg: entry.header.alg, kid: entry.header.kid }, encrypted_key: entry.encrypted_key }
  })

  return { protected: protectedBase64, recipients: entries, iv, ciphertext, tag }
}

/** The protected header of a document: `enc`, `cty` and the place `2s`. */
export function readHyokHeader(document: HyokDocument | string): HyokHeader {
  const parsed = typeof document === 'string' ? parseHyokDocument(document) : document
  const header: unknown = JSON.parse(new TextDecoder().decode(base64UrlDecode(parsed.protected)))

  if (!isRecord(header)) {
    throw new Error('The protected header is not a JSON object.')
  }

  return header as HyokHeader
}

/** The recipient ids a document was encrypted for. */
export const getHyokRecipientIds = (document: HyokDocument | string): string[] =>
  (typeof document === 'string' ? parseHyokDocument(document) : document).recipients.map((entry) => entry.header.kid)

/** Generates an RSA-OAEP-256 key pair of 3072 bits for a new recipient; register the public JWK, keep the private one. */
export async function generateRecipientKeyPair(modulusLength = minimumRecipientModulusBits): Promise<{ publicJwk: RsaPublicJwk; privateJwk: JsonWebKey; privateKey: CryptoKey }> {
  const pair = await crypto.subtle.generateKey(
    { name: 'RSA-OAEP', modulusLength, publicExponent: new Uint8Array([1, 0, 1]), hash: 'SHA-256' },
    true,
    ['encrypt', 'decrypt'],
  )
  const publicJwk = (await crypto.subtle.exportKey('jwk', pair.publicKey)) as JsonWebKey & RsaPublicJwk
  const privateJwk = await crypto.subtle.exportKey('jwk', pair.privateKey)

  return { publicJwk: { kty: 'RSA', n: publicJwk.n, e: publicJwk.e }, privateJwk, privateKey: pair.privateKey }
}

function normalizeRecipients(recipients: readonly (HyokRecipient | ApiRecipient)[]): HyokRecipient[] {
  const result: HyokRecipient[] = []

  for (const recipient of recipients) {
    if ('id' in recipient && typeof recipient.id === 'string') {
      result.push(recipient)
      continue
    }

    const api = recipient as ApiRecipient

    if (api.IsEnabled === false || !api.Id || !api.PublicKey?.N || !api.PublicKey.E) {
      continue
    }

    result.push({ id: api.Id, publicKey: { kty: 'RSA', n: api.PublicKey.N, e: api.PublicKey.E } })
  }

  return result
}

async function importRecipientKey(publicKey: RsaPublicJwk | string): Promise<CryptoKey> {
  const algorithm = { name: 'RSA-OAEP', hash: 'SHA-256' } as const

  if (typeof publicKey === 'string') {
    const der = pemToDer(publicKey, 'PUBLIC KEY')
    return crypto.subtle.importKey('spki', der, algorithm, false, ['encrypt'])
  }

  const modulusBits = base64UrlDecode(publicKey.n).length * 8

  if (modulusBits < minimumRecipientModulusBits) {
    throw new Error(`The recipient key has ${modulusBits} bits; at least ${minimumRecipientModulusBits} are required.`)
  }

  return crypto.subtle.importKey('jwk', { kty: 'RSA', n: publicKey.n, e: publicKey.e, alg: hyokKeyAlgorithm, ext: true }, algorithm, false, ['encrypt'])
}

async function importPrivateKey(privateKey: CryptoKey | JsonWebKey | string): Promise<CryptoKey> {
  const algorithm = { name: 'RSA-OAEP', hash: 'SHA-256' } as const

  if (typeof privateKey === 'string') {
    return crypto.subtle.importKey('pkcs8', pemToDer(privateKey, 'PRIVATE KEY'), algorithm, false, ['decrypt'])
  }

  if (isCryptoKey(privateKey)) {
    return privateKey
  }

  return crypto.subtle.importKey('jwk', { ...privateKey, alg: hyokKeyAlgorithm, ext: true }, algorithm, false, ['decrypt'])
}

function isCryptoKey(value: unknown): value is CryptoKey {
  return typeof value === 'object' && value !== null && 'type' in value && 'algorithm' in value && 'usages' in value
}

function pemToDer(pem: string, label: string): Uint8Array<ArrayBuffer> {
  const body = pem
    .replace(new RegExp(`-----BEGIN ${label}-----`), '')
    .replace(new RegExp(`-----END ${label}-----`), '')
    .replace(/\s+/g, '')

  if (body.length === 0 || !pem.includes(`BEGIN ${label}`)) {
    throw new Error(`The key is not a PEM ${label}.`)
  }

  return base64Decode(body)
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

const utf8Encode = (text: string): Uint8Array<ArrayBuffer> => new Uint8Array(new TextEncoder().encode(text))

function asciiEncode(text: string): Uint8Array<ArrayBuffer> {
  const bytes = new Uint8Array(text.length)

  for (let index = 0; index < text.length; index++) {
    bytes[index] = text.charCodeAt(index)
  }

  return bytes
}

function base64UrlEncode(bytes: Uint8Array): string {
  let binary = ''

  for (const byte of bytes) {
    binary += String.fromCharCode(byte)
  }

  return btoa(binary).replaceAll('+', '-').replaceAll('/', '_').replace(/=+$/, '')
}

function base64UrlDecode(text: string): Uint8Array<ArrayBuffer> {
  return base64Decode(text.replaceAll('-', '+').replaceAll('_', '/'))
}

function base64Decode(text: string): Uint8Array<ArrayBuffer> {
  const padded = text + '='.repeat((4 - (text.length % 4)) % 4)
  return asciiEncode(atob(padded))
}

/**
 * Accepts only a same-origin path ("/orgs?x=1"), never an absolute URL or a protocol-relative "//host".
 * AuthKit round-trips the state through the URL without integrity protection, so it is untrusted input.
 */
export function safeReturnTo(value: unknown, origin: string = location.origin): string | undefined {
  if (typeof value !== 'string' || !value.startsWith('/') || value.startsWith('//') || value.startsWith('/\\')) {
    return undefined
  }

  try {
    const url = new URL(value, origin)
    return url.origin === origin ? `${url.pathname}${url.search}${url.hash}` : undefined
  } catch {
    return undefined
  }
}

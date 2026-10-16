// A small zip writer and reader for the mock API and the tests: the writer stores entries uncompressed, the reader
// takes stored and deflated entries (DecompressionStream). No zip64, no encryption, no comments.

export interface ZipFile {
  name: string
  data: Uint8Array
  /** The time of the entry when it is written; the zip's time when unset. The reader does not set it. */
  modified?: Date
}

const encoder = new TextEncoder()

const crcTable = (() => {
  const table = new Uint32Array(256)
  for (let index = 0; index < 256; index++) {
    let value = index
    for (let bit = 0; bit < 8; bit++) {
      value = value & 1 ? 0xedb88320 ^ (value >>> 1) : value >>> 1
    }
    table[index] = value >>> 0
  }
  return table
})()

export function crc32(data: Uint8Array): number {
  let crc = 0xffffffff
  for (const byte of data) {
    crc = crcTable[(crc ^ byte) & 0xff]! ^ (crc >>> 8)
  }
  return (crc ^ 0xffffffff) >>> 0
}

/** The DOS time and date of a zip entry, in the clock time of the given date. */
function dosTime(date: Date): [number, number] {
  const time = (date.getHours() << 11) | (date.getMinutes() << 5) | Math.floor(date.getSeconds() / 2)
  const day = ((date.getFullYear() - 1980) << 9) | ((date.getMonth() + 1) << 5) | date.getDate()
  return [time, day]
}

export function textFile(name: string, text: string, modified?: Date): ZipFile {
  return { name, data: encoder.encode(text), ...(modified ? { modified } : {}) }
}

/** A zip with stored (uncompressed) entries and UTF-8 names; each entry has its own time or the zip's. */
export function createZip(files: ZipFile[], modified = new Date()): Uint8Array<ArrayBuffer> {
  const locals: Uint8Array[] = []
  const centrals: Uint8Array[] = []
  let offset = 0

  for (const file of files) {
    const [time, date] = dosTime(file.modified ?? modified)
    const name = encoder.encode(file.name)
    const crc = crc32(file.data)

    const local = new Uint8Array(30 + name.length)
    const localView = new DataView(local.buffer)
    localView.setUint32(0, 0x04034b50, true)
    localView.setUint16(4, 20, true)
    localView.setUint16(6, 0x0800, true)
    localView.setUint16(8, 0, true)
    localView.setUint16(10, time, true)
    localView.setUint16(12, date, true)
    localView.setUint32(14, crc, true)
    localView.setUint32(18, file.data.length, true)
    localView.setUint32(22, file.data.length, true)
    localView.setUint16(26, name.length, true)
    local.set(name, 30)

    const central = new Uint8Array(46 + name.length)
    const centralView = new DataView(central.buffer)
    centralView.setUint32(0, 0x02014b50, true)
    centralView.setUint16(4, 20, true)
    centralView.setUint16(6, 20, true)
    centralView.setUint16(8, 0x0800, true)
    centralView.setUint16(10, 0, true)
    centralView.setUint16(12, time, true)
    centralView.setUint16(14, date, true)
    centralView.setUint32(16, crc, true)
    centralView.setUint32(20, file.data.length, true)
    centralView.setUint32(24, file.data.length, true)
    centralView.setUint16(28, name.length, true)
    centralView.setUint32(42, offset, true)
    central.set(name, 46)

    locals.push(local, file.data)
    centrals.push(central)
    offset += local.length + file.data.length
  }

  const centralSize = centrals.reduce((sum, part) => sum + part.length, 0)
  const end = new Uint8Array(22)
  const endView = new DataView(end.buffer)
  endView.setUint32(0, 0x06054b50, true)
  endView.setUint16(8, files.length, true)
  endView.setUint16(10, files.length, true)
  endView.setUint32(12, centralSize, true)
  endView.setUint32(16, offset, true)

  const result = new Uint8Array(offset + centralSize + end.length)
  let position = 0
  for (const part of [...locals, ...centrals, end]) {
    result.set(part, position)
    position += part.length
  }
  return result
}

async function inflateRaw(data: Uint8Array): Promise<Uint8Array> {
  const stream = new Blob([data as Uint8Array<ArrayBuffer>])
    .stream()
    .pipeThrough(new DecompressionStream('deflate-raw'))
  return new Uint8Array(await new Response(stream).arrayBuffer())
}

/** The files of a zip (directories left out); throws for anything that is not a readable zip. */
export async function readZip(bytes: Uint8Array): Promise<ZipFile[]> {
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength)
  let end = -1

  for (let position = bytes.length - 22; position >= Math.max(0, bytes.length - 22 - 0xffff); position--) {
    if (view.getUint32(position, true) === 0x06054b50) {
      end = position
      break
    }
  }

  if (end < 0) {
    throw new Error('The archive is not a valid zip.')
  }

  const count = view.getUint16(end + 10, true)
  let position = view.getUint32(end + 16, true)
  const decoder = new TextDecoder()
  const files: ZipFile[] = []

  for (let index = 0; index < count; index++) {
    if (view.getUint32(position, true) !== 0x02014b50) {
      throw new Error('The archive is not a valid zip.')
    }

    const method = view.getUint16(position + 10, true)
    const compressedSize = view.getUint32(position + 20, true)
    const nameLength = view.getUint16(position + 28, true)
    const extraLength = view.getUint16(position + 30, true)
    const commentLength = view.getUint16(position + 32, true)
    const localOffset = view.getUint32(position + 42, true)
    const name = decoder.decode(bytes.subarray(position + 46, position + 46 + nameLength))
    position += 46 + nameLength + extraLength + commentLength

    if (name.endsWith('/')) {
      continue
    }

    const dataStart = localOffset + 30 + view.getUint16(localOffset + 26, true) + view.getUint16(localOffset + 28, true)
    const raw = bytes.subarray(dataStart, dataStart + compressedSize)

    if (method !== 0 && method !== 8) {
      throw new Error(`The entry ${name} uses an unsupported compression.`)
    }

    files.push({ name, data: method === 8 ? await inflateRaw(raw) : raw.slice() })
  }

  return files
}

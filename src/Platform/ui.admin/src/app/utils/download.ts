/**
 * Saves a blob as a file through a temporary link. The archive of an export is fetched with the bearer token, so
 * a plain link to the API cannot download it.
 */
export function saveBlob(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = fileName
  link.rel = 'noopener'
  link.style.display = 'none'
  document.body.append(link)
  link.click()
  link.remove()

  // the browser has taken the file by then; revoking at once can cancel the download in some browsers
  setTimeout(() => URL.revokeObjectURL(url), 60_000)
}

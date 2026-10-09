// Continuation-token paging of the list endpoints (annotations, configurations, …).

export interface ContinuationPage {
  ContinuationToken?: string | null
}

export type PageFetcher<TPage extends ContinuationPage> = (
  continuationToken: string | undefined,
  signal?: AbortSignal,
) => Promise<TPage>

/** Yields every page until the API stops returning a continuation token. */
export async function* paginate<TPage extends ContinuationPage>(
  fetchPage: PageFetcher<TPage>,
  signal?: AbortSignal,
): AsyncGenerator<TPage, void, undefined> {
  let token: string | undefined

  do {
    signal?.throwIfAborted()
    const page = await fetchPage(token, signal)
    yield page
    token = page.ContinuationToken ?? undefined
  } while (token)
}

/** Collects the items of every page, for example (page) => page.Annotations. */
export async function collectAll<TPage extends ContinuationPage, TItem>(
  fetchPage: PageFetcher<TPage>,
  selectItems: (page: TPage) => readonly TItem[] | null | undefined,
  signal?: AbortSignal,
): Promise<TItem[]> {
  const items: TItem[] = []

  for await (const page of paginate(fetchPage, signal)) {
    items.push(...(selectItems(page) ?? []))
  }

  return items
}

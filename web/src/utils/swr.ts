export const DEFAULT_TTL_MS = 30_000

export interface SwrOptions {
  force?: boolean
  silent?: boolean
  ttlMs?: number
}

export interface SwrChannel<T = unknown> {
  run: (
    opts: SwrOptions | undefined,
    hasCachedData: () => boolean,
    fetcher: () => Promise<T>,
    setLoading: (val: boolean) => void
  ) => Promise<T | undefined>
  invalidate: () => void
  touch: () => void
}

/**
 * Stale-while-revalidate helper with 30s freshness TTL and in-flight deduplication.
 * Keeps `loading` false whenever cached data already exists or `silent: true` is passed.
 */
export function createSwrChannel<T = unknown>(defaultTtlMs = DEFAULT_TTL_MS): SwrChannel<T> {
  let lastFetchedAt = 0
  let inflight: Promise<T> | null = null

  function isFresh(ttlMs: number): boolean {
    return lastFetchedAt > 0 && Date.now() - lastFetchedAt < ttlMs
  }

  async function run(
    opts: SwrOptions | undefined,
    hasCachedData: () => boolean,
    fetcher: () => Promise<T>,
    setLoading: (val: boolean) => void
  ): Promise<T | undefined> {
    const force = opts?.force ?? false
    const silent = opts?.silent ?? false
    const ttlMs = opts?.ttlMs ?? defaultTtlMs

    if (!force && hasCachedData() && isFresh(ttlMs)) {
      setLoading(false)
      return undefined
    }

    if (inflight) {
      return inflight
    }

    const shouldShowLoading = !silent && !hasCachedData()
    if (shouldShowLoading) {
      setLoading(true)
    }

    inflight = fetcher()
      .then((res) => {
        lastFetchedAt = Date.now()
        return res
      })
      .finally(() => {
        inflight = null
        if (shouldShowLoading) {
          setLoading(false)
        }
      })

    return inflight
  }

  function invalidate(): void {
    lastFetchedAt = 0
  }

  function touch(): void {
    lastFetchedAt = Date.now()
  }

  return { run, invalidate, touch }
}

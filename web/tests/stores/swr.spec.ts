import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { createSwrChannel } from '../../src/utils/swr'
import { useConfigPipelinesStore } from '../../src/stores/configPipelines'
import * as apiModule from '../../src/api'

describe('SWR caching, TTL, deduplication & invalidation', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-11-20T10:00:00Z'))
    setActivePinia(createPinia())
  })

  afterEach(() => {
    vi.useRealTimers()
    vi.restoreAllMocks()
  })

  it('shows loading only on initial uncached fetch, skips within 30s TTL, and refreshes silently after TTL', async () => {
    let resolveFirst!: (v: string[]) => void
    const fetcher = vi
      .fn()
      .mockImplementationOnce(
        () =>
          new Promise<string[]>((res) => {
            resolveFirst = res
          })
      )
      .mockResolvedValueOnce(['a', 'b'])

    const loadingStates: boolean[] = []
    let hasCached = false
    const swr = createSwrChannel<string[]>(30_000)

    // 1. Initial fetch (no cache) -> loading becomes true
    const p1 = swr.run(undefined, () => hasCached, fetcher, (v) => loadingStates.push(v))
    expect(loadingStates).toEqual([true])
    resolveFirst(['a'])
    await p1
    hasCached = true
    expect(loadingStates).toEqual([true, false])
    expect(fetcher).toHaveBeenCalledTimes(1)

    // 2. Within 30s TTL -> skipped, loading stays false
    vi.advanceTimersByTime(15_000)
    await swr.run(undefined, () => hasCached, fetcher, (v) => loadingStates.push(v))
    expect(fetcher).toHaveBeenCalledTimes(1)

    // 3. After 30s TTL -> background refresh runs, loading never becomes true
    vi.advanceTimersByTime(16_000)
    const beforeCallCount = loadingStates.length
    const res2 = await swr.run(undefined, () => hasCached, fetcher, (v) => loadingStates.push(v))
    expect(fetcher).toHaveBeenCalledTimes(2)
    expect(res2).toEqual(['a', 'b'])
    expect(loadingStates.slice(beforeCallCount)).toEqual([])
  })

  it('deduplicates concurrent in-flight requests', async () => {
    let resolveFetch!: (v: number[]) => void
    const fetcher = vi.fn().mockImplementation(
      () =>
        new Promise<number[]>((res) => {
          resolveFetch = res
        })
    )
    const swr = createSwrChannel<number[]>(30_000)

    const p1 = swr.run(undefined, () => false, fetcher, () => {})
    const p2 = swr.run(undefined, () => false, fetcher, () => {})

    expect(fetcher).toHaveBeenCalledTimes(1)
    resolveFetch([1, 2, 3])
    const [r1, r2] = await Promise.all([p1, p2])
    expect(r1).toEqual([1, 2, 3])
    expect(r2).toEqual([1, 2, 3])
  })

  it('useConfigPipelinesStore caches within 30s TTL and invalidates on createPipeline', async () => {
    const getPipelines = vi.fn().mockResolvedValue([
      { id: 1, name: 'api', gitRepoUrl: 'https://example.com/api.git', branch: 'main' }
    ])
    const createPipeline = vi.fn().mockResolvedValue({
      id: 2,
      name: 'web',
      gitRepoUrl: 'https://example.com/web.git',
      branch: 'main'
    })

    vi.spyOn(apiModule, 'getApi').mockReturnValue({
      getPipelines,
      createPipeline
    } as any)

    const store = useConfigPipelinesStore()

    await store.fetchPipelines()
    expect(getPipelines).toHaveBeenCalledTimes(1)
    expect(store.loading).toBe(false)

    // Revisit within 30s -> skipped
    await store.fetchPipelines({ silent: true })
    expect(getPipelines).toHaveBeenCalledTimes(1)

    // Mutation invalidates TTL and refreshes
    await store.createPipeline({
      name: 'web',
      gitRepoUrl: 'https://example.com/web.git',
      branch: 'main',
      dockerfilePath: 'Dockerfile',
      buildContext: null,
      imageName: 'web',
      slaveId: 1,
      deployTargetId: 1,
      gitCredentialId: null,
      registryCredentialId: null,
      deployRegistryHost: null
    } as any)
    expect(createPipeline).toHaveBeenCalledTimes(1)
    expect(getPipelines).toHaveBeenCalledTimes(2)
  })
})

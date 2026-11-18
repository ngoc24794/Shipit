import { ref, reactive, computed, watch, type Ref, type ComputedRef } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import type { PagedResult } from '../api/types'

export type SortDirection = 'asc' | 'desc' | null

export interface DataTableServerParams {
  q: string
  filters: Record<string, string>
  sortBy: string | null
  sortDir: SortDirection
  page: number
  pageSize: number
}

export interface UseDataTableOptions<T> {
  mode: 'client' | 'server'
  /** Client mode: reactive source array */
  items?: Ref<T[]> | ComputedRef<T[]>
  /** Client mode: keys or getter functions to search against `q` */
  searchFields?: Array<keyof T | ((item: T) => string | number | null | undefined)>
  /** Client mode: custom search predicate */
  searchFn?: (item: T, q: string) => boolean
  /** Client mode: per-filter predicate */
  filterFns?: Record<string, (item: T, value: string) => boolean>
  /** Client mode: per-column comparator or accessor */
  sortFns?: Record<string, (a: T, b: T) => number>
  /** Server mode: fetch function */
  fetchFn?: (params: DataTableServerParams) => Promise<PagedResult<T>>
  /** Initial filter keys and default values */
  defaultFilters?: Record<string, string>
  /** Default page size (10, 20, 50, 100) */
  defaultPageSize?: number
  /** Default sort column */
  defaultSortBy?: string | null
  /** Default sort direction */
  defaultSortDir?: SortDirection
  /** Sync state with URL query parameters (default: true) */
  syncUrl?: boolean
  /** Query parameter prefix when multiple tables share a route */
  queryPrefix?: string
  /** Auto-fetch in server mode on mount/change (default: false; caller triggers initial load or sets true) */
  immediate?: boolean
}

const VALID_PAGE_SIZES = [10, 20, 50, 100]

function normalizePageSize(val: unknown, fallback: number): number {
  const num = Number(val)
  return VALID_PAGE_SIZES.includes(num) ? num : fallback
}

function normalizePage(val: unknown): number {
  const num = Number(val)
  return Number.isInteger(num) && num >= 1 ? num : 1
}

export function useDataTable<T>(options: UseDataTableOptions<T>) {
  const defaultPageSize = options.defaultPageSize ?? 20
  const defaultFilters = options.defaultFilters ?? {}
  const syncUrl = options.syncUrl ?? true
  const prefix = options.queryPrefix ?? ''

  let route: ReturnType<typeof useRoute> | undefined
  let router: ReturnType<typeof useRouter> | undefined
  try {
    route = useRoute()
    router = useRouter()
  } catch {
    route = undefined
    router = undefined
  }

  const filterKeys = Object.keys(defaultFilters)
  const qKey = `${prefix}q`
  const pageKey = `${prefix}page`
  const pageSizeKey = `${prefix}pageSize`
  const sortByKey = `${prefix}sortBy`
  const sortDirKey = `${prefix}sortDir`

  function readQueryString(key: string): string {
    if (!syncUrl || !route) return ''
    const raw = route.query[key]
    if (Array.isArray(raw)) return String(raw[0] ?? '')
    return raw !== undefined && raw !== null ? String(raw) : ''
  }

  const q = ref<string>(readQueryString(qKey))
  const page = ref<number>(normalizePage(readQueryString(pageKey) || 1))
  const pageSize = ref<number>(normalizePageSize(readQueryString(pageSizeKey), defaultPageSize))
  const sortBy = ref<string | null>(readQueryString(sortByKey) || options.defaultSortBy || null)
  const initialSortDir = readQueryString(sortDirKey)
  const sortDir = ref<SortDirection>(
    initialSortDir === 'asc' || initialSortDir === 'desc'
      ? initialSortDir
      : options.defaultSortDir ?? null
  )

  const filters = reactive<Record<string, string>>({})
  for (const key of filterKeys) {
    filters[key] = readQueryString(`${prefix}${key}`) || defaultFilters[key] || ''
  }

  // Server state
  const serverItems = ref<T[]>([]) as Ref<T[]>
  const serverTotal = ref(0)
  const serverPaginated = ref(true)
  const serverLoading = ref(false)
  const serverError = ref<string | null>(null)

  let isSyncingFromUrl = false
  let fetchSeq = 0

  function buildNextQuery(): Record<string, string | string[] | undefined> {
    const current = route ? { ...route.query } : {}
    const next: Record<string, any> = { ...current }

    if (q.value.trim() !== '') {
      next[qKey] = q.value.trim()
    } else {
      delete next[qKey]
    }

    for (const key of filterKeys) {
      const fullKey = `${prefix}${key}`
      const val = filters[key] ?? ''
      const def = defaultFilters[key] ?? ''
      if (val !== '' && val !== def) {
        next[fullKey] = val
      } else {
        delete next[fullKey]
      }
    }

    if (page.value > 1) {
      next[pageKey] = String(page.value)
    } else {
      delete next[pageKey]
    }

    if (pageSize.value !== defaultPageSize) {
      next[pageSizeKey] = String(pageSize.value)
    } else {
      delete next[pageSizeKey]
    }

    if (sortBy.value && sortDir.value) {
      next[sortByKey] = sortBy.value
      next[sortDirKey] = sortDir.value
    } else {
      delete next[sortByKey]
      delete next[sortDirKey]
    }

    return next
  }

  function queriesEqual(a: Record<string, any>, b: Record<string, any>): boolean {
    const keysA = Object.keys(a).filter((k) => a[k] !== undefined && a[k] !== '')
    const keysB = Object.keys(b).filter((k) => b[k] !== undefined && b[k] !== '')
    if (keysA.length !== keysB.length) return false
    return keysA.every((k) => String(a[k]) === String(b[k]))
  }

  function pushStateToUrl() {
    if (!syncUrl || !router || !route || isSyncingFromUrl) return
    const nextQuery = buildNextQuery()
    if (!queriesEqual(route.query, nextQuery)) {
      router.replace({ query: nextQuery }).catch(() => {})
    }
  }

  if (syncUrl && route) {
    watch(
      () => route.query,
      () => {
        isSyncingFromUrl = true
        try {
          const nextQ = readQueryString(qKey)
          if (q.value !== nextQ) q.value = nextQ

          const nextPage = normalizePage(readQueryString(pageKey) || 1)
          if (page.value !== nextPage) page.value = nextPage

          const nextSize = normalizePageSize(readQueryString(pageSizeKey), defaultPageSize)
          if (pageSize.value !== nextSize) pageSize.value = nextSize

          const nextSortBy = readQueryString(sortByKey) || options.defaultSortBy || null
          if (sortBy.value !== nextSortBy) sortBy.value = nextSortBy

          const rawDir = readQueryString(sortDirKey)
          const nextSortDir: SortDirection =
            rawDir === 'asc' || rawDir === 'desc' ? rawDir : options.defaultSortDir ?? null
          if (sortDir.value !== nextSortDir) sortDir.value = nextSortDir

          for (const key of filterKeys) {
            const nextVal = readQueryString(`${prefix}${key}`) || defaultFilters[key] || ''
            if (filters[key] !== nextVal) filters[key] = nextVal
          }
        } finally {
          isSyncingFromUrl = false
        }
      },
      { deep: true }
    )
  }

  // Reset page to 1 when search or filters change interactively
  watch(
    q,
    () => {
      if (!isSyncingFromUrl) {
        page.value = 1
      }
      pushStateToUrl()
    },
    { flush: 'sync' }
  )

  watch(
    () => filterKeys.map((k) => filters[k]),
    () => {
      if (!isSyncingFromUrl) {
        page.value = 1
      }
      pushStateToUrl()
    },
    { flush: 'sync' }
  )

  watch(
    pageSize,
    () => {
      if (!isSyncingFromUrl) {
        page.value = 1
      }
      pushStateToUrl()
    },
    { flush: 'sync' }
  )

  watch(
    [page, sortBy, sortDir],
    () => {
      pushStateToUrl()
    },
    { flush: 'sync' }
  )

  // Client-mode pipeline
  const clientFiltered = computed<T[]>(() => {
    if (options.mode !== 'client') return []
    const source = options.items?.value ?? []
    const query = q.value.trim().toLowerCase()

    return source.filter((item) => {
      if (query !== '') {
        let matched = false
        if (options.searchFn) {
          matched = options.searchFn(item, query)
        } else if (options.searchFields && options.searchFields.length > 0) {
          matched = options.searchFields.some((field) => {
            const raw = typeof field === 'function' ? field(item) : (item as any)[field]
            if (raw === undefined || raw === null) return false
            return String(raw).toLowerCase().includes(query)
          })
        } else {
          matched = true
        }
        if (!matched) return false
      }

      if (options.filterFns) {
        for (const key of filterKeys) {
          const val = filters[key] ?? ''
          if (val !== '' && options.filterFns[key]) {
            if (!options.filterFns[key](item, val)) return false
          }
        }
      }

      return true
    })
  })

  const clientSorted = computed<T[]>(() => {
    if (options.mode !== 'client') return []
    const list = [...clientFiltered.value]
    if (!sortBy.value || !sortDir.value) return list

    const key = sortBy.value
    const dir = sortDir.value === 'asc' ? 1 : -1
    const customSort = options.sortFns?.[key]

    list.sort((a, b) => {
      if (customSort) {
        return customSort(a, b) * dir
      }
      const valA = (a as any)[key]
      const valB = (b as any)[key]
      if (valA === valB) return 0
      if (valA === undefined || valA === null) return 1
      if (valB === undefined || valB === null) return -1
      if (typeof valA === 'number' && typeof valB === 'number') {
        return (valA - valB) * dir
      }
      return String(valA).localeCompare(String(valB), 'vi') * dir
    })

    return list
  })

  const total = computed<number>(() => {
    if (options.mode === 'client') {
      return clientSorted.value.length
    }
    return serverTotal.value
  })

  const totalPages = computed<number>(() => {
    return Math.max(1, Math.ceil(total.value / pageSize.value))
  })

  // Clamp page when client data shrinks (only if totalPages < page and source has data or filters active)
  watch([totalPages, () => options.items?.value.length ?? 0], ([tp, srcLen]) => {
    if (options.mode === 'client' && (srcLen > 0 || hasActiveFilters.value) && page.value > tp) {
      page.value = tp
    }
  })

  const items = computed<T[]>(() => {
    if (options.mode === 'client') {
      const start = (page.value - 1) * pageSize.value
      return clientSorted.value.slice(start, start + pageSize.value)
    }
    if (!serverPaginated.value) {
      // Legacy server returned all items without X-Total-Count header
      const start = (page.value - 1) * pageSize.value
      return serverItems.value.slice(start, start + pageSize.value)
    }
    return serverItems.value
  })

  const startItem = computed<number>(() => {
    if (total.value === 0) return 0
    return (page.value - 1) * pageSize.value + 1
  })

  const endItem = computed<number>(() => {
    if (total.value === 0) return 0
    return Math.min(total.value, page.value * pageSize.value)
  })

  const hasActiveFilters = computed<boolean>(() => {
    if (q.value.trim() !== '') return true
    return filterKeys.some((key) => {
      const val = filters[key] ?? ''
      const def = defaultFilters[key] ?? ''
      return val !== '' && val !== def
    })
  })

  async function load(opts?: { silent?: boolean }): Promise<void> {
    if (options.mode !== 'server' || !options.fetchFn) return
    const seq = ++fetchSeq
    if (!opts?.silent) {
      serverLoading.value = true
    }
    serverError.value = null
    try {
      const result = await options.fetchFn({
        q: q.value.trim(),
        filters: { ...filters },
        sortBy: sortBy.value,
        sortDir: sortDir.value,
        page: page.value,
        pageSize: pageSize.value
      })
      if (seq !== fetchSeq) return
      serverItems.value = result.items
      serverTotal.value = result.total
      serverPaginated.value = result.serverPaginated !== false
    } catch (err: any) {
      if (seq !== fetchSeq) return
      serverError.value = err?.message || 'Không thể tải dữ liệu'
      throw err
    } finally {
      if (seq === fetchSeq && !opts?.silent) {
        serverLoading.value = false
      }
    }
  }

  if (options.mode === 'server') {
    watch(
      [q, page, pageSize, sortBy, sortDir, () => filterKeys.map((k) => filters[k])],
      () => {
        load().catch(() => {})
      },
      { immediate: options.immediate ?? false }
    )
  }

  function clearFilters() {
    q.value = ''
    for (const key of filterKeys) {
      filters[key] = defaultFilters[key] ?? ''
    }
    page.value = 1
  }

  function toggleSort(key: string) {
    if (sortBy.value !== key) {
      sortBy.value = key
      sortDir.value = 'asc'
    } else if (sortDir.value === 'asc') {
      sortDir.value = 'desc'
    } else {
      sortBy.value = null
      sortDir.value = null
    }
    page.value = 1
  }

  function setPage(nextPage: number) {
    const clamped = Math.max(1, Math.min(totalPages.value, Math.trunc(nextPage) || 1))
    page.value = clamped
  }

  function setPageSize(nextSize: number) {
    if (VALID_PAGE_SIZES.includes(nextSize)) {
      pageSize.value = nextSize
      page.value = 1
    }
  }

  return {
    q,
    filters,
    sortBy,
    sortDir,
    page,
    pageSize,
    items,
    total,
    totalPages,
    startItem,
    endItem,
    hasActiveFilters,
    loading: serverLoading,
    error: serverError,
    load,
    clearFilters,
    toggleSort,
    setPage,
    setPageSize
  }
}

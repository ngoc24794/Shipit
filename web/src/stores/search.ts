import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { ApiError } from '../api/client'
import { searchAll } from '../api/search'
import type { SearchGroup, SearchKind } from '../api/types'

export type SearchItemKind = SearchKind | 'shortcut' | 'action'

export interface PaletteItem {
  id: string
  title: string
  subtitle: string | null
  path: string
  kind: SearchItemKind
  keywords?: string
}

export interface PaletteGroup {
  key: string
  kind: SearchItemKind
  label: string
  items: PaletteItem[]
}

export interface RecentSearchItem {
  id: string
  title: string
  subtitle: string | null
  path: string
  kind: SearchItemKind
}

export const RECENT_STORAGE_KEY = 'shipit:search:recent'
export const MAX_RECENT_ITEMS = 6
export const SEARCH_DEBOUNCE_MS = 250

export const NAVIGATION_SHORTCUTS: PaletteItem[] = [
  { id: 'nav-runs', title: 'Lượt chạy', subtitle: 'Danh sách lượt chạy', path: '/', kind: 'run', keywords: 'runs luot chay' },
  { id: 'nav-pipelines', title: 'Pipeline', subtitle: 'Danh sách pipeline', path: '/pipelines', kind: 'pipeline', keywords: 'pipelines' },
  { id: 'nav-jobs', title: 'Job', subtitle: 'Danh sách job', path: '/jobs', kind: 'job', keywords: 'jobs' },
  { id: 'nav-slaves', title: 'Máy build', subtitle: 'Danh sách máy build', path: '/slaves', kind: 'slave', keywords: 'slaves may build' },
  { id: 'nav-credentials', title: 'Thông tin xác thực', subtitle: 'Quản lý thông tin xác thực', path: '/credentials', kind: 'credential', keywords: 'credentials thong tin xac thuc' },
  { id: 'nav-allowlist', title: 'Allowlist', subtitle: 'Allowlist deployment và job', path: '/settings/allowlist', kind: 'deploy-target', keywords: 'allowlist deploy job' }
]

export const ACTION_SHORTCUTS: PaletteItem[] = [
  { id: 'act-pipeline-new', title: 'Thêm pipeline', subtitle: 'Tạo pipeline mới', path: '/pipelines/new', kind: 'action', keywords: 'them pipeline tao moi new' },
  { id: 'act-job-new', title: 'Thêm Job', subtitle: 'Tạo job mới', path: '/jobs/new', kind: 'action', keywords: 'them job tao moi new' },
  { id: 'act-credential-new', title: 'Thêm thông tin xác thực', subtitle: 'Tạo credential mới', path: '/credentials/new', kind: 'action', keywords: 'them thong tin xac thuc credential new' },
  { id: 'act-slave-new', title: 'Thêm máy build', subtitle: 'Tạo máy build mới', path: '/slaves/new', kind: 'action', keywords: 'them may build slave new' }
]

function normalizeText(value: string): string {
  return value
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .toLowerCase()
}

function loadRecentFromStorage(): RecentSearchItem[] {
  try {
    if (typeof localStorage === 'undefined') return []
    const raw = localStorage.getItem(RECENT_STORAGE_KEY)
    if (!raw) return []
    const parsed = JSON.parse(raw)
    if (!Array.isArray(parsed)) return []
    return parsed
      .filter(
        (item): item is RecentSearchItem =>
          Boolean(item) &&
          typeof item.id === 'string' &&
          typeof item.title === 'string' &&
          typeof item.path === 'string'
      )
      .slice(0, MAX_RECENT_ITEMS)
  } catch {
    return []
  }
}

function saveRecentToStorage(items: RecentSearchItem[]): void {
  try {
    if (typeof localStorage === 'undefined') return
    localStorage.setItem(RECENT_STORAGE_KEY, JSON.stringify(items.slice(0, MAX_RECENT_ITEMS)))
  } catch {
    // Ignore storage quota or security errors
  }
}

function isAbortError(err: unknown): boolean {
  if (!err || typeof err !== 'object') return false
  const name = (err as { name?: string }).name
  return name === 'AbortError'
}

export const useSearchStore = defineStore('search', () => {
  const isOpen = ref(false)
  const query = ref('')
  const apiGroups = ref<SearchGroup[]>([])
  const loading = ref(false)
  const error = ref<string | null>(null)
  const unauthorized = ref(false)
  const recent = ref<RecentSearchItem[]>(loadRecentFromStorage())

  let debounceTimer: ReturnType<typeof setTimeout> | null = null
  let activeController: AbortController | null = null
  let requestSeq = 0

  function cancelPending(): void {
    if (debounceTimer !== null) {
      clearTimeout(debounceTimer)
      debounceTimer = null
    }
    if (activeController) {
      activeController.abort()
      activeController = null
    }
  }

  function open(): void {
    recent.value = loadRecentFromStorage()
    isOpen.value = true
  }

  function close(): void {
    isOpen.value = false
    cancelPending()
    loading.value = false
  }

  function toggle(): void {
    if (isOpen.value) {
      close()
    } else {
      open()
    }
  }

  function reset(): void {
    cancelPending()
    query.value = ''
    apiGroups.value = []
    loading.value = false
    error.value = null
    unauthorized.value = false
  }

  const filteredShortcuts = computed<PaletteItem[]>(() => {
    const trimmed = query.value.trim()
    const all = [...NAVIGATION_SHORTCUTS, ...ACTION_SHORTCUTS]
    if (!trimmed) return all

    const needle = trimmed.toLowerCase()
    const normalizedNeedle = normalizeText(trimmed)
    return all.filter((item) => {
      const haystack = `${item.title} ${item.subtitle ?? ''} ${item.keywords ?? ''}`
      return (
        haystack.toLowerCase().includes(needle) ||
        normalizeText(haystack).includes(normalizedNeedle)
      )
    })
  })

  const displayGroups = computed<PaletteGroup[]>(() => {
    const trimmed = query.value.trim()
    if (!trimmed) {
      const groups: PaletteGroup[] = []
      if (recent.value.length > 0) {
        groups.push({
          key: 'recent',
          kind: 'shortcut',
          label: 'Gần đây',
          items: recent.value.map((r) => ({
            id: `recent-${r.path}`,
            title: r.title,
            subtitle: r.subtitle,
            path: r.path,
            kind: r.kind
          }))
        })
      }
      groups.push({
        key: 'shortcuts-nav',
        kind: 'shortcut',
        label: 'Lối tắt',
        items: NAVIGATION_SHORTCUTS
      })
      groups.push({
        key: 'shortcuts-actions',
        kind: 'action',
        label: 'Hành động nhanh',
        items: ACTION_SHORTCUTS
      })
      return groups
    }

    const groups: PaletteGroup[] = apiGroups.value.map((g) => ({
      key: `api-${g.kind}`,
      kind: g.kind,
      label: g.label,
      items: g.items.map((item) => ({
        id: `${g.kind}-${item.id}`,
        title: item.title,
        subtitle: item.subtitle,
        path: item.path,
        kind: g.kind
      }))
    }))

    if (filteredShortcuts.value.length > 0) {
      groups.push({
        key: 'shortcuts-filtered',
        kind: 'shortcut',
        label: 'Lối tắt',
        items: filteredShortcuts.value
      })
    }

    return groups
  })

  async function executeSearch(trimmedQuery: string, seq: number): Promise<void> {
    if (activeController) {
      activeController.abort()
    }
    const controller = new AbortController()
    activeController = controller

    try {
      const res = await searchAll(trimmedQuery, 5, controller.signal)
      if (seq !== requestSeq) return
      apiGroups.value = res.groups || []
      error.value = null
      unauthorized.value = false
    } catch (err: unknown) {
      if (isAbortError(err) || seq !== requestSeq) {
        return
      }
      apiGroups.value = []
      if (err instanceof ApiError && err.status === 401) {
        unauthorized.value = true
        error.value = 'Phiên đăng nhập đã hết hạn'
      } else {
        error.value = err instanceof Error ? err.message : 'Không thể tìm kiếm'
      }
    } finally {
      if (seq === requestSeq) {
        loading.value = false
        if (activeController === controller) {
          activeController = null
        }
      }
    }
  }

  function setQuery(nextQuery: string): void {
    query.value = nextQuery
    const trimmed = nextQuery.trim()
    requestSeq += 1
    const seq = requestSeq

    cancelPending()
    error.value = null
    unauthorized.value = false

    if (!trimmed) {
      apiGroups.value = []
      loading.value = false
      return
    }

    loading.value = true
    debounceTimer = setTimeout(() => {
      debounceTimer = null
      void executeSearch(trimmed, seq)
    }, SEARCH_DEBOUNCE_MS)
  }

  function addRecent(item: PaletteItem): void {
    const entry: RecentSearchItem = {
      id: item.id,
      title: item.title,
      subtitle: item.subtitle,
      path: item.path,
      kind: item.kind
    }
    const next = [entry, ...recent.value.filter((r) => r.path !== entry.path)].slice(0, MAX_RECENT_ITEMS)
    recent.value = next
    saveRecentToStorage(next)
  }

  return {
    isOpen,
    query,
    apiGroups,
    loading,
    error,
    unauthorized,
    recent,
    filteredShortcuts,
    displayGroups,
    open,
    close,
    toggle,
    reset,
    setQuery,
    addRecent
  }
})

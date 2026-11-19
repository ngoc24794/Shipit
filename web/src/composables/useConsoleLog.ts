import { computed, ref, type ComputedRef, type Ref } from 'vue'

export type ConsoleLogLevel = 'info' | 'success' | 'warn' | 'error'

export interface ConsoleLogEntry {
  id: number
  time: string
  level: ConsoleLogLevel
  message: string
}

export interface ConsoleLogApi {
  entries: Ref<ConsoleLogEntry[]>
  lastEntry: ComputedRef<ConsoleLogEntry | null>
  drawerOpen: Ref<boolean>
  log: (message: string, level?: ConsoleLogLevel) => void
  clear: () => void
  openDrawer: () => void
  closeDrawer: () => void
  toggleDrawer: () => void
}

const MAX_CONSOLE_ENTRIES = 100

const entries = ref<ConsoleLogEntry[]>([])
const drawerOpen = ref(false)
let nextId = 1

function formatNow(): string {
  const now = new Date()
  const hh = String(now.getHours()).padStart(2, '0')
  const mm = String(now.getMinutes()).padStart(2, '0')
  const ss = String(now.getSeconds()).padStart(2, '0')
  return `${hh}:${mm}:${ss}`
}

export function useConsoleLog(): ConsoleLogApi {
  const lastEntry = computed<ConsoleLogEntry | null>(() => {
    if (entries.value.length === 0) return null
    return entries.value[entries.value.length - 1]
  })

  function log(message: string, level: ConsoleLogLevel = 'info'): void {
    const entry: ConsoleLogEntry = {
      id: nextId++,
      time: formatNow(),
      level,
      message
    }
    entries.value.push(entry)
    if (entries.value.length > MAX_CONSOLE_ENTRIES) {
      entries.value.splice(0, entries.value.length - MAX_CONSOLE_ENTRIES)
    }
  }

  function clear(): void {
    entries.value = []
  }

  function openDrawer(): void {
    drawerOpen.value = true
  }

  function closeDrawer(): void {
    drawerOpen.value = false
  }

  function toggleDrawer(): void {
    drawerOpen.value = !drawerOpen.value
  }

  return {
    entries,
    lastEntry,
    drawerOpen,
    log,
    clear,
    openDrawer,
    closeDrawer,
    toggleDrawer
  }
}

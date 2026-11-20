import { computed, type ComputedRef, type Ref } from 'vue'
import { useToast, type ConsoleEntry, type ToastType } from './useToast'

export type ConsoleLogLevel = 'info' | 'success' | 'warn' | 'error'

export interface ConsoleLogEntry {
  id: number
  time: string
  level: ConsoleLogLevel
  message: string
}

export interface ConsoleLogApi {
  entries: ComputedRef<ConsoleLogEntry[]>
  lastEntry: ComputedRef<ConsoleLogEntry | null>
  drawerOpen: Ref<boolean>
  log: (message: string, level?: ConsoleLogLevel) => void
  clear: () => void
  openDrawer: () => void
  closeDrawer: () => void
  toggleDrawer: () => void
}

function toConsoleLevel(type: ToastType): ConsoleLogLevel {
  if (type === 'warning') return 'warn'
  return type
}

function mapEntry(entry: ConsoleEntry): ConsoleLogEntry {
  return {
    id: entry.id,
    time: entry.timestamp,
    level: toConsoleLevel(entry.type),
    message: entry.message
  }
}

export function useConsoleLog(): ConsoleLogApi {
  const toast = useToast()

  const entries = computed<ConsoleLogEntry[]>(() => toast.entries.value.map(mapEntry))

  const lastEntry = computed<ConsoleLogEntry | null>(() => {
    const latest = toast.latestEntry.value
    return latest ? mapEntry(latest) : null
  })

  function log(message: string, level: ConsoleLogLevel = 'info'): void {
    toast.logHistory(message, level)
  }

  return {
    entries,
    lastEntry,
    drawerOpen: toast.drawerOpen,
    log,
    clear: toast.clear,
    openDrawer: toast.openDrawer,
    closeDrawer: toast.closeDrawer,
    toggleDrawer: toast.toggleDrawer
  }
}

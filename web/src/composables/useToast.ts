import { ref, computed } from 'vue'

export type ToastType = 'success' | 'error' | 'warn' | 'warning' | 'info'

export interface ToastAction {
  label: string
  onClick: () => void
}

export interface ToastOptions {
  action?: ToastAction
  duration?: number
}

export interface ToastItem {
  id: number
  message: string
  type: ToastType
  duration: number
  createdAt: string
  action?: ToastAction
}

export interface ConsoleEntry {
  id: number
  message: string
  type: ToastType
  timestamp: string
}

const MAX_HISTORY = 100

const toasts = ref<ToastItem[]>([])
const entries = ref<ConsoleEntry[]>([])
const unreadCount = ref(0)
const drawerOpen = ref(false)
let nextId = 1

interface TimerRecord {
  timerId: ReturnType<typeof setTimeout> | null
  remaining: number
  startedAt: number
}

const timers = new Map<number, TimerRecord>()

function formatTime(date: Date): string {
  return date.toLocaleTimeString('vi-VN', {
    hour12: false,
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit'
  })
}

export function useToast() {
  function clearTimer(id: number) {
    const rec = timers.get(id)
    if (rec?.timerId) {
      clearTimeout(rec.timerId)
      rec.timerId = null
    }
  }

  function remove(id: number) {
    clearTimer(id)
    timers.delete(id)
    const idx = toasts.value.findIndex((t) => t.id === id)
    if (idx !== -1) {
      toasts.value.splice(idx, 1)
    }
  }

  function scheduleRemoval(id: number, delay: number) {
    if (delay <= 0) return
    clearTimer(id)
    const timerId = setTimeout(() => {
      remove(id)
    }, delay)
    timers.set(id, {
      timerId,
      remaining: delay,
      startedAt: Date.now()
    })
  }

  function pause(id: number) {
    const rec = timers.get(id)
    if (!rec || !rec.timerId) return
    clearTimeout(rec.timerId)
    rec.timerId = null
    const elapsed = Date.now() - rec.startedAt
    rec.remaining = Math.max(200, rec.remaining - elapsed)
  }

  function resume(id: number) {
    const rec = timers.get(id)
    if (!rec || rec.timerId) return
    scheduleRemoval(id, rec.remaining)
  }

  function add(
    message: string,
    type: ToastType = 'info',
    durationOrOptions: number | ToastOptions = 4000,
    maybeOptions?: ToastOptions
  ) {
    const duration =
      typeof durationOrOptions === 'number'
        ? durationOrOptions
        : durationOrOptions.duration ?? 4000
    const action =
      typeof durationOrOptions === 'object' ? durationOrOptions.action : maybeOptions?.action

    const id = nextId++
    const timestamp = formatTime(new Date())
    const item: ToastItem = {
      id,
      message,
      type,
      duration,
      createdAt: timestamp,
      action
    }
    toasts.value.push(item)

    entries.value.unshift({
      id,
      message,
      type,
      timestamp
    })
    if (entries.value.length > MAX_HISTORY) {
      entries.value.length = MAX_HISTORY
    }
    if (!drawerOpen.value) {
      unreadCount.value++
    }

    if (duration > 0) {
      scheduleRemoval(id, duration)
    }
    return id
  }

  function success(message: string, durationOrOptions?: number | ToastOptions, options?: ToastOptions) {
    return add(message, 'success', durationOrOptions, options)
  }

  function error(message: string, durationOrOptions?: number | ToastOptions, options?: ToastOptions) {
    return add(message, 'error', durationOrOptions, options)
  }

  function warn(message: string, durationOrOptions?: number | ToastOptions, options?: ToastOptions) {
    return add(message, 'warn', durationOrOptions, options)
  }

  function info(message: string, durationOrOptions?: number | ToastOptions, options?: ToastOptions) {
    return add(message, 'info', durationOrOptions, options)
  }

  function logHistory(message: string, type: ToastType = 'info') {
    const id = nextId++
    const timestamp = formatTime(new Date())
    entries.value.unshift({
      id,
      message,
      type,
      timestamp
    })
    if (entries.value.length > MAX_HISTORY) {
      entries.value.length = MAX_HISTORY
    }
    return id
  }

  function clear() {
    entries.value = []
    unreadCount.value = 0
  }

  function markAllRead() {
    unreadCount.value = 0
  }

  function openDrawer() {
    drawerOpen.value = true
    unreadCount.value = 0
  }

  function closeDrawer() {
    drawerOpen.value = false
  }

  function toggleDrawer() {
    if (drawerOpen.value) {
      closeDrawer()
    } else {
      openDrawer()
    }
  }

  const latestEntry = computed(() => entries.value[0] ?? null)

  return {
    toasts,
    entries,
    unreadCount,
    latestEntry,
    drawerOpen,
    add,
    remove,
    pause,
    resume,
    success,
    error,
    warn,
    info,
    logHistory,
    clear,
    markAllRead,
    openDrawer,
    closeDrawer,
    toggleDrawer
  }
}

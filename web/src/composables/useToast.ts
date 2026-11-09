import { ref } from 'vue'

export type ToastType = 'success' | 'error' | 'info'

export interface ToastItem {
  id: number
  message: string
  type: ToastType
  duration?: number
}

const toasts = ref<ToastItem[]>([])
let nextId = 1

export function useToast() {
  function remove(id: number) {
    const idx = toasts.value.findIndex((t) => t.id === id)
    if (idx !== -1) {
      toasts.value.splice(idx, 1)
    }
  }

  function add(message: string, type: ToastType = 'info', duration: number = 4000) {
    const id = nextId++
    const item: ToastItem = { id, message, type, duration }
    toasts.value.push(item)

    if (duration > 0) {
      setTimeout(() => {
        remove(id)
      }, duration)
    }
    return id
  }

  function success(message: string, duration?: number) {
    return add(message, 'success', duration)
  }

  function error(message: string, duration?: number) {
    return add(message, 'error', duration)
  }

  function info(message: string, duration?: number) {
    return add(message, 'info', duration)
  }

  return {
    toasts,
    add,
    remove,
    success,
    error,
    info
  }
}

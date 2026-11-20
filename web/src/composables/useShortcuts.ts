import { computed, onActivated, onDeactivated, onUnmounted, ref } from 'vue'

export interface ShortcutBinding {
  id: string
  keys: string
  label: string
  scope: string
  handler: (e: KeyboardEvent) => void
  allowInInput?: boolean
  when?: () => boolean
}

export interface ShortcutConflict {
  keys: string
  existingId: string
  incomingId: string
  scope: string
}

export interface ShortcutGroup {
  scope: string
  items: ShortcutBinding[]
}

const bindings = ref<ShortcutBinding[]>([])
const conflicts = ref<ShortcutConflict[]>([])
const isOverlayOpen = ref(false)
const pendingPrefix = ref<string | null>(null)
let prefixTimer: ReturnType<typeof setTimeout> | null = null
let listenerAttached = false

const CHORD_TIMEOUT_MS = 900

function normalizeKeys(raw: string): string {
  return raw
    .trim()
    .toLowerCase()
    .replace(/\s+/g, ' ')
    .replace(/\besc\b/g, 'escape')
}

export function isTypingElement(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) return false
  if (target.isContentEditable) return true
  const tag = target.tagName.toLowerCase()
  if (tag === 'input' || tag === 'textarea' || tag === 'select') return true
  return target.closest('[role="combobox"], [role="textbox"]') !== null
}

function clearPendingPrefix(): void {
  pendingPrefix.value = null
  if (prefixTimer) {
    clearTimeout(prefixTimer)
    prefixTimer = null
  }
}

function eventToSingleKey(e: KeyboardEvent): string {
  const parts: string[] = []
  if (e.ctrlKey) parts.push('ctrl')
  if (e.metaKey) parts.push('meta')
  if (e.altKey) parts.push('alt')
  const key = e.key === ' ' ? 'space' : e.key.toLowerCase()
  parts.push(key)
  return parts.join('+')
}

function handleGlobalKeydown(e: KeyboardEvent): void {
  const typing = isTypingElement(e.target)
  const singleKey = eventToSingleKey(e)

  if (e.key === 'Escape') {
    clearPendingPrefix()
    if (isOverlayOpen.value) {
      e.preventDefault()
      isOverlayOpen.value = false
      return
    }
  }

  // Check if we have a pending chord prefix (e.g. 'g')
  if (pendingPrefix.value && !typing && !e.ctrlKey && !e.metaKey && !e.altKey) {
    const chord = `${pendingPrefix.value} ${singleKey}`
    clearPendingPrefix()
    const match = findActiveBinding(chord, typing)
    if (match) {
      e.preventDefault()
      match.handler(e)
      return
    }
  }

  // Start a chord if any active binding starts with "<singleKey> "
  if (!typing && !e.ctrlKey && !e.metaKey && !e.altKey) {
    const hasChordPrefix = bindings.value.some((b) => {
      if (b.when && !b.when()) return false
      return normalizeKeys(b.keys).startsWith(`${singleKey} `)
    })
    if (hasChordPrefix) {
      e.preventDefault()
      pendingPrefix.value = singleKey
      if (prefixTimer) clearTimeout(prefixTimer)
      prefixTimer = setTimeout(() => {
        pendingPrefix.value = null
      }, CHORD_TIMEOUT_MS)
      return
    }
  }

  const match = findActiveBinding(singleKey, typing)
  if (match) {
    e.preventDefault()
    match.handler(e)
  }
}

function findActiveBinding(normalizedKey: string, typing: boolean): ShortcutBinding | undefined {
  // Search in reverse registration order so view-scoped bindings take precedence over global ones
  for (let i = bindings.value.length - 1; i >= 0; i -= 1) {
    const b = bindings.value[i]
    if (normalizeKeys(b.keys) !== normalizedKey) continue
    if (typing && !b.allowInInput) continue
    if (b.when && !b.when()) continue
    return b
  }
  return undefined
}

function ensureListener(): void {
  if (listenerAttached || typeof window === 'undefined') return
  window.addEventListener('keydown', handleGlobalKeydown)
  listenerAttached = true
}

export function registerShortcut(binding: ShortcutBinding): () => void {
  ensureListener()
  const norm = normalizeKeys(binding.keys)
  const existing = bindings.value.find(
    (b) => normalizeKeys(b.keys) === norm && (b.scope === binding.scope || b.scope === 'Toàn cục' || binding.scope === 'Toàn cục')
  )
  if (existing && existing.id !== binding.id) {
    conflicts.value.push({
      keys: binding.keys,
      existingId: existing.id,
      incomingId: binding.id,
      scope: binding.scope
    })
  }
  bindings.value = [...bindings.value.filter((b) => b.id !== binding.id), binding]

  return () => {
    bindings.value = bindings.value.filter((b) => b.id !== binding.id)
  }
}

export function useShortcuts() {
  ensureListener()
  const localBindings: ShortcutBinding[] = []
  const localCleanups: Array<() => void> = []

  function clearLocalCleanups(): void {
    for (const cleanup of localCleanups) {
      cleanup()
    }
    localCleanups.length = 0
  }

  function register(binding: ShortcutBinding): () => void {
    if (!localBindings.some((b) => b.id === binding.id)) {
      localBindings.push(binding)
    }
    const unregister = registerShortcut(binding)
    localCleanups.push(unregister)
    return () => {
      const idx = localBindings.findIndex((b) => b.id === binding.id)
      if (idx !== -1) localBindings.splice(idx, 1)
      unregister()
    }
  }

  function registerMany(list: ShortcutBinding[]): void {
    for (const item of list) {
      register(item)
    }
  }

  onDeactivated(() => {
    clearLocalCleanups()
  })

  onActivated(() => {
    if (localCleanups.length === 0 && localBindings.length > 0) {
      for (const binding of localBindings) {
        localCleanups.push(registerShortcut(binding))
      }
    }
  })

  onUnmounted(() => {
    clearLocalCleanups()
    localBindings.length = 0
  })

  const activeShortcuts = computed<ShortcutBinding[]>(() =>
    bindings.value.filter((b) => !b.when || b.when())
  )

  const groupedShortcuts = computed<ShortcutGroup[]>(() => {
    const map = new Map<string, ShortcutBinding[]>()
    for (const item of activeShortcuts.value) {
      const list = map.get(item.scope) || []
      list.push(item)
      map.set(item.scope, list)
    }
    return Array.from(map.entries()).map(([scope, items]) => ({ scope, items }))
  })

  function openOverlay(): void {
    isOverlayOpen.value = true
  }

  function closeOverlay(): void {
    isOverlayOpen.value = false
  }

  function toggleOverlay(): void {
    isOverlayOpen.value = !isOverlayOpen.value
  }

  return {
    bindings,
    conflicts,
    activeShortcuts,
    groupedShortcuts,
    isOverlayOpen,
    pendingPrefix,
    register,
    registerMany,
    openOverlay,
    closeOverlay,
    toggleOverlay
  }
}

export function resetShortcutsForTest(): void {
  bindings.value = []
  conflicts.value = []
  isOverlayOpen.value = false
  clearPendingPrefix()
}

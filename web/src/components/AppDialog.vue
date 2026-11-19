<script lang="ts">
// Module-level scroll lock counter so nested dialogs/drawers do not unlock prematurely
let scrollLockCount = 0
let savedOverflow = ''
let savedPaddingRight = ''

export function acquireScrollLock() {
  if (typeof document === 'undefined') return
  if (scrollLockCount === 0) {
    savedOverflow = document.body.style.overflow
    savedPaddingRight = document.body.style.paddingRight
    const scrollbarWidth = window.innerWidth - document.documentElement.clientWidth
    document.body.style.overflow = 'hidden'
    if (scrollbarWidth > 0) {
      document.body.style.paddingRight = `${scrollbarWidth}px`
    }
  }
  scrollLockCount++
}

export function releaseScrollLock() {
  if (typeof document === 'undefined') return
  if (scrollLockCount > 0) {
    scrollLockCount--
    if (scrollLockCount === 0) {
      document.body.style.overflow = savedOverflow
      document.body.style.paddingRight = savedPaddingRight
    }
  }
}
</script>

<script setup lang="ts">
import { ref, computed, watch, nextTick, onBeforeUnmount } from 'vue'
import { X } from 'lucide-vue-next'

export type DialogSize = 'sm' | 'md' | 'lg' | 'xl' | 'full'

const props = withDefaults(
  defineProps<{
    modelValue: boolean
    title?: string
    maxWidth?: DialogSize
    size?: DialogSize
    closeOnOverlay?: boolean
    closeOnEsc?: boolean
  }>(),
  {
    title: undefined,
    maxWidth: 'md',
    size: undefined,
    closeOnOverlay: true,
    closeOnEsc: true
  }
)

const emit = defineEmits<{
  (e: 'update:modelValue', value: boolean): void
  (e: 'close'): void
}>()

const dialogRef = ref<HTMLElement | null>(null)
let previousActiveElement: HTMLElement | null = null
let lockedByThis = false

const effectiveSize = computed<DialogSize>(() => props.size ?? props.maxWidth)

const sizeClass = computed(() => {
  switch (effectiveSize.value) {
    case 'sm':
      return 'max-w-[400px]'
    case 'md':
      return 'max-w-[560px]'
    case 'lg':
      return 'max-w-[740px]'
    case 'xl':
      return 'max-w-[960px]'
    case 'full':
      return 'max-w-[calc(100vw-32px)] h-[calc(100vh-32px)]'
    default:
      return 'max-w-[560px]'
  }
})

function close() {
  emit('update:modelValue', false)
  emit('close')
}

function handleOverlayClick() {
  if (props.closeOnOverlay) {
    close()
  }
}

function getFocusableElements(): HTMLElement[] {
  if (!dialogRef.value) return []
  return Array.from(
    dialogRef.value.querySelectorAll<HTMLElement>(
      'a[href], button:not([disabled]), textarea:not([disabled]), input:not([disabled]), select:not([disabled]), [tabindex]:not([tabindex="-1"])'
    )
  ).filter((el) => el.offsetParent !== null || el.getClientRects().length > 0 || el === document.activeElement)
}

function handleKeyDown(event: KeyboardEvent) {
  if (event.key === 'Escape' && props.closeOnEsc) {
    event.stopPropagation()
    close()
    return
  }

  if (event.key === 'Tab' && dialogRef.value) {
    const focusableElements = getFocusableElements()
    if (focusableElements.length === 0) {
      event.preventDefault()
      dialogRef.value.focus()
      return
    }

    const firstElement = focusableElements[0]
    const lastElement = focusableElements[focusableElements.length - 1]

    if (event.shiftKey) {
      if (document.activeElement === firstElement || document.activeElement === dialogRef.value) {
        lastElement.focus()
        event.preventDefault()
      }
    } else {
      if (document.activeElement === lastElement) {
        firstElement.focus()
        event.preventDefault()
      }
    }
  }
}

function cleanupOpenState() {
  document.removeEventListener('keydown', handleKeyDown)
  if (lockedByThis) {
    releaseScrollLock()
    lockedByThis = false
  }
  if (previousActiveElement && typeof previousActiveElement.focus === 'function') {
    previousActiveElement.focus()
    previousActiveElement = null
  }
}

watch(
  () => props.modelValue,
  async (open) => {
    if (open) {
      previousActiveElement = document.activeElement as HTMLElement | null
      if (!lockedByThis) {
        acquireScrollLock()
        lockedByThis = true
      }
      document.addEventListener('keydown', handleKeyDown)
      await nextTick()
      if (dialogRef.value) {
        const focusable = dialogRef.value.querySelector<HTMLElement>(
          'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'
        )
        if (focusable) {
          focusable.focus()
        } else {
          dialogRef.value.focus()
        }
      }
    } else {
      cleanupOpenState()
    }
  },
  { immediate: true }
)

onBeforeUnmount(() => {
  cleanupOpenState()
})
</script>

<template>
  <Teleport to="body">
    <Transition name="d1-modal">
      <div
        v-if="modelValue"
        class="fixed inset-0 z-50 flex items-center justify-center p-4 bg-[var(--backdrop)]"
        @click.self="handleOverlayClick"
      >
        <div
          ref="dialogRef"
          role="dialog"
          aria-modal="true"
          :aria-label="title"
          tabindex="-1"
          :class="[
            'relative w-full max-h-[85vh] flex flex-col rounded-[2px] bg-bg-elevated border border-border-strong shadow-[var(--shadow-overlay)] overflow-hidden font-mono text-fg outline-none',
            sizeClass
          ]"
        >
          <!-- Header -->
          <div class="shrink-0 flex items-center justify-between gap-2 px-3 py-2 bg-bg-surface border-b border-border">
            <h2 class="text-[13px] font-bold text-fg-strong truncate">
              <slot name="header">
                <slot name="title">{{ title }}</slot>
              </slot>
            </h2>
            <button
              type="button"
              aria-label="Đóng"
              class="inline-flex items-center justify-center w-6 h-6 rounded-[2px] text-fg-muted hover:text-fg-strong hover:bg-bg-hover transition-colors duration-[80ms] cursor-pointer focus-visible:outline-2 focus-visible:outline-border-focus"
              @click="close"
            >
              <X class="w-4 h-4" aria-hidden="true" />
            </button>
          </div>

          <!-- Body -->
          <div class="flex-1 min-h-0 px-3 py-3 overflow-y-auto flex flex-col gap-3">
            <slot />
          </div>

          <!-- Footer -->
          <div
            v-if="$slots.footer"
            class="shrink-0 flex items-center justify-end gap-2 px-3 py-2 bg-bg-surface border-t border-border"
          >
            <slot name="footer" />
          </div>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>

<style scoped>
.d1-modal-enter-active,
.d1-modal-leave-active {
  transition: opacity var(--dur-fast, 110ms) var(--ease-snap, cubic-bezier(0.2, 0, 0, 1));
}
.d1-modal-enter-active > div,
.d1-modal-leave-active > div {
  transition: transform var(--dur-fast, 110ms) var(--ease-snap, cubic-bezier(0.2, 0, 0, 1)),
    opacity var(--dur-fast, 110ms) var(--ease-snap, cubic-bezier(0.2, 0, 0, 1));
}
.d1-modal-enter-from,
.d1-modal-leave-to {
  opacity: 0;
}
.d1-modal-enter-from > div,
.d1-modal-leave-to > div {
  opacity: 0;
  transform: scale(0.98);
}
</style>

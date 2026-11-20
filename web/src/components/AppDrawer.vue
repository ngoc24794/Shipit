<script setup lang="ts">
import { ref, computed, watch, nextTick, onBeforeUnmount } from 'vue'
import { X } from 'lucide-vue-next'
import { acquireScrollLock, releaseScrollLock } from './AppDialog.vue'

export type DrawerSide = 'left' | 'right'
export type DrawerSize = 'sm' | 'md' | 'lg'

const props = withDefaults(
  defineProps<{
    modelValue: boolean
    title?: string
    side?: DrawerSide
    size?: DrawerSize
    closeOnOverlay?: boolean
    closeOnEsc?: boolean
  }>(),
  {
    title: undefined,
    side: 'right',
    size: 'md',
    closeOnOverlay: true,
    closeOnEsc: true
  }
)

const emit = defineEmits<{
  (e: 'update:modelValue', value: boolean): void
  (e: 'close'): void
}>()

const drawerRef = ref<HTMLElement | null>(null)
let previousActiveElement: HTMLElement | null = null
let lockedByThis = false

const widthClass = computed(() => {
  switch (props.size) {
    case 'sm':
      return 'w-[360px] max-w-[92vw]'
    case 'lg':
      return 'w-[640px] max-w-[92vw]'
    case 'md':
    default:
      return 'w-[480px] max-w-[92vw]'
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
  if (!drawerRef.value) return []
  return Array.from(
    drawerRef.value.querySelectorAll<HTMLElement>(
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

  if (event.key === 'Tab' && drawerRef.value) {
    const focusableElements = getFocusableElements()
    if (focusableElements.length === 0) {
      event.preventDefault()
      drawerRef.value.focus()
      return
    }

    const firstElement = focusableElements[0]
    const lastElement = focusableElements[focusableElements.length - 1]

    if (event.shiftKey) {
      if (document.activeElement === firstElement || document.activeElement === drawerRef.value) {
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
      if (drawerRef.value) {
        const focusable = drawerRef.value.querySelector<HTMLElement>(
          'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'
        )
        if (focusable) {
          focusable.focus()
        } else {
          drawerRef.value.focus()
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
    <Transition :name="side === 'left' ? 'd1-drawer-left' : 'd1-drawer-right'">
      <div
        v-if="modelValue"
        class="fixed inset-0 z-50 flex bg-[var(--backdrop)]"
        :class="side === 'left' ? 'justify-start' : 'justify-end'"
        @click.self="handleOverlayClick"
      >
        <div
          ref="drawerRef"
          role="dialog"
          aria-modal="true"
          :aria-label="title"
          tabindex="-1"
          :class="[
            'h-full flex flex-col bg-bg-elevated border-border-strong shadow-[var(--shadow-overlay)] font-mono text-fg outline-none',
            side === 'left' ? 'border-r' : 'border-l',
            widthClass
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
.d1-drawer-right-enter-active,
.d1-drawer-right-leave-active,
.d1-drawer-left-enter-active,
.d1-drawer-left-leave-active {
  transition: opacity var(--dur-fast, 110ms) var(--ease-snap, cubic-bezier(0.2, 0, 0, 1));
}
.d1-drawer-right-enter-active > div,
.d1-drawer-right-leave-active > div,
.d1-drawer-left-enter-active > div,
.d1-drawer-left-leave-active > div {
  transition: transform var(--dur-fast, 110ms) var(--ease-snap, cubic-bezier(0.2, 0, 0, 1));
}
.d1-drawer-right-enter-from,
.d1-drawer-right-leave-to,
.d1-drawer-left-enter-from,
.d1-drawer-left-leave-to {
  opacity: 0;
}
.d1-drawer-right-enter-from > div,
.d1-drawer-right-leave-to > div {
  transform: translateX(100%);
}
.d1-drawer-left-enter-from > div,
.d1-drawer-left-leave-to > div {
  transform: translateX(-100%);
}
</style>

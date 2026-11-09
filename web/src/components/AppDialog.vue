<script setup lang="ts">
import { ref, watch, nextTick, onBeforeUnmount } from 'vue'
import { X } from 'lucide-vue-next'

const props = withDefaults(
  defineProps<{
    modelValue: boolean
    title?: string
    maxWidth?: 'sm' | 'md' | 'lg' | 'xl'
  }>(),
  {
    title: undefined,
    maxWidth: 'md'
  }
)

const emit = defineEmits<{
  (e: 'update:modelValue', value: boolean): void
  (e: 'close'): void
}>()

const dialogRef = ref<HTMLElement | null>(null)
let previousActiveElement: HTMLElement | null = null

function close() {
  emit('update:modelValue', false)
  emit('close')
}

function handleKeyDown(event: KeyboardEvent) {
  if (event.key === 'Escape') {
    close()
    return
  }

  // Focus trap
  if (event.key === 'Tab' && dialogRef.value) {
    const focusableElements = dialogRef.value.querySelectorAll<HTMLElement>(
      'a[href], button:not([disabled]), textarea:not([disabled]), input:not([disabled]), select:not([disabled]), [tabindex]:not([tabindex="-1"])'
    )
    if (focusableElements.length === 0) return

    const firstElement = focusableElements[0]
    const lastElement = focusableElements[focusableElements.length - 1]

    if (event.shiftKey) {
      if (document.activeElement === firstElement) {
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

watch(
  () => props.modelValue,
  async (open) => {
    if (open) {
      previousActiveElement = document.activeElement as HTMLElement
      document.addEventListener('keydown', handleKeyDown)
      await nextTick()
      if (dialogRef.value) {
        // Focus first focusable element or dialog
        const focusable = dialogRef.value.querySelector<HTMLElement>(
          'button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])'
        )
        if (focusable) {
          focusable.focus()
        } else {
          dialogRef.value.focus()
        }
      }
    } else {
      document.removeEventListener('keydown', handleKeyDown)
      if (previousActiveElement && typeof previousActiveElement.focus === 'function') {
        previousActiveElement.focus()
      }
    }
  }
)

onBeforeUnmount(() => {
  document.removeEventListener('keydown', handleKeyDown)
})
</script>

<template>
  <Teleport to="body">
    <div
      v-if="modelValue"
      class="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/60 backdrop-blur-xs"
      @click.self="close"
    >
      <div
        ref="dialogRef"
        role="dialog"
        aria-modal="true"
        :aria-label="title"
        tabindex="-1"
        :class="[
          'relative w-full rounded-[8px] bg-card border border-border shadow-xl overflow-hidden animate-in fade-in zoom-in-95 duration-150',
          maxWidth === 'sm' ? 'max-w-sm' : '',
          maxWidth === 'md' ? 'max-w-md' : '',
          maxWidth === 'lg' ? 'max-w-lg' : '',
          maxWidth === 'xl' ? 'max-w-xl' : ''
        ]"
      >
        <!-- Header -->
        <div class="flex items-center justify-between px-6 py-4 border-b border-border">
          <h2 class="text-base font-semibold text-fg">
            <slot name="title">{{ title }}</slot>
          </h2>
          <button
            type="button"
            aria-label="Đóng"
            class="p-1 rounded-[6px] text-muted-fg hover:text-fg hover:bg-muted transition-colors cursor-pointer focus-visible:outline-2 focus-visible:outline-ring"
            @click="close"
          >
            <X class="w-5 h-5" aria-hidden="true" />
          </button>
        </div>

        <!-- Body -->
        <div class="px-6 py-4 max-h-[80vh] overflow-y-auto">
          <slot />
        </div>

        <!-- Footer -->
        <div v-if="$slots.footer" class="flex items-center justify-end gap-3 px-6 py-3.5 bg-muted/30 border-t border-border">
          <slot name="footer" />
        </div>
      </div>
    </div>
  </Teleport>
</template>
